using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using System.Diagnostics;

namespace SuiteOperations.Events
{
    public partial class DriverExecEvent : Driver
    {
        private const int RebootRequiredExitCode = 3010;

        private Log _log;

        public DriverExecEvent(Log log)
        {
            _log = log;
        }

        public DriverExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            switch (Action)
            {
                case DriverAction.Install:
                    InstallDriver();
                    break;
                case DriverAction.Remove:
                    RemoveDriver();
                    break;
                default:
                    _log.WriteLog($"Unknown driver action: {Action}");
                    throw new InvalidOperationException($"Unknown driver action: {Action}");
            }
        }

        public void InstallDriver()
        {
            _log.WriteLog($"Installing driver: {InfPath}");
            try
            {
                if (string.IsNullOrWhiteSpace(InfPath))
                {
                    _log.WriteLog("Driver .inf file path is not specified.", "DriverExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException("Driver .inf file path is not specified.");
                }
                if (!File.Exists(InfPath))
                {
                    _log.WriteLog($"Driver .inf file was not found: {InfPath}", "DriverExecEvent", Log.Severity.Error);
                    throw new FileNotFoundException($"Driver .inf file was not found: {InfPath}", InfPath);
                }
                (int exitCode, string output) = RunPnpUtil($"/add-driver \"{InfPath}\" /install");
                if (exitCode == RebootRequiredExitCode)
                {
                    _log.WriteLog("Driver installed successfully, but a reboot is required to complete the installation.", "DriverExecEvent", Log.Severity.Warning);
                }
                else if (exitCode != 0)
                {
                    _log.WriteLog($"pnputil output: {output}", "DriverExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException($"pnputil failed to install driver: {InfPath}, exit code: {exitCode}");
                }
                else
                {
                    _log.WriteLog("Driver installed successfully.");
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to install driver: {ex.Message}", "DriverExecEvent", Log.Severity.Error);
                throw;
            }
        }

        public void RemoveDriver()
        {
            _log.WriteLog($"Uninstalling driver with original .inf name: {InfName}");
            try
            {
                if (string.IsNullOrWhiteSpace(InfName))
                {
                    _log.WriteLog("Driver original .inf file name is not specified.", "DriverExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException("Driver original .inf file name is not specified.");
                }
                List<string> publishedNames = FindPublishedNames(InfName);
                if (publishedNames.Count == 0)
                {
                    _log.WriteLog($"No driver with original .inf name: {InfName} found in the driver store, nothing to remove.", "DriverExecEvent", Log.Severity.Warning);
                    return;
                }
                foreach (string publishedName in publishedNames)
                {
                    _log.WriteLog($"Removing driver package: {publishedName} (original name: {InfName})");
                    (int exitCode, string output) = RunPnpUtil($"/delete-driver \"{publishedName}\" /uninstall /force");
                    if (exitCode == RebootRequiredExitCode)
                    {
                        _log.WriteLog($"Driver package {publishedName} removed, but a reboot is required to complete the removal.", "DriverExecEvent", Log.Severity.Warning);
                    }
                    else if (exitCode != 0)
                    {
                        _log.WriteLog($"pnputil output: {output}", "DriverExecEvent", Log.Severity.Error);
                        throw new InvalidOperationException($"pnputil failed to remove driver package: {publishedName}, exit code: {exitCode}");
                    }
                }
                _log.WriteLog("Driver uninstalled.");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to uninstall driver: {ex.Message}", "DriverExecEvent", Log.Severity.Error);
                throw;
            }
        }

        // The driver store renames INFs to oemNN.inf, so match store entries back to the
        // driver by the Original Name reported by pnputil /enum-drivers.
        private List<string> FindPublishedNames(string originalInfName)
        {
            (int exitCode, string output) = RunPnpUtil("/enum-drivers");
            if (exitCode != 0)
            {
                _log.WriteLog($"pnputil output: {output}", "DriverExecEvent", Log.Severity.Error);
                throw new InvalidOperationException($"pnputil failed to enumerate the driver store, exit code: {exitCode}");
            }

            List<string> publishedNames = new();
            string? currentPublishedName = null;
            foreach (string rawLine in output.Split('\n'))
            {
                string line = rawLine.Trim();
                int separatorIndex = line.IndexOf(':');
                if (separatorIndex < 0) continue;
                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1).Trim();
                if (key.Equals("Published Name", StringComparison.OrdinalIgnoreCase))
                {
                    currentPublishedName = value;
                }
                else if (key.Equals("Original Name", StringComparison.OrdinalIgnoreCase) &&
                    value.Equals(originalInfName, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(currentPublishedName))
                {
                    publishedNames.Add(currentPublishedName);
                }
            }
            return publishedNames;
        }

        private static (int ExitCode, string Output) RunPnpUtil(string arguments)
        {
            string pnpUtilPath = Path.Combine(System.Environment.SystemDirectory, "pnputil.exe");
            using Process process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pnpUtilPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
    }
}
