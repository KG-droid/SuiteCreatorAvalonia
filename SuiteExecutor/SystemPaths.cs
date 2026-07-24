using System;
using System.IO;

namespace SuiteExecutor
{
    /// <summary>
    /// Fully-qualified paths to the Windows system binaries this elevated process launches.
    /// Launching these by bare name would let the executable search path (which includes the current
    /// working directory) resolve a planted binary and run it as SYSTEM. Always launch by full path.
    /// </summary>
    internal static class SystemPaths
    {
        // Environment.SystemDirectory resolves to %windir%\System32 (or SysWOW64 for a 32-bit process),
        // keeping these consistent with the running process's bitness.
        internal static readonly string SchTasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        internal static readonly string Shutdown = Path.Combine(Environment.SystemDirectory, "shutdown.exe");

        // powershell.exe is not directly in System32 — it lives under WindowsPowerShell\v1.0.
        internal static readonly string PowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

        // explorer.exe lives in %windir% itself, not System32.
        internal static readonly string Explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
    }
}
