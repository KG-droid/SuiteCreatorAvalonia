using SuiteCreatorAvalonia.Models.Common;
using System.Diagnostics;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private const string _failSafeTaskPrefix = "SuiteFailSafe_";

        private void CreateFailSafeTask()
        {
            if (!_suiteConfig.BuildSettings.FailSafe)
                return;

            // Determine whether the popup uses logon or startup trigger based on LoggedOffAction
            bool useLogonTrigger = _suiteConfig.PopupSettings.ShowPopupWarning &&
                                   _suiteConfig.PopupSettings.LoggedOffAction == PopupAction.Skip;

            CreateRecoveryTask(useLogonTrigger);
        }

        // RestartOnFailure triggers its own restart to retry, so it needs a recovery task to survive that
        // restart regardless of whether the general FailSafe (interruption-recovery) option is enabled —
        // otherwise the retry would be stranded after the reboot. Always use a boot trigger here since the
        // restart is deliberate and immediate, not tied to how the popup's logged-off action was configured.
        private void CreateRestartRetryTask()
        {
            CreateRecoveryTask(useLogonTrigger: false);
        }

        private void CreateRecoveryTask(bool useLogonTrigger)
        {
            string upgradeCode = _suiteConfig.BuildSettings.UpgradeCode.ToString();
            string taskName = _failSafeTaskPrefix + upgradeCode;
            string? thisProcPath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(thisProcPath))
            {
                _log.WriteLog("FailSafe: Unable to determine executable path, skipping task creation.", "FailSafe", Log.Severity.Warning);
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

                string trigger = useLogonTrigger
                    ? "<LogonTrigger><Enabled>true</Enabled></LogonTrigger>"
                    : "<BootTrigger><Delay>PT1M</Delay><Enabled>true</Enabled></BootTrigger>";

                // XML-encode any value that originates from config or the environment before embedding it in
                // the task definition, so characters like & < > can't break out of the element or restructure the task.
                string descriptionName = System.Security.SecurityElement.Escape(_suiteConfig.BuildSettings.Name ?? "Suite");
                string commandPath = System.Security.SecurityElement.Escape(thisProcPath);
                string configArg = string.IsNullOrWhiteSpace(_suiteConfigPath)
                    ? string.Empty
                    : $@" --Config ""{System.Security.SecurityElement.Escape(_suiteConfigPath)}""";

                string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Suite fail-safe recovery for {descriptionName}</Description>
  </RegistrationInfo>
  <Triggers>
    {trigger}
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
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{commandPath}""</Command>
      <Arguments>{actionArg}{configArg} --failsafe</Arguments>
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

                    _log.WriteLog($"FailSafe scheduled task '{taskName}' created.", "FailSafe", Log.Severity.Info);
                }
                finally
                {
                    try { File.Delete(tempXmlPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"FailSafe: Failed to create scheduled task: {ex.Message}", "FailSafe", Log.Severity.Warning);
            }
        }

        private void RemoveFailSafeTask()
        {
            // Not gated on BuildSettings.FailSafe: RestartOnFailure can also create this recovery task (to
            // survive its own restart) even when the general FailSafe option is off, so cleanup must always
            // be attempted. It's a safe no-op via the /Query check below if no task was ever created.
            string upgradeCode = _suiteConfig.BuildSettings.UpgradeCode.ToString();
            string taskName = _failSafeTaskPrefix + upgradeCode;

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = SystemPaths.SchTasks,
                    Arguments = $"/Query /TN \"{taskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process? queryProcess = Process.Start(psi))
                {
                    if (queryProcess == null) return;
                    queryProcess.WaitForExit();
                    if (queryProcess.ExitCode != 0)
                        return; // Task doesn't exist, nothing to remove
                }

                ProcessStartInfo deletePsi = new ProcessStartInfo
                {
                    FileName = SystemPaths.SchTasks,
                    Arguments = $"/Delete /TN \"{taskName}\" /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process? deleteProcess = Process.Start(deletePsi);
                if (deleteProcess == null) return;

                string error = deleteProcess.StandardError.ReadToEnd();
                deleteProcess.WaitForExit();

                if (deleteProcess.ExitCode != 0)
                    _log.WriteLog($"FailSafe: Failed to delete scheduled task '{taskName}': {error}", "FailSafe", Log.Severity.Warning);
                else
                    _log.WriteLog($"FailSafe scheduled task '{taskName}' removed.", "FailSafe", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"FailSafe: Error removing scheduled task: {ex.Message}", "FailSafe", Log.Severity.Warning);
            }
        }
    }
}
