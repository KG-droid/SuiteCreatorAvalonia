using System;
using System.Diagnostics;
using System.IO;

namespace SuiteProgressPopup.Services
{
    /// <summary>
    /// Kills and restores explorer.exe for the fullscreen lockdown mode. This popup already runs in the
    /// interactive user's own session (SuiteExecutor launches it via StartProcessAsCurrentUser), so no
    /// elevation/impersonation is needed to reach explorer.exe the way SuiteExecutor's own RefreshEnvironment
    /// (Suite.RefreshEnv.cs) does from a SYSTEM context.
    /// </summary>
    internal static class LockdownService
    {
        private static readonly string ExplorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        public static void KillExplorer()
        {
            foreach (Process explorer in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    explorer.Kill();
                    explorer.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    AppLogService.Warning($"Lockdown: could not kill explorer PID {explorer.Id}: {ex.Message}", "Lockdown");
                }
                finally
                {
                    explorer.Dispose();
                }
            }
            AppLogService.Info("Lockdown: Explorer closed for the duration of the install.", "Lockdown");
        }

        public static void RestoreExplorer()
        {
            try
            {
                if (Process.GetProcessesByName("explorer").Length > 0)
                    return;

                string explorerWorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                Process.Start(new ProcessStartInfo
                {
                    FileName = ExplorerPath,
                    WorkingDirectory = explorerWorkingDir,
                    UseShellExecute = true
                });
                AppLogService.Info("Lockdown: Explorer restored.", "Lockdown");
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Lockdown: failed to restore Explorer: {ex.Message}", "Lockdown");
            }
        }
    }
}
