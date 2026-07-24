using Logger;
using System;
using System.IO;

namespace SuiteUserPopup.Services
{
    internal static class AppLogService
    {
        private static readonly object _syncLock = new object();
        private static Log? _logger;

        public static Log Logger
        {
            get
            {
                Initialize();
                return _logger!;
            }
        }

        public static void Initialize(string? logFilePath = null)
        {
            if (_logger is not null)
                return;

            lock (_syncLock)
            {
                if (_logger is not null)
                    return;

                string resolvedLogFilePath = string.IsNullOrWhiteSpace(logFilePath)
                    ? @"C:\Modern-Workplace-Logs\SuiteUserPopup.log"
                    : logFilePath;

                _logger = new Log(resolvedLogFilePath);
            }
        }

        public static void Info(string message, string component)
        {
            Write(message, component, Log.Severity.Info);
        }

        public static void Warning(string message, string component)
        {
            Write(message, component, Log.Severity.Warning);
        }

        public static void Error(string message, string component)
        {
            Write(message, component, Log.Severity.Error);
        }

        private static void Write(string message, string component, Log.Severity severity)
        {
            try
            {
                Logger.WriteLog(message, component, severity);
            }
            catch
            {
            }
        }
    }
}