using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading;
using static SuiteTools.UserTools.ProcessExtensions;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private static readonly string _progressPopupExe = Path.Combine(_installedPopupDir, "SuiteProgressPopup.exe");
        private string? _progressFilePath;
        private bool _progressPopupStarted = false;
        private string _progressLaunchArguments = string.Empty;

        // UpdateProgress is called synchronously from the main package-installation thread on every stage
        // transition (see Suite.Execution.cs), so it must never block that thread on disk I/O - a slow disk,
        // AV real-time scan, or transient contention with the popup's own read would otherwise stall the
        // actual install. These fields back a small coalescing queue: the caller just stashes the latest
        // percentage/text and returns immediately; at most one background Task drains it, always writing
        // only the newest value (intermediate ticks are cheap to drop - a progress bar just catches up).
        private readonly object _progressUpdateLock = new object();
        private (int Percentage, string? StatusText)? _pendingProgressUpdate;
        private Task? _progressDrainTask;

        // Watchdog: separate from the update-drain queue above on purpose - it has to keep checking on a
        // timer even while no progress updates are flowing (e.g. a single slow package install stage), which
        // the drain queue alone would never wake up for. _progressProcessLock is the single point of
        // serialization between this background watchdog and the main thread's own teardown
        // (WaitForProgressPopupExit/SignalProgressShutdown): whichever side is actively killing/relaunching
        // the tracked process holds the lock for that whole operation, and _progressShuttingDown - checked
        // inside the lock at every decision point - is what stops the watchdog from ever spawning a new
        // popup after the main thread has decided the suite is done, no matter what it's in the middle of.
        private static readonly TimeSpan _progressWatchdogPollInterval = TimeSpan.FromSeconds(5);
        private const int _progressUnresponsiveThreshold = 3; // consecutive failed polls before acting
        private readonly object _progressProcessLock = new object();
        private Process? _progressProcess;
        private bool _progressShuttingDown;
        private CancellationTokenSource? _progressWatchdogCts;
        private Task? _progressWatchdogTask;

        private void StartProgressPopup()
        {
            if (!_suiteConfig.PopupSettings.ShowProgress)
                return;

            // There's no real interactive user to show progress to during ESP, so just skip it - no
            // ESPAction to honor here, that's specific to the warning popup's whole-suite skip behavior.
            if (IsDeviceInEsp())
            {
                _log.WriteLog("Device is in ESP (Autopilot Enrollment Status Page / initial setup), skipping progress popup", "Progress", Log.Severity.Info);
                return;
            }

            // The popup condition scripts are shared across both popup types, not just the warning popup -
            // if the condition explicitly says not to show a popup, that applies to progress too.
            if (_suiteConfig.HasGlobalPSCondition && IsPopupConditionExplicitlyNotMet(_suiteConfig.GlobalPSCondition, "global popup condition"))
            {
                _log.WriteLog("Global popup condition was not met, skipping progress popup", "Progress", Log.Severity.Info);
                return;
            }
            if (_suiteConfig.PopupSettings.HasPSCondition && IsPopupConditionExplicitlyNotMet(_suiteConfig.PopupSettings.PSCondition, "popup condition"))
            {
                _log.WriteLog("Popup condition was not met, skipping progress popup", "Progress", Log.Severity.Info);
                return;
            }

            try
            {
                string popupDir = Path.Combine(_suiteRootDir, "Popup");
                string suiteLogoPath = Path.Combine(popupDir, "SuiteLogo.png");
                string progressFilePath = Path.Combine(popupDir, "progress.json");

                if (!Directory.Exists(popupDir))
                {
                    _log.WriteLog($"Progress popup is enabled but Popup directory does not exist: {popupDir}. Skipping progress popup.", "Progress", Log.Severity.Warning);
                    return;
                }
                if (!File.Exists(suiteLogoPath))
                {
                    _log.WriteLog($"Progress popup is enabled but suite logo was not found: {suiteLogoPath}. Skipping progress popup.", "Progress", Log.Severity.Warning);
                    return;
                }
                if (!File.Exists(_progressPopupExe))
                {
                    _log.WriteLog($"Progress popup is enabled but SuiteProgressPopup.exe was not found: {_progressPopupExe}. Skipping progress popup.", "Progress", Log.Severity.Warning);
                    return;
                }

                _progressFilePath = progressFilePath;
                WriteProgressStatusSync(0, $"Preparing {_suiteConfig.BuildSettings.Name}...", isComplete: false, isError: false);

                string progressArguments = $"--SuiteLogo \"{suiteLogoPath}\" --ProgressFile \"{progressFilePath}\" --LogFile \"{_logPath}\" --ProgressColour \"{_suiteConfig.CompanyLogoBackgroundColor}\"";

                if (_suiteConfig.PopupSettings.LockdownEnabled)
                {
                    string? companyLogoPath = ResolveCompanyLogoPath(popupDir);
                    int maxMinutes = _suiteConfig.PopupSettings.LockdownMaxMinutes > 0 ? _suiteConfig.PopupSettings.LockdownMaxMinutes : 30;

                    progressArguments += $" --Lockdown --MaxMinutes \"{maxMinutes}\"";
                    if (companyLogoPath != null)
                        progressArguments += $" --CompanyLogo \"{companyLogoPath}\"";
                    if (!string.IsNullOrWhiteSpace(_suiteConfig.PopupSettings.LockdownMessage))
                        progressArguments += $" --LockdownMessage \"{EscapeArgument(_suiteConfig.PopupSettings.LockdownMessage)}\"";
                }

                _progressLaunchArguments = progressArguments;
                _log.WriteLog($"Launching progress popup: \"{_progressPopupExe}\" {progressArguments}", "Progress", Log.Severity.Info);

                Process? initialProcess = LaunchProgressPopupProcess();
                if (initialProcess == null)
                {
                    _log.WriteLog("Failed to launch the progress popup.", "Progress", Log.Severity.Warning);
                    return;
                }

                _progressProcess = initialProcess;
                _progressWatchdogCts = new CancellationTokenSource();
                _progressWatchdogTask = Task.Run(() => RunProgressWatchdogAsync(_progressWatchdogCts.Token));

                _progressPopupStarted = true;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to start progress popup: {ex.Message}", "Progress", Log.Severity.Error);
            }
        }

        // Fire-and-forget launch (wait:false) so the caller gets the PID back immediately instead of blocking
        // until the popup exits - StartProcessAsCurrentUser still ties the process to SuiteExecutor's own
        // lifetime via a Job Object (killWithParent:true), so it's cleaned up even if this process is killed
        // outright before ever reaching its own teardown code.
        private Process? LaunchProgressPopupProcess()
        {
            ImpersonatedProcessResult? result = StartProcessAsCurrentUser(_progressPopupExe, _progressLaunchArguments, _installedPopupDir, true, false, null, true);
            if (result == null || result.ProcessId <= 0)
                return null;

            try
            {
                return Process.GetProcessById(result.ProcessId);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to attach to launched progress popup process (PID {result.ProcessId}): {ex.Message}", "Progress", Log.Severity.Warning);
                return null;
            }
        }

        // Escapes a value for embedding inside a double-quoted Win32 command-line argument
        // (CommandLineToArgvW rules: a literal quote must be backslash-escaped).
        private static string EscapeArgument(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string? ResolveCompanyLogoPath(string popupDir)
        {
            foreach (string ext in new[] { ".png", ".gif" })
            {
                string candidate = Path.Combine(popupDir, $"CompanyLogo{ext}");
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        // Non-blocking: stashes the latest value and returns immediately. At most one drain Task is ever
        // running - if one is already in flight, this just updates what it'll pick up next, rather than
        // spawning another. See the field comments above for why this needs to not block the caller.
        private void UpdateProgress(int percentage, string? statusText)
        {
            if (!_progressPopupStarted)
                return;

            lock (_progressUpdateLock)
            {
                _pendingProgressUpdate = (percentage, statusText);
                if (_progressDrainTask != null && !_progressDrainTask.IsCompleted)
                    return;

                _progressDrainTask = Task.Run(DrainPendingProgressUpdates);
            }
        }

        private void DrainPendingProgressUpdates()
        {
            while (true)
            {
                (int Percentage, string? StatusText) update;
                lock (_progressUpdateLock)
                {
                    if (_pendingProgressUpdate == null)
                        return;
                    update = _pendingProgressUpdate.Value;
                    _pendingProgressUpdate = null;
                }

                WriteProgressStatus(update.Percentage, update.StatusText, isComplete: false, isError: false);
            }
        }

        private void CompleteProgressPopup(bool isError)
        {
            if (!_progressPopupStarted)
                return;

            // Signal shutdown BEFORE writing the final status: the watchdog must not relaunch the popup
            // from this point on under any circumstances, including a kill-and-relaunch it's already in the
            // middle of. This is the only call to CompleteProgressPopup on the failure path (Suite.cs's
            // catch block goes straight from here to Environment.Exit, never through
            // WaitForProgressPopupExit), so this is the sole place both paths are guaranteed to pass through.
            SignalProgressShutdown();

            string statusText = isError ? "Suite failed" : "Suite complete";
            WriteProgressStatusSync(100, statusText, isComplete: !isError, isError: isError);
        }

        // Used only for the initial "Preparing..." write and the final complete/error write - both need to
        // reliably land on disk before returning (unlike UpdateProgress's frequent, best-effort ticks), and
        // in particular the final write must not be overtaken by a still-draining earlier UpdateProgress
        // call landing after it. Clears anything queued and waits for any in-flight drain to finish first,
        // so this write is guaranteed to be the last one on disk.
        private void WriteProgressStatusSync(int percentage, string? statusText, bool isComplete, bool isError)
        {
            Task? drainTask;
            lock (_progressUpdateLock)
            {
                _pendingProgressUpdate = null;
                drainTask = _progressDrainTask;
            }

            try { drainTask?.Wait(); }
            catch (Exception ex) { _log.WriteLog($"Error waiting for pending progress update to drain: {ex.Message}", "Progress", Log.Severity.Warning); }

            WriteProgressStatus(percentage, statusText, isComplete, isError);
        }

        // Marks the popup's lifetime as over: the watchdog checks this (inside _progressProcessLock) before
        // ever killing-and-relaunching, and bails out instead if it's set - including immediately after
        // relaunching, in case shutdown was signalled mid-relaunch. Safe to call multiple times.
        private void SignalProgressShutdown()
        {
            lock (_progressProcessLock)
            {
                _progressShuttingDown = true;
            }
            try { _progressWatchdogCts?.Cancel(); } catch { }
        }

        private void WaitForProgressPopupExit(TimeSpan timeout)
        {
            if (!_progressPopupStarted)
                return;

            // CompleteProgressPopup already called SignalProgressShutdown, so the watchdog will not relaunch
            // from here on - it's now safe to wait for (and, if necessary, force) the exit of whatever's
            // currently tracked without racing a fresh relaunch back into existence.
            Process? current;
            lock (_progressProcessLock)
            {
                current = _progressProcess;
            }

            if (current != null)
            {
                try
                {
                    if (!current.HasExited && !current.WaitForExit((int)Math.Max(0, timeout.TotalMilliseconds)))
                    {
                        _log.WriteLog($"Progress popup did not exit within {timeout}; forcing it closed.", "Progress", Log.Severity.Warning);
                        try { current.Kill(entireProcessTree: true); }
                        catch (Exception ex) { _log.WriteLog($"Failed to force-close the progress popup: {ex.Message}", "Progress", Log.Severity.Warning); }
                    }
                }
                catch (Exception ex)
                {
                    _log.WriteLog($"Error waiting for progress popup to exit: {ex.Message}", "Progress", Log.Severity.Warning);
                }
                finally
                {
                    try { current.Dispose(); } catch { }
                }
            }

            // Give the watchdog loop a brief moment to notice cancellation and actually stop, so nothing is
            // left touching _progressProcess after this returns.
            try { _progressWatchdogTask?.Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { _log.WriteLog($"Error waiting for progress watchdog to stop: {ex.Message}", "Progress", Log.Severity.Warning); }
        }

        private async Task RunProgressWatchdogAsync(CancellationToken token)
        {
            int unresponsiveStreak = 0;
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(_progressWatchdogPollInterval, token); }
                catch (OperationCanceledException) { break; }
                if (token.IsCancellationRequested)
                    break;

                Process? current;
                lock (_progressProcessLock)
                {
                    if (_progressShuttingDown)
                        break;
                    current = _progressProcess;
                }
                if (current == null)
                    continue;

                bool exited;
                bool responding;
                try
                {
                    current.Refresh();
                    exited = current.HasExited;
                    responding = exited || current.Responding;
                }
                catch (InvalidOperationException)
                {
                    // The Process object is no longer valid for querying - treat as gone.
                    exited = true;
                    responding = false;
                }
                catch (Exception ex)
                {
                    _log.WriteLog($"Progress popup watchdog check failed: {ex.Message}", "Progress", Log.Severity.Warning);
                    continue; // Inconclusive - don't act on this poll.
                }

                if (exited)
                {
                    _log.WriteLog("Progress popup process has exited unexpectedly; relaunching.", "Progress", Log.Severity.Warning);
                    unresponsiveStreak = 0;
                    RestartProgressPopupProcess(token);
                    continue;
                }

                if (responding)
                {
                    unresponsiveStreak = 0;
                    continue;
                }

                unresponsiveStreak++;
                _log.WriteLog($"Progress popup appears unresponsive ({unresponsiveStreak}/{_progressUnresponsiveThreshold})", "Progress", Log.Severity.Warning);
                if (unresponsiveStreak < _progressUnresponsiveThreshold)
                    continue;

                unresponsiveStreak = 0;
                _log.WriteLog("Progress popup unresponsive for too long; killing and relaunching.", "Progress", Log.Severity.Warning);
                RestartProgressPopupProcess(token);
            }
        }

        // Kills whatever's currently tracked and relaunches it, but only if the suite hasn't finished in the
        // meantime - checked before the kill, and again (still holding the lock across the relaunch itself)
        // right before handing the new process over to _progressProcess, so a shutdown signalled mid-restart
        // can never result in a fresh popup being left behind after the main thread thinks it's done.
        private void RestartProgressPopupProcess(CancellationToken token)
        {
            Process? staleProcess;
            lock (_progressProcessLock)
            {
                if (_progressShuttingDown || token.IsCancellationRequested)
                    return;
                staleProcess = _progressProcess;
                _progressProcess = null; // Claimed - the main thread's own teardown won't also act on it now.
            }

            try
            {
                if (staleProcess != null && !staleProcess.HasExited)
                    staleProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to kill unresponsive progress popup: {ex.Message}", "Progress", Log.Severity.Warning);
            }
            finally
            {
                try { staleProcess?.Dispose(); } catch { }
            }

            lock (_progressProcessLock)
            {
                if (_progressShuttingDown || token.IsCancellationRequested)
                    return; // The suite finished while the old process was being killed - don't spawn a new one.

                Process? relaunched = LaunchProgressPopupProcess();
                if (relaunched == null)
                {
                    _log.WriteLog("Failed to relaunch progress popup after it became unresponsive.", "Progress", Log.Severity.Warning);
                    return;
                }

                // Re-check once more before publishing it: LaunchProgressPopupProcess (impersonation +
                // CreateProcessAsUser) can take a moment, during which the main thread could have finished -
                // but the lock has been held for the whole call, so nothing else could have raced this.
                if (_progressShuttingDown || token.IsCancellationRequested)
                {
                    try { relaunched.Kill(entireProcessTree: true); } catch { }
                    try { relaunched.Dispose(); } catch { }
                    return;
                }

                _progressProcess = relaunched;
                _log.WriteLog("Progress popup relaunched after becoming unresponsive.", "Progress", Log.Severity.Info);
            }
        }

        private void RunWithEstimatedProgress(int estimatedSeconds, double startPercent, double endPercent, string? statusText, Action action)
        {
            if (estimatedSeconds <= 0 || !_progressPopupStarted)
            {
                action();
                return;
            }

            using CancellationTokenSource cts = new();
            Task tickTask = Task.Run(() => TickEstimatedProgress(estimatedSeconds, startPercent, endPercent, statusText, cts.Token));
            try
            {
                action();
            }
            finally
            {
                cts.Cancel();
                try
                {
                    tickTask.Wait();
                }
                catch (Exception ex)
                {
                    _log.WriteLog($"Estimated progress ticking ended with error: {ex.Message}", "Progress", Log.Severity.Warning);
                }
            }
        }

        // Capped short of endPercent so an under-estimate doesn't leave the bar visually stalled at the
        // window's ceiling before the process actually finishes - the next stage boundary (or the final
        // completion write) snaps progress forward regardless of where ticking left off.
        private void TickEstimatedProgress(int estimatedSeconds, double startPercent, double endPercent, string? statusText, CancellationToken token)
        {
            double cappedEnd = startPercent + (endPercent - startPercent) * 0.95;
            Stopwatch sw = Stopwatch.StartNew();
            while (!token.IsCancellationRequested)
            {
                double ratio = Math.Clamp(sw.Elapsed.TotalSeconds / estimatedSeconds, 0, 1);
                double percent = startPercent + (cappedEnd - startPercent) * ratio;
                UpdateProgress((int)Math.Round(percent), statusText);
                if (token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500)))
                    break;
            }
        }

        private void WriteProgressStatus(int percentage, string? statusText, bool isComplete, bool isError)
        {
            if (string.IsNullOrWhiteSpace(_progressFilePath))
                return;

            try
            {
                JsonObject json = new JsonObject
                {
                    ["Percentage"] = Math.Clamp(percentage, 0, 100),
                    ["StatusText"] = statusText,
                    ["ProductName"] = _suiteConfig.BuildSettings.Name,
                    ["IsComplete"] = isComplete,
                    ["IsError"] = isError
                };

                File.WriteAllText(_progressFilePath, json.ToJsonString());
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to write progress status: {ex.Message}", "Progress", Log.Severity.Warning);
            }
        }
    }
}
