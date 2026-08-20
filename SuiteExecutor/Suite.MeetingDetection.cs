using System.Diagnostics;
using static SuiteTools.UserTools.ProcessExtensions;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        // Exit codes reported by SuiteUserPopup.exe --CheckMeetingStatus: 0 not in use, 1 in use, 2 check
        // failed/inconclusive.
        private enum MeetingCheckExitType
        {
            NotInUse = 0,
            InUse = 1,
            CheckFailed = 2,
        }

        private const string _meetingWaitTaskPrefix = "SuiteMeetingWait_";
        private static readonly TimeSpan _meetingRecheckInterval = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _meetingMaxWait = TimeSpan.FromHours(4);

        // Rather than blocking this process for however long the meeting runs (which would tie up the
        // deployment tool's own execution timeout - see WaitWhileInMeeting's earlier, since-removed design),
        // this hands 1602 straight back and leaves a short-interval repeating scheduled task to keep
        // rechecking on its own. Each recheck re-invokes the Executor with --reminder (the same run mode the
        // deferral mechanism already uses), which skips the deferral gate and lands right back in
        // ExecutePopup - if the mic is still in use it exits 1602 again immediately and lets the task's own
        // repetition fire again shortly; once free, it cleans the task up and carries on to show the popup.
        // This means the person doesn't need the deployment tool's own re-evaluation cadence (which could be
        // hours) to happen to land between meetings - the recheck interval is under our control instead.
        private void ScheduleMeetingRecheckIfNeeded()
        {
            // Scoped by action, not just UpgradeCode: the family mutex (SuiteExecutor_{UpgradeCode}_{Action})
            // allows a Deployment and a Removal of the same suite family to run concurrently, and without the
            // action in the name a second schtasks /Create /F would silently overwrite the first's task,
            // orphaning whichever action's wait it clobbered.
            string taskName = $"{_meetingWaitTaskPrefix}{_suiteConfig.BuildSettings.UpgradeCode}_{_action}";
            if (DoesScheduledTaskExist(taskName))
            {
                return;
            }

            string? thisProcPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(thisProcPath))
            {
                _log.WriteLog("Unable to determine this executable path, cannot schedule a meeting recheck", "ExecPopup", Log.Severity.Warning);
                return;
            }

            try
            {
                string actionArg = _action switch
                {
                    SuiteAction.Deployment => "deploy",
                    SuiteAction.Removal => "remove",
                    SuiteAction.Rollback => "rollback",
                    _ => "deploy"
                };

                DateTime startTime = DateTime.Now + _meetingRecheckInterval;
                string startBoundary = startTime.ToString("yyyy-MM-ddTHH:mm:ss");
                // schtasks' schema requires TimeTrigger to carry its own EndBoundary even when Repetition
                // already bounds it via Duration/StopAtDurationEnd - omitting it fails XML validation.
                string endBoundary = (startTime + _meetingMaxWait).ToString("yyyy-MM-ddTHH:mm:ss");
                string recheckIntervalIso = $"PT{(int)_meetingRecheckInterval.TotalMinutes}M";
                string maxWaitIso = $"PT{(int)_meetingMaxWait.TotalHours}H";
                string descriptionName = System.Security.SecurityElement.Escape(_suiteConfig.BuildSettings.Name ?? "Suite");
                string commandPath = System.Security.SecurityElement.Escape(thisProcPath);
                string configArg = string.IsNullOrWhiteSpace(_suiteConfigPath)
                    ? string.Empty
                    : $@" --Config ""{System.Security.SecurityElement.Escape(_suiteConfigPath)}""";

                string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Suite meeting-pause recheck for {descriptionName}</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <StartBoundary>{startBoundary}</StartBoundary>
      <EndBoundary>{endBoundary}</EndBoundary>
      <Repetition>
        <Interval>{recheckIntervalIso}</Interval>
        <Duration>{maxWaitIso}</Duration>
        <StopAtDurationEnd>true</StopAtDurationEnd>
      </Repetition>
      <Enabled>true</Enabled>
    </TimeTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <DeleteExpiredTaskAfter>PT0S</DeleteExpiredTaskAfter>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{commandPath}""</Command>
      <Arguments>{actionArg}{configArg} --reminder</Arguments>
    </Exec>
  </Actions>
</Task>";

                string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{taskName}.xml");
                try
                {
                    File.WriteAllText(tempXmlPath, taskXml);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = SystemPaths.SchTasks,
                        Arguments = $"/Create /TN \"{taskName}\" /XML \"{tempXmlPath}\" /F",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using Process? process = Process.Start(psi);
                    if (process == null)
                        throw new InvalidOperationException("Failed to start schtasks.exe");

                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                        throw new InvalidOperationException($"schtasks /Create failed (exit {process.ExitCode}): {error}");

                    _log.WriteLog($"Scheduled meeting recheck task '{taskName}' — rechecking every {_meetingRecheckInterval.TotalMinutes:0} minute(s), up to {_meetingMaxWait.TotalHours:0} hours", "ExecPopup", Log.Severity.Info);
                }
                finally
                {
                    try { File.Delete(tempXmlPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to schedule meeting recheck: {ex.Message}", "ExecPopup", Log.Severity.Warning);
            }
        }

        private void RemoveMeetingRecheckTaskIfExists()
        {
            string taskName = $"{_meetingWaitTaskPrefix}{_suiteConfig.BuildSettings.UpgradeCode}_{_action}";
            if (!DoesScheduledTaskExist(taskName))
            {
                return;
            }

            DeleteScheduledTask(taskName);
            _log.WriteLog($"Removed meeting recheck task '{taskName}'", "ExecPopup", Log.Severity.Info);
        }

        // Delegates the actual detection to SuiteUserPopup.exe running as the interactive user (via a live
        // WASAPI audio-session check, see MicrophoneActivityDetector in that project), rather than reading
        // any local state SuiteExecutor itself has access to as SYSTEM - registry values can be edited by
        // the user, but a live "is there an open capture stream right now" check can't be spoofed the same
        // way. No window is shown for this check; it's a headless probe with a short timeout.
        private bool IsMicrophoneInUse(out string diagnostics)
        {
            diagnostics = string.Empty;
            try
            {
                TimeSpan checkTimeout = TimeSpan.FromSeconds(15);
                ImpersonatedProcessResult? result = StartProcessAsCurrentUser(_userPopExe, "--CheckMeetingStatus", _installedPopupDir, false, true, checkTimeout);
                if (result == null)
                {
                    diagnostics = "no active user session found";
                    return false;
                }

                switch ((MeetingCheckExitType?)result.ExitCode)
                {
                    case MeetingCheckExitType.InUse:
                        diagnostics = result.StandardOutput ?? string.Empty;
                        return true;
                    case MeetingCheckExitType.NotInUse:
                        diagnostics = result.StandardOutput ?? string.Empty;
                        return false;
                    default:
                        // ErrorMessage only covers launch failures (see StartProcessAsCurrentUser); the check
                        // process's own exception, if any, comes back via StandardError instead.
                        string failureDetail = !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.ErrorMessage;
                        _log.WriteLog($"Microphone check returned an unexpected result (exit code: {result.ExitCode}, detail: {failureDetail}), assuming not in use", "ExecPopup", Log.Severity.Warning);
                        diagnostics = $"check failed: {failureDetail}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to check microphone usage: {ex.Message}, assuming not in use", "ExecPopup", Log.Severity.Warning);
                diagnostics = $"check failed: {ex.Message}";
                return false;
            }
        }
    }
}
