using System.Diagnostics;
using System.Runtime.InteropServices;
using static SuiteTools.UserTools.ProcessExtensions;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        private void RefreshEnvironment()
        {
            if (!_suiteConfig.BuildSettings.RefreshEnv)
                return;

            _log.WriteLog("RefreshEnv: Broadcasting WM_SETTINGCHANGE to refresh environment variables.", "RefreshEnv", Log.Severity.Info);
            try
            {
                SendMessageTimeout(
                    HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    "Environment",
                    SMTO_ABORTIFHUNG,
                    5000,
                    out UIntPtr _);

                _log.WriteLog("RefreshEnv: Environment variable refresh broadcast sent.", "RefreshEnv", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"RefreshEnv: Failed to broadcast environment change: {ex.Message}", "RefreshEnv", Log.Severity.Warning);
            }

            _log.WriteLog("RefreshEnv: Restarting Windows Explorer to refresh desktop icons.", "RefreshEnv", Log.Severity.Info);
            try
            {
                // Kill all Explorer instances
                foreach (Process explorer in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        explorer.Kill();
                        explorer.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        _log.WriteLog($"RefreshEnv: Could not kill explorer PID {explorer.Id}: {ex.Message}", "RefreshEnv", Log.Severity.Warning);
                    }
                    finally
                    {
                        explorer.Dispose();
                    }
                }

                string explorerWorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                StartProcessAsCurrentUser(SystemPaths.Explorer, null, explorerWorkingDir, isWindowVisibleToUser: true, wait: false);

                _log.WriteLog("RefreshEnv: Windows Explorer restarted in the user session.", "RefreshEnv", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"RefreshEnv: Failed to restart Windows Explorer: {ex.Message}", "RefreshEnv", Log.Severity.Warning);
            }
        }
    }
}
