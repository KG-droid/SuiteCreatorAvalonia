using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using System.Diagnostics;
using Environment = System.Environment;

namespace SuiteOperations.Events
{
    public partial class PowerShellExecEvent : PowerShellBase
    {
        private Log _log;

        public PowerShellExecEvent(Log log)
        {
            _log = log;
        }

        public PowerShellExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            RunPowerShellScript();
        }

        public void RunPowerShellScript()
        {
            _log.WriteLog($"Running PowerShell: {ScriptName}");

            // Prefer an actual script file on disk (the normal deployed case, resolved by SuiteExecutor
            // path resolution) so the script is launched with -File rather than inlined into -Command,
            // which is fragile for anything beyond trivial scripts and is required to run it as another user.
            string? scriptFilePath = ScriptPath;
            bool isTempScript = false;
            if (string.IsNullOrWhiteSpace(scriptFilePath) || !File.Exists(scriptFilePath))
            {
                if (ScriptDoc == null || string.IsNullOrWhiteSpace(ScriptDoc.Text))
                {
                    _log.WriteLog("No PowerShell script provided.", "PowerShellExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException("No PowerShell script provided.");
                }

                string tempDir = !string.IsNullOrWhiteSpace(SupportFilesDir) ? SupportFilesDir : Path.GetTempPath();
                scriptFilePath = Path.Combine(tempDir, $"{Guid.NewGuid()}.ps1");
                File.WriteAllText(scriptFilePath, ScriptDoc.Text);
                isTempScript = true;
            }

            string workingDirectory = !string.IsNullOrWhiteSpace(SupportFilesDir)
                ? SupportFilesDir
                : Path.GetDirectoryName(scriptFilePath) ?? string.Empty;

            string arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFilePath}\"{(string.IsNullOrWhiteSpace(ScriptArgs) ? string.Empty : " " + ScriptArgs)}";

            try
            {
                if (Context == Contexts.System)
                {
                    RunAsSystem(arguments, workingDirectory);
                }
                else // User context: run as the actual logged-on user(s), not the SYSTEM service account
                {
                    RunAsLoggedOnUser(arguments, workingDirectory);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Exception running PowerShell script: {ex.Message}", "PowerShellExecEvent", Log.Severity.Error);
                throw;
            }
            finally
            {
                if (isTempScript)
                {
                    try { File.Delete(scriptFilePath); } catch { /* best-effort cleanup */ }
                }
            }
        }

        private void RunAsSystem(string arguments, string workingDirectory)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process? process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start PowerShell process.");
            }
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                _log.WriteLog($"PowerShell script exited with code {process.ExitCode}.", "PowerShellExecEvent", Log.Severity.Error);
                throw new InvalidOperationException($"PowerShell process exited with code {process.ExitCode}.");
            }
            _log.WriteLog($"PowerShell script completed successfully, exit code {process.ExitCode}");
        }

        private static readonly string PowerShellExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

        private void RunAsLoggedOnUser(string arguments, string workingDirectory)
        {
            List<SuiteTools.UserTools.ProcessExtensions.ImpersonatedProcessResult> results =
                SuiteTools.UserTools.ProcessExtensions.StartProcessAsAllUsers(PowerShellExePath, arguments, workingDirectory, false, true);

            List<string> errors = new();
            foreach (SuiteTools.UserTools.ProcessExtensions.ImpersonatedProcessResult result in results)
            {
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                {
                    errors.Add($"User {result.UserName}: {result.ErrorMessage}");
                    continue;
                }
                if (result.ExitCode != 0)
                {
                    errors.Add($"User {result.UserName}: PowerShell process exited with code {result.ExitCode}. {result.StandardError}");
                    continue;
                }
                _log.WriteLog($"PowerShell script completed successfully for user {result.UserName}, output was: {result.StandardOutput}");
            }

            if (errors.Count > 0)
            {
                string error = string.Join(Environment.NewLine, errors);
                _log.WriteLog($"PowerShell script error: {error}", "PowerShellExecEvent", Log.Severity.Error);
                throw new InvalidOperationException($"PowerShell script error: {error}");
            }
        }
    }
}
