using System;
using System.IO;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>
    /// App-wide logger for SuiteCreator. Static so it is usable from anywhere (including before
    /// DI is configured and in global crash handlers). All writes are best-effort: a failure to
    /// log must never take the app down, so logging errors are swallowed.
    /// </summary>
    public static class AppLog
    {
        internal static readonly string LogFilePath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "SuiteCreator", "SuiteCreator.Log");
        private static readonly Logger.Log _log = new Logger.Log(LogFilePath);

        /// <summary>
        /// Hooks the last-resort exception handlers so crashes and unobserved task exceptions
        /// are captured in the log. Call once at process start, before the app framework spins up.
        /// </summary>
        public static void RegisterGlobalExceptionHandlers()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Error("Unhandled exception, application will terminate", (e.ExceptionObject as Exception) ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown"));
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
        }

        public static void Info(string message, string component = "Application")
            => Write(() => _log.WriteLog(message, component, Logger.Log.Severity.Info));

        public static void Warning(string message, string component = "Application")
            => Write(() => _log.WriteLog(message, component, Logger.Log.Severity.Warning));

        public static void Error(string message, string component = "Application")
            => Write(() => _log.WriteLog(message, component, Logger.Log.Severity.Error));

        public static void Error(string message, Exception ex, string component = "Application")
            => Write(() => _log.WriteLog(message, ex, component, Logger.Log.Severity.Error));

        private static void Write(Action writeAction)
        {
            try
            {
                writeAction();
            }
            catch
            {
                // Logging is best-effort only
            }
        }
    }
}
