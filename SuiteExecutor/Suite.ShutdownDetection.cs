using System.Runtime.InteropServices;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private const uint CTRL_LOGOFF_EVENT = 5;
        private const uint CTRL_SHUTDOWN_EVENT = 6;

        private delegate bool ConsoleCtrlHandlerDelegate(uint ctrlType);

        // Kept as a field so the delegate isn't garbage-collected while native code still holds a pointer to
        // it — SetConsoleCtrlHandler doesn't root the managed delegate on its own.
        private ConsoleCtrlHandlerDelegate? _ctrlHandler;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate handler, bool add);

        // Windows broadcasts this to every console process a few seconds before a shutdown/restart/logoff
        // actually happens, then force-kills anything still running once that grace period expires. That's
        // enough time for one synchronous log write, but not for anything slower — do the minimum here.
        private void RegisterShutdownDetection()
        {
            _ctrlHandler = OnConsoleCtrlEvent;
            if (!SetConsoleCtrlHandler(_ctrlHandler, true))
            {
                _log.WriteLog("Failed to register shutdown/restart detection handler.", "Shutdown", Log.Severity.Warning);
            }
        }

        private bool OnConsoleCtrlEvent(uint ctrlType)
        {
            string? reason = ctrlType switch
            {
                CTRL_SHUTDOWN_EVENT => "system shutdown or restart",
                CTRL_LOGOFF_EVENT => "user logoff",
                _ => null
            };

            if (reason != null)
            {
                try { _log.WriteLog($"Suite execution interrupted: {reason} detected while the suite was running.", "Shutdown", Log.Severity.Warning); }
                catch { }
            }

            return false; // Don't claim to have handled it — let Windows' default shutdown handling continue.
        }
    }
}
