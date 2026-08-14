using System.Text.Json.Nodes;
using static SuiteTools.UserTools.ProcessExtensions;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private static readonly string _progressPopupExe = Path.Combine(_installedPopupDir, "SuiteProgressPopup.exe");
        private string? _progressFilePath;
        private bool _progressPopupStarted = false;
        private Task? _progressPopupTask;

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
            if (_suiteConfig.PopupSettings.HasGlobalPSCondition && IsPopupConditionExplicitlyNotMet(_suiteConfig.PopupSettings.GlobalPSCondition, "global popup condition"))
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
                WriteProgressStatus(0, $"Preparing {_suiteConfig.BuildSettings.Name}...", isComplete: false, isError: false);

                string progressArguments = $"--SuiteLogo \"{suiteLogoPath}\" --ProgressFile \"{progressFilePath}\" --LogFile \"{_logPath}\" --ProgressColour \"{_suiteConfig.PopupSettings.BackgroundColor.Value}\"";

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

                _log.WriteLog($"Launching progress popup: \"{_progressPopupExe}\" {progressArguments}", "Progress", Log.Severity.Info);

                _progressPopupTask = Task.Run(() =>
                {
                    try
                    {
                        StartProcessAsCurrentUser(_progressPopupExe, progressArguments, _installedPopupDir, true, true, TimeSpan.FromSeconds(30), true);
                    }
                    catch (Exception ex)
                    {
                        _log.WriteLog($"Progress popup process ended with error: {ex.Message}", "Progress", Log.Severity.Warning);
                    }
                });

                _progressPopupStarted = true;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to start progress popup: {ex.Message}", "Progress", Log.Severity.Error);
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

        private void UpdateProgress(int percentage, string? statusText)
        {
            if (!_progressPopupStarted)
                return;

            WriteProgressStatus(percentage, statusText, isComplete: false, isError: false);
        }

        private void CompleteProgressPopup(bool isError)
        {
            if (!_progressPopupStarted)
                return;

            string statusText = isError ? "Suite failed" : "Suite complete";
            WriteProgressStatus(100, statusText, isComplete: !isError, isError: isError);
        }

        // CompleteProgressPopup only signals the popup to close by writing to the progress file — the popup
        // notices asynchronously (its own poll interval, a completion linger, then shutdown) and exits on its
        // own time, or is force-terminated once its waitTimeout elapses (see StartProgressPopup). Wait for
        // that to actually happen before anything (e.g. CleanupDeferral) touches paths the popup still holds
        // open, rather than racing a fixed delay.
        private void WaitForProgressPopupExit(TimeSpan timeout)
        {
            if (_progressPopupTask == null)
                return;

            try
            {
                if (!_progressPopupTask.Wait(timeout))
                {
                    _log.WriteLog($"Progress popup did not exit within {timeout}; proceeding anyway", "Progress", Log.Severity.Warning);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Error waiting for progress popup to exit: {ex.Message}", "Progress", Log.Severity.Warning);
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
