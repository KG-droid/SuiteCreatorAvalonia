using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using System.Diagnostics;
using System.Threading;
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
                    RunAsSystem(scriptFilePath, workingDirectory);
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

        private void RunAsSystem(string scriptFilePath, string workingDirectory)
        {
            // UseShellExecute+Verb=runas is required for the elevation prompt, but that combination means
            // Windows can't give us redirected std handles for the child process. Instead, wrap the script
            // in a small launcher that redirects its own success stream (i.e. Write-Output) to a file, and
            // tail that file on a background thread so lines show up in the suite log while the script runs.
            string outputFilePath = Path.Combine(workingDirectory, $"{Guid.NewGuid()}.pswrite-output.log");
            string wrapperPath = Path.Combine(workingDirectory, $"{Guid.NewGuid()}.wrapper.ps1");
            string scriptArgsPart = string.IsNullOrWhiteSpace(ScriptArgs) ? string.Empty : " " + ScriptArgs;
            File.WriteAllText(wrapperPath, $"& \"{scriptFilePath}\"{scriptArgsPart} > \"{outputFilePath}\"");
            File.WriteAllText(outputFilePath, string.Empty);

            using CancellationTokenSource tailCts = new CancellationTokenSource();
            System.Threading.Tasks.Task tailTask = System.Threading.Tasks.Task.Run(() => TailOutputFile(outputFilePath, tailCts.Token));

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{wrapperPath}\"",
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
            finally
            {
                tailCts.Cancel();
                tailTask.Wait();
                try { File.Delete(wrapperPath); } catch { /* best-effort cleanup */ }
                try { File.Delete(outputFilePath); } catch { /* best-effort cleanup */ }
            }
        }

        // Polls the redirected output file for lines the running script has written via Write-Output and
        // logs each one as it appears. Re-reads the whole file each pass (rather than tracking a byte
        // offset) because StreamReader's internal buffering makes it unreliable to resume from a raw
        // FileStream.Position after a partial read.
        private void TailOutputFile(string filePath, CancellationToken token)
        {
            int linesLogged = 0;
            while (!token.IsCancellationRequested)
            {
                linesLogged = ReadAndLogNewLines(filePath, linesLogged, includeLastLine: false);
                token.WaitHandle.WaitOne(300);
            }
            // Final pass once the process has exited: the file is no longer being written to, so whatever
            // is left on the last line is complete and safe to log.
            ReadAndLogNewLines(filePath, linesLogged, includeLastLine: true);
        }

        private int ReadAndLogNewLines(string filePath, int linesAlreadyLogged, bool includeLastLine)
        {
            string[] lines;
            try
            {
                using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new StreamReader(fs);
                lines = reader.ReadToEnd().Split('\n');
            }
            catch (IOException)
            {
                // File may be momentarily locked by the writer; just try again next pass.
                return linesAlreadyLogged;
            }

            // The last split segment is only a complete line once we know the writer is done (includeLastLine).
            int upperBound = includeLastLine ? lines.Length : lines.Length - 1;
            for (int i = linesAlreadyLogged; i < upperBound; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (!string.IsNullOrEmpty(line))
                {
                    _log.WriteLog(line, ScriptName ?? "PowerShellExecEvent");
                }
            }
            return Math.Max(linesAlreadyLogged, upperBound);
        }

        private static readonly string PowerShellExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

        private void RunAsLoggedOnUser(string arguments, string workingDirectory)
        {
            List<SuiteTools.UserTools.ProcessExtensions.ImpersonatedProcessResult> results =
                SuiteTools.UserTools.ProcessExtensions.StartProcessAsAllUsers(
                    PowerShellExePath,
                    arguments,
                    workingDirectory,
                    false,
                    true,
                    (line, userName) => _log.WriteLog($"[{userName}] {line}", ScriptName ?? "PowerShellExecEvent"));

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
                _log.WriteLog($"PowerShell script completed successfully for user {result.UserName}");
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
