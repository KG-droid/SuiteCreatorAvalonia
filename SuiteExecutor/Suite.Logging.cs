using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private string _defaultLogDir = "C:\\Modern-Workplace-Logs";

        private void LogStartupInfo()
        {
            _log.WriteLog("*****Starting Suite*****", "Startup", Log.Severity.Info);
            _log.WriteLog($"Computer Name: {Environment.MachineName}", "Startup", Log.Severity.Info);
            _log.WriteLog($"Suite Action: {_action}", "Startup", Log.Severity.Info);
            _log.WriteLog($"SuiteExecutor Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}", "Startup", Log.Severity.Info);
        }

        private void LogTimeZoneWarning()
        {
            _log.WriteLog($"Current UTC Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z", "Startup", Log.Severity.Info);
            var localZone = TimeZoneInfo.Local;
            var offset = localZone.GetUtcOffset(DateTime.Now);
            var offsetSign = offset.TotalHours >= 0 ? "+" : "-";
            var offsetString = $"{offsetSign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";
            _log.WriteLog(
                $"WARNING: All log times are in device local time zone: {localZone.DisplayName} (UTC{offsetString})",
                "Startup",
                Log.Severity.Warning);
        }

        private void PrepareLog()
        {
            string logFileName = $"{_suiteConfig.BuildSettings.Manufacturer}_{_suiteConfig.BuildSettings.Name}_{_suiteConfig.BuildSettings.SuiteVersion}_{_suiteConfig.BuildSettings.Revision}.log";

            // Establish a guaranteed fallback logger first so _log is never null even if the configured log
            // directory can't be created below. The Log constructor only stores the path (no I/O), so it
            // cannot throw here — actual writes happen later and the temp dir is writable for a SYSTEM run.
            _logPath = Path.Combine(Path.GetTempPath(), logFileName);
            _log = new Log(_logPath);

            try
            {
                string? logDir = _suiteConfig.BuildSettings.LogDir;
                string fullLogDir = !string.IsNullOrWhiteSpace(logDir)
                    ? Path.GetFullPath(logDir)
                    : Path.GetFullPath(_defaultLogDir);

                if (!Directory.Exists(fullLogDir))
                {
                    Directory.CreateDirectory(fullLogDir);
                }

                // Configured directory is usable — promote the logger to it.
                _logPath = Path.Combine(fullLogDir, logFileName);
                _log = new Log(_logPath);
            }
            catch (Exception ex)
            {
                // Keep the temp fallback logger established above and report why the configured dir failed.
                string errorMsg = $"Failed to prepare configured log directory, falling back to '{_logPath}': {ex.Message}";
                try { _log.WriteLog(errorMsg, "Logging", Log.Severity.Warning); } catch { }
                try { Console.Error.WriteLine(errorMsg); } catch { }
                try { System.Diagnostics.Debug.WriteLine(errorMsg); } catch { }
            }
        }
    }
}
