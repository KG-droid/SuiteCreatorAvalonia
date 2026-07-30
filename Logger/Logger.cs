using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace Logger
{
    public class Log
    {
        // Instance fields, so that multiple Log instances (each with their own file) can coexist in one process
        private readonly string _logFilePath;
        private const long _maxLogLength = 1000000;
        private const int _maxLogFileCount = 10;
        public Log(string LogFilePath)
        {
            _logFilePath = LogFilePath;
        }
        public enum Severity
        {
            Info,
            Warning,
            Error
        }
        public void WriteLog(string Message, Exception Ex, string Component = "Application", Severity Sev = Severity.Error)
        {
            WriteLog($"{Message}{System.Environment.NewLine}{Ex}", Component, Sev);
        }
        public void WriteLog(string Message, string Component = "Application", Severity Sev = Severity.Info)
        {
            // CMTrace colors rows off a numeric type code (1=Info, 2=Warning/yellow, 3=Error/red), not the
            string logEntry = $"<![LOG[{Message}]LOG]!><time=\"{DateTime.Now:HH:mm:ss.fffff}\" " +
                $"date=\"{DateTime.Now:MM-dd-yyyy}\" component=\"{Component}\" context=\"{WindowsIdentity.GetCurrent().Name}\" type=\"{(int)Sev + 1}\" " +
                $"thread=\"{Process.GetCurrentProcess().Id}\" file=\"{Assembly.GetExecutingAssembly().Location}\">";
            RotateLogFiles();
            AppendLog(logEntry);
        }
        private void AppendLog(string Message)
        {
            int maxRetries = 3;
            int delay = 500;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (!Directory.Exists(Path.GetDirectoryName(_logFilePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath));
                    }
                    using (StreamWriter sw = new StreamWriter(_logFilePath, true))
                    {
                        sw.WriteLine(Message);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    if (ex is IOException)
                    {
                        if (attempt == maxRetries)
                        {
                            throw new Exception($"Failed to write to log file after {maxRetries} attempts, error: {ex.Message}", ex);
                        }
                        Thread.Sleep(delay);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
        private void RotateLogFiles()
        {
            if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length > _maxLogLength)
            {
                string logDir = Path.GetDirectoryName(_logFilePath) ?? string.Empty;
                string logName = Path.GetFileNameWithoutExtension(_logFilePath);
                string archiveFilePath = Path.Combine(logDir, $"{logName}_{DateTime.Now:ddMMyyyyHHmmss}.log");
                File.Move(_logFilePath, archiveFilePath);
                List<string> logFiles = Directory.GetFiles(Path.GetDirectoryName(_logFilePath), $"{logName}*.log").OrderByDescending(f => f).ToList();
                if (logFiles.Count > _maxLogFileCount)
                {
                    foreach (string filePath in logFiles.Where(f => logFiles.IndexOf(f) > 9).ToList())
                    {
                        File.Delete(filePath);
                    }
                }
            }
        }
    }
}
