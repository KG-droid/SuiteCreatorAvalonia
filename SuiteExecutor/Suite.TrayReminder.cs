using System.Diagnostics;
using Log = Logger.Log;
using static SuiteTools.UserTools.ProcessExtensions;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        // "Run now" tray icon: shown for the lifetime of a pending deferral so a user who picked a late
        // time and changed their mind isn't stuck waiting for the scheduled reminder. It's a purely
        // best-effort addition on top of the deferral itself — the popup config path, the SuiteReminder_*
        // task, and the deferral flow all work exactly as before if any part of this fails, so every step
        // here is wrapped and logged rather than allowed to fail the deferral that's already in progress.
        private const string _trayReminderTaskPrefix = "SuiteTrayReminder_";

        private string GetTrayReminderTaskName() => _trayReminderTaskPrefix + _suiteConfig.BuildSettings.UpgradeCode.ToString();

        // Called right after ScheduleReminder() creates the SuiteReminder_* task. Widens that task's
        // permissions so the (unelevated) tray icon can trigger it on demand, registers a logon-triggered
        // task so the tray icon reappears if the user logs off/reboots before the deferred time, and
        // launches it immediately for the session that's deferring right now.
        private void EnableTrayRunNow(string suiteLogoPath)
        {
            string reminderTaskName = GetDeferralTaskName();

            try { GrantInteractiveTaskRunPermission(reminderTaskName); }
            catch (Exception ex) { _log.WriteLog($"TrayReminder: Failed to grant run permission on '{reminderTaskName}': {ex.Message}", "TrayReminder", Log.Severity.Warning); }

            try { CreateTrayReminderTask(reminderTaskName, suiteLogoPath); }
            catch (Exception ex) { _log.WriteLog($"TrayReminder: Failed to create logon-trigger tray task: {ex.Message}", "TrayReminder", Log.Severity.Warning); }

            try { LaunchTrayReminderNow(reminderTaskName, suiteLogoPath); }
            catch (Exception ex) { _log.WriteLog($"TrayReminder: Failed to launch tray icon for the current session: {ex.Message}", "TrayReminder", Log.Severity.Warning); }
        }

        // The SuiteReminder_* task is registered by this elevated process (SYSTEM/HighestAvailable
        // Principal), so by default only admins can start it on demand — a standard user's schtasks /Run
        // against it fails with access denied. Widen its security descriptor to also grant the interactive
        // Users group Read+Execute (start), without touching who it actually runs as. There's no schtasks.exe
        // switch for this — it's only settable via the Task Scheduler COM API — so it's done through a short
        // PowerShell script rather than in-process COM interop, since SuiteExecutor is Native AOT-published
        // and can't use the dynamic/reflection-based COM marshalling that API needs.
        private void GrantInteractiveTaskRunPermission(string taskName)
        {
            string script = """
                param([Parameter(Mandatory=$true)][string]$TaskName)
                $ErrorActionPreference = 'Stop'
                $service = New-Object -ComObject 'Schedule.Service'
                $service.Connect()
                $folder = $service.GetFolder('\')
                $task = $folder.GetTask($TaskName)
                # D:P protects the DACL from inheritance. SY/BA keep SYSTEM and Administrators at full
                # control; BU (Builtin Users) gets generic read+execute so a standard user can query and
                # start (but not modify or delete) the task.
                $sddl = 'D:P(A;;GRGX;;;BU)(A;;GA;;;SY)(A;;GA;;;BA)'
                $task.SetSecurityDescriptor($sddl, 0)
                """;

            string tempScriptPath = Path.Combine(Path.GetTempPath(), $"grant_task_run_{Guid.NewGuid()}.ps1");
            try
            {
                File.WriteAllText(tempScriptPath, script);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = SystemPaths.PowerShell,
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\" -TaskName \"{taskName}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null)
                    throw new InvalidOperationException("Failed to start PowerShell process.");

                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Granting run permission failed (exit {process.ExitCode}): {error}");

                _log.WriteLog($"TrayReminder: Granted interactive users run permission on '{taskName}'.", "TrayReminder", Log.Severity.Info);
            }
            finally
            {
                try { File.Delete(tempScriptPath); } catch { }
            }
        }

        // Runs for whichever user is interactively logged on (Principal GroupId S-1-5-4 = INTERACTIVE,
        // RunLevel LeastPrivilege) rather than a specific user or SYSTEM — it just needs to show a tray
        // icon in whatever session is active, unelevated, without a stored password or a UAC prompt.
        private void CreateTrayReminderTask(string reminderTaskName, string suiteLogoPath)
        {
            string taskName = GetTrayReminderTaskName();

            if (string.IsNullOrWhiteSpace(_userPopExe) || !File.Exists(_userPopExe))
            {
                _log.WriteLog($"TrayReminder: SuiteUserPopup.exe not found at '{_userPopExe}', skipping tray task creation.", "TrayReminder", Log.Severity.Warning);
                return;
            }

            string descriptionName = System.Security.SecurityElement.Escape(_suiteConfig.BuildSettings.Name ?? "Suite");
            string commandPath = System.Security.SecurityElement.Escape(_userPopExe);
            string escapedReminderTask = System.Security.SecurityElement.Escape(reminderTaskName);
            string escapedSuiteName = System.Security.SecurityElement.Escape(_suiteConfig.BuildSettings.Name ?? "Suite");
            string escapedLogoPath = System.Security.SecurityElement.Escape(suiteLogoPath);

            string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Suite deferral ""Run now"" tray icon for {descriptionName}</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <GroupId>S-1-5-4</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{commandPath}""</Command>
      <Arguments>--Tray --ReminderTask ""{escapedReminderTask}"" --SuiteName ""{escapedSuiteName}"" --SuiteLogo ""{escapedLogoPath}""</Arguments>
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

                _log.WriteLog($"TrayReminder scheduled task '{taskName}' created.", "TrayReminder", Log.Severity.Info);
            }
            finally
            {
                try { File.Delete(tempXmlPath); } catch { }
            }
        }

        // Launches the tray icon right now for whichever user is in the active session — the logon-trigger
        // task above only covers a future logon, but the user who's deferring right now is already logged
        // on and shouldn't have to log off and back on just to get the "run now" option.
        private void LaunchTrayReminderNow(string reminderTaskName, string suiteLogoPath)
        {
            if (string.IsNullOrWhiteSpace(_userPopExe) || !File.Exists(_userPopExe))
                return;

            string popupDir = Path.Combine(_suiteRootDir, "Popup");
            string suiteName = _suiteConfig.BuildSettings.Name ?? "Suite";
            string trayArguments = $"--Tray --ReminderTask \"{reminderTaskName}\" --SuiteName \"{suiteName}\" --SuiteLogo \"{suiteLogoPath}\"";

            // Fire-and-forget, and deliberately NOT tied to this process's lifetime (killWithParent stays
            // false): SuiteExecutor exits immediately after this via Environment.Exit(1602), which would
            // otherwise tear the tray icon down the instant it appeared.
            StartProcessAsCurrentUser(_userPopExe, trayArguments, popupDir, isWindowVisibleToUser: false, wait: false);

            _log.WriteLog("TrayReminder: Launched tray icon for the current session.", "TrayReminder", Log.Severity.Info);
        }

        private void RemoveTrayReminderTask()
        {
            string taskName = GetTrayReminderTaskName();
            if (DoesScheduledTaskExist(taskName))
            {
                DeleteScheduledTask(taskName);
                _log.WriteLog($"Cleaned up tray reminder scheduled task '{taskName}'", "TrayReminder", Log.Severity.Info);
            }
        }
    }
}
