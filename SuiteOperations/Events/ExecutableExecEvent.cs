using Logger;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using System.Diagnostics;

namespace SuiteOperations.Events
{
    public partial class ExecutableExecEvent : ExecutableBase
    {
        private Log _log;

        public ExecutableExecEvent(Log log)
        {
            _log = log;
        }

        public ExecutableExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            RunExecutable();
        }

        private void RunExecutable()
        {
            // Parse WorkingDIR and Command
            string? workingDir = null;
            if (WorkingDIR != null && WorkingDIR.Count > 0)
            {
                List<string> dirParts = new List<string>();
                foreach (var v in WorkingDIR)
                {
                    string? val = v?.GetValue();
                    if (!string.IsNullOrWhiteSpace(val))
                        dirParts.Add(val);
                }
                workingDir = string.Join(" ", dirParts);
            }

            string? exePath = null;
            string? arguments = null;
            if (Command != null && Command.Count > 0)
            {
                List<string> cmdParts = new();
                foreach (VariableText v in Command)
                {
                    string? val = v?.GetValue();
                    if (!string.IsNullOrWhiteSpace(val))
                        cmdParts.Add(val);
                }
                if (cmdParts.Count > 0)
                {
                    string fullCommand = string.Join("",cmdParts);
                    ParseCommand(fullCommand, out exePath, out arguments);
                }
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                _log.WriteLog("Executable path is empty. Skipping execution.");
                throw new InvalidOperationException("Executable path is empty.");
            }

            if (!File.Exists(exePath))
            {
                if (ContinueOnNotFound)
                {
                    _log.WriteLog($"Executable not found: {exePath}, but config says to continue anyway.");
                    return;
                }
                else
                {
                    throw new FileNotFoundException($"Executable not found: {exePath}");
                } 
            }

            if (!SecureParams)
            {
                _log.WriteLog($"Running executable command: {exePath} {arguments ?? string.Empty}, working directory is: {workingDir ?? "(default)"}");
            }
            else
            {
                _log.WriteLog($"Running executable command (parameters hidden), working directory is: {workingDir ?? "(default)"}");
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = !string.IsNullOrWhiteSpace(workingDir) ? workingDir : Path.GetDirectoryName(exePath) ?? string.Empty,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(psi);
                if (process == null)
                {
                    _log.WriteLog($"Failed to start process: {exePath}", Sev: Log.Severity.Error);
                    if (!ContinueOnError)
                        throw new Exception($"Failed to start process: {exePath}");
                    return;
                }
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    _log.WriteLog($"Process exited with code {process.ExitCode}. Error: {error}", Sev: Log.Severity.Error);
                    if (!ContinueOnError)
                        throw new Exception($"Process exited with code {process.ExitCode}");
                    return;
                }
                else
                {
                    _log.WriteLog($"Process completed successfully. Output: {output}");
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Exception running executable: {ex.Message}", Sev: Log.Severity.Error);
                if (!ContinueOnError)
                    throw;
                return;
            }
        }

        private void ParseCommand(string fullCommand, out string? exePath, out string? arguments)
        {
            exePath = null;
            arguments = null;

            if (string.IsNullOrWhiteSpace(fullCommand))
            {
                return;
            }

            string trimmedCommand = fullCommand.Trim();

            if (trimmedCommand.StartsWith('"'))
            {
                int closingQuoteIndex = trimmedCommand.IndexOf('"', 1);
                if (closingQuoteIndex > 0)
                {
                    exePath = trimmedCommand.Substring(1, closingQuoteIndex - 1);

                    string remainingCommand = trimmedCommand.Substring(closingQuoteIndex + 1).Trim();
                    arguments = string.IsNullOrWhiteSpace(remainingCommand) ? null : remainingCommand;
                    return;
                }

                exePath = trimmedCommand.Trim('"');
                return;
            }

            int firstSpaceIndex = trimmedCommand.IndexOf(' ');
            if (firstSpaceIndex < 0)
            {
                exePath = trimmedCommand;
                return;
            }

            exePath = trimmedCommand.Substring(0, firstSpaceIndex);

            string remainingArguments = trimmedCommand.Substring(firstSpaceIndex + 1).Trim();
            arguments = string.IsNullOrWhiteSpace(remainingArguments) ? null : remainingArguments;

            _log.WriteLog("Parsed command: exePath = " + exePath + ", arguments = " + arguments);
        }
    }
}
