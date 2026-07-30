using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Logger;
using SuiteTools;
using WinRegistry = Microsoft.Win32.Registry;

namespace SuiteOperations.Events
{
    // Shared per-suite-run failsafe used by both ProcClosureExecEvent (StopAndBlock) and ServiceClosureExecEvent
    // (Stop/Disable): one ONSTART scheduled task plus one background --watch-pid watcher process cover every
    // process blocked and every service stopped/disabled during this suite run, so a suite killed mid-run
    // (crash, force-kill, power loss) doesn't leave a process permanently blocked or a service permanently down.
    //
    // The task/watcher are torn down again once the suite run ends on its own (see Suite.cs's Unblocks()) —
    // they only guard the window while the suite is actually alive, not indefinitely.
    public static class ClosureFailsafe
    {
        private const string ProcessEntryPrefix = "PROC|";
        private const string ServiceEntryPrefix = "SVC|";

        // One shared task/list per suite process — every process and service touched during this run adds
        // itself to the same list rather than each getting its own task and watcher.
        public static string SharedFailsafeTaskName() => $"UnblockSuite_{Environment.ProcessId}";

        public static string SharedBlockListPath(string taskName, string? suiteId)
        {
            // Runs as SYSTEM in the real world, so %localappdata% resolves to the SYSTEM profile —
            // not writable by a standard user — rather than a world-writable location.
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuiteExec", suiteId ?? "Unknown");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{taskName}.list");
        }

        public static bool FailsafeTaskExists(string taskName)
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{taskName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using Process? proc = Process.Start(psi);
                if (proc == null) return false;
                proc.StandardOutput.ReadToEnd();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit(5000);
                return proc.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        // Creates the shared ONSTART scheduled task (reboot failsafe) and the background PID-watcher for this
        // suite run, if not already created. Callers must ensure this succeeds BEFORE actually blocking a
        // process or stopping/disabling a service — if the failsafe can't be established, that action must not
        // proceed, or a killed suite could leave it permanently bricked with no way back.
        public static void EnsureFailsafeTaskAndWatcher(Log log, string taskName, string blockListPath)
        {
            if (FailsafeTaskExists(taskName))
                return;

            int suitePid = Environment.ProcessId;
            string suiteProcessName = Process.GetCurrentProcess().ProcessName;
            string? suiteExePath = Environment.ProcessPath;

            string installedExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SuiteExecutor", "SuiteExecutor.exe");
            string? failsafeExePath = File.Exists(installedExePath) ? installedExePath : suiteExePath;
            string schtasksArgs = $"/Create /TN \"{taskName}\" /TR \"\\\"{failsafeExePath}\\\" --failsafe-unblock-all \\\"{blockListPath}\\\" \\\"{taskName}\\\"\" /SC ONSTART /F /RL HIGHEST /RU SYSTEM";
            log.WriteLog($"Failsafe command: {schtasksArgs}");
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", schtasksArgs)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process? schtasksProc = Process.Start(psi))
            {
                if (schtasksProc == null)
                {
                    throw new Exception("Failed to start schtasks.exe for failsafe unblock task.");
                }
                string stdErr = schtasksProc.StandardError.ReadToEnd();
                schtasksProc.StandardOutput.ReadToEnd();
                schtasksProc.WaitForExit();
                if (schtasksProc.ExitCode != 0)
                {
                    throw new Exception($"schtasks failsafe creation failed (exit {schtasksProc.ExitCode}): {stdErr}");
                }
                log.WriteLog($"Created shared scheduled failsafe unblock task: {taskName}.");
            }

            // Start the single background watcher process for the whole suite run. It outlives the main suite
            // process by design (it polls until suitePid exits), so it must not inherit the main process's
            // current directory — that may well be the SFX cache dir, and an inherited CWD there would keep it
            // locked for as long as the watcher runs. None of its arguments are relative paths, so it doesn't
            // need to run from inside the cache at all.
            ProcessStartInfo waitPsi = new ProcessStartInfo(suiteExePath, $"--watch-pid {suitePid} {suiteProcessName} \"{blockListPath}\" \"{taskName}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(suiteExePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };
            Process? watcherProcess = Process.Start(waitPsi);
            if (watcherProcess == null || watcherProcess.HasExited)
            {
                throw new Exception("Failed to start background watcher process.");
            }
            log.WriteLog($"Started background unblock watcher (PID {watcherProcess.Id}) for suite PID {suitePid}");
        }

        // Call once per unblock/clear batch (after every process/service entry that batch is touching has
        // already been removed) rather than after each individual entry — tearing the task down mid-batch,
        // just because the list is transiently empty, would only force it straight back up again for the
        // next entry in the same batch (new task, new orphaned --watch-pid watcher) instead of leaving it be.
        public static void TeardownIfEmpty(Log log, string? suiteId)
        {
            string taskName = SharedFailsafeTaskName();
            string blockListPath = SharedBlockListPath(taskName, suiteId);
            if (ListEmpty(blockListPath))
            {
                DeleteFailsafeTask(taskName, blockListPath);
                log.WriteLog($"Cleaned up shared failsafe task: {taskName}");
            }
        }

        public static void DeleteFailsafeTask(string taskName, string blockListPath)
        {
            ProcessStartInfo deleteTaskPsi = new ProcessStartInfo("schtasks", $"/Delete /TN \"{taskName}\" /F")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            try { Process.Start(deleteTaskPsi)?.WaitForExit(5000); } catch { /* Best-effort cleanup */ }
            try { File.Delete(blockListPath); } catch { /* Best-effort cleanup */ }
        }

        public static bool ListEmpty(string blockListPath)
            => !File.Exists(blockListPath) || File.ReadAllLines(blockListPath).All(string.IsNullOrWhiteSpace);

        public static void AddProcessEntry(string blockListPath, string processExe)
        {
            HashSet<string> existing = ReadEntries(blockListPath);
            string entry = ProcessEntryPrefix + processExe;
            if (existing.Add(entry))
            {
                File.AppendAllLines(blockListPath, new[] { entry });
            }
        }

        public static void RemoveProcessEntry(string blockListPath, string processExe)
        {
            string entry = ProcessEntryPrefix + processExe;
            RemoveLines(blockListPath, line => string.Equals(line, entry, StringComparison.OrdinalIgnoreCase));
        }

        public static void AddServiceEntry(string blockListPath, string serviceName, uint priorStartType, bool wasRunning)
        {
            // Service entries carry captured state that could differ between calls, so always replace any
            // existing entry for the same service rather than de-duping on the literal line.
            RemoveServiceEntry(blockListPath, serviceName);
            string entry = $"{ServiceEntryPrefix}{serviceName}|{priorStartType}|{(wasRunning ? 1 : 0)}";
            File.AppendAllLines(blockListPath, new[] { entry });
        }

        public static void RemoveServiceEntry(string blockListPath, string serviceName)
        {
            RemoveLines(blockListPath, line =>
                TryParseServiceEntry(line, out string name, out _, out _) &&
                string.Equals(name, serviceName, StringComparison.OrdinalIgnoreCase));
        }

        // Invoked by the ONSTART task / --watch-pid watcher once the suite is confirmed gone. Restores every
        // entry still in the list — unblocking processes and restoring services to their captured prior state.
        public static void RestoreAllEntries(string blockListPath)
        {
            if (!File.Exists(blockListPath))
                return;

            // SharedBlockListPath writes this file to ".../SuiteExec/<suiteId>/<taskName>.list" — recover
            // the suiteId from that layout so a failsafe-triggered restore (boot/logon recovery, no
            // ProcClosureExecEvent instance around to ask) can still find and close blocked-process notices.
            string? suiteId = Path.GetFileName(Path.GetDirectoryName(blockListPath));

            foreach (string rawLine in File.ReadAllLines(blockListPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    if (line.StartsWith(ProcessEntryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        UnblockProcess(line.Substring(ProcessEntryPrefix.Length), suiteId);
                    }
                    else if (TryParseServiceEntry(line, out string serviceName, out uint priorStartType, out bool wasRunning))
                    {
                        RestoreService(serviceName, priorStartType, wasRunning);
                    }
                }
                catch { /* Best-effort — keep restoring the remaining entries in the list */ }
            }
        }

        public static void UnblockProcess(string processExe, string? suiteId = null)
        {
            string regPath = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{processExe}";
            using (Microsoft.Win32.RegistryKey? key = WinRegistry.LocalMachine.OpenSubKey(regPath, true))
            {
                if (key != null)
                {
                    key.DeleteValue("Debugger", false);
                    if (key.ValueCount == 0 && key.SubKeyCount == 0)
                    {
                        key.Close();
                        using (Microsoft.Win32.RegistryKey? parent = WinRegistry.LocalMachine.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options", true))
                        {
                            parent?.DeleteSubKey(processExe, false);
                        }
                    }
                }
            }

            KillLingeringBlockedNotices(suiteId);
        }

        // If a user tried to launch a blocked exe from this suite, SuiteUserPopup.exe --Blocked is still
        // showing a notice for it, tagged with this suite's ID in its (invisible — borderless, no
        // taskbar) window title (see App.BlockedNoticeTitlePrefix in SuiteUserPopup). Now that the suite
        // is unblocking, any such notice is stale, so find and close it. All of a suite's blocked
        // processes are unblocked together, so this isn't scoped to the specific processExe just
        // unblocked — closing every notice for this suite at once is correct either way.
        private static void KillLingeringBlockedNotices(string? suiteId)
        {
            if (string.IsNullOrWhiteSpace(suiteId))
                return;

            string expectedTitle = "SuiteExecBlockedNotice|" + suiteId;
            List<uint> matchingPids = new List<uint>();

            WindowNativeMethods.EnumWindows((hWnd, _) =>
            {
                int length = WindowNativeMethods.GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    StringBuilder title = new StringBuilder(length + 1);
                    WindowNativeMethods.GetWindowText(hWnd, title, title.Capacity);
                    if (title.ToString() == expectedTitle)
                    {
                        WindowNativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                        matchingPids.Add(pid);
                    }
                }
                return true;
            }, IntPtr.Zero);

            foreach (uint pid in matchingPids)
            {
                try
                {
                    using Process proc = Process.GetProcessById((int)pid);
                    if (!proc.HasExited && string.Equals(proc.ProcessName, "SuiteUserPopup", StringComparison.OrdinalIgnoreCase))
                        proc.Kill();
                }
                catch
                {
                    // Already gone, or no longer accessible — nothing to do.
                }
            }
        }

        private static class WindowNativeMethods
        {
            public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern int GetWindowTextLength(IntPtr hWnd);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        }

        public static void RestoreService(string serviceName, uint priorStartType, bool wasRunning)
        {
            SetServiceStartType(serviceName, priorStartType);

            if (wasRunning)
            {
                using ServiceController sc = new ServiceController(serviceName);
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            }
        }

        // Reads the service's current start type (Boot/System/Automatic/Manual/Disabled) via QueryServiceConfig
        // so callers can capture it before changing it, and restore the exact prior value later rather than
        // assuming Automatic.
        public static uint GetServiceStartType(string serviceName)
        {
            IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero, buffer = IntPtr.Zero;
            try
            {
                scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS);
                if (scm == IntPtr.Zero)
                    throw new InvalidOperationException($"OpenSCManager failed: {Marshal.GetLastWin32Error()}");

                svc = NativeMethods.OpenService(scm, serviceName, NativeMethods.SERVICE_QUERY_CONFIG);
                if (svc == IntPtr.Zero)
                    throw new InvalidOperationException($"OpenService failed for '{serviceName}': {Marshal.GetLastWin32Error()}");

                NativeMethods.QueryServiceConfig(svc, IntPtr.Zero, 0, out uint bytesNeeded);
                buffer = Marshal.AllocHGlobal((int)bytesNeeded);
                if (!NativeMethods.QueryServiceConfig(svc, buffer, bytesNeeded, out _))
                    throw new InvalidOperationException($"QueryServiceConfig failed for '{serviceName}': {Marshal.GetLastWin32Error()}");

                NativeMethods.QUERY_SERVICE_CONFIG config = Marshal.PtrToStructure<NativeMethods.QUERY_SERVICE_CONFIG>(buffer);
                return config.dwStartType;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (svc != IntPtr.Zero) NativeMethods.CloseServiceHandle(svc);
                if (scm != IntPtr.Zero) NativeMethods.CloseServiceHandle(scm);
            }
        }

        public static void SetServiceStartType(string serviceName, uint startType)
        {
            IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
            try
            {
                scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS);
                if (scm == IntPtr.Zero)
                    throw new InvalidOperationException($"OpenSCManager failed: {Marshal.GetLastWin32Error()}");

                svc = NativeMethods.OpenService(scm, serviceName, NativeMethods.SERVICE_CHANGE_CONFIG | NativeMethods.SERVICE_QUERY_CONFIG);
                if (svc == IntPtr.Zero)
                    throw new InvalidOperationException($"OpenService failed for '{serviceName}': {Marshal.GetLastWin32Error()}");

                bool result = NativeMethods.ChangeServiceConfig(
                    svc, NativeMethods.SERVICE_NO_CHANGE, startType, NativeMethods.SERVICE_NO_CHANGE,
                    null, null, IntPtr.Zero, null, null, null, null);
                if (!result)
                    throw new InvalidOperationException($"ChangeServiceConfig failed for '{serviceName}': {Marshal.GetLastWin32Error()}");
            }
            finally
            {
                if (svc != IntPtr.Zero) NativeMethods.CloseServiceHandle(svc);
                if (scm != IntPtr.Zero) NativeMethods.CloseServiceHandle(scm);
            }
        }

        private static HashSet<string> ReadEntries(string blockListPath) =>
            File.Exists(blockListPath)
                ? new HashSet<string>(File.ReadAllLines(blockListPath), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static void RemoveLines(string blockListPath, Func<string, bool> shouldRemove)
        {
            if (!File.Exists(blockListPath)) return;

            string[] remaining = File.ReadAllLines(blockListPath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !shouldRemove(line.Trim()))
                .ToArray();

            if (remaining.Length == 0)
                File.Delete(blockListPath);
            else
                File.WriteAllLines(blockListPath, remaining);
        }

        private static bool TryParseServiceEntry(string line, out string serviceName, out uint priorStartType, out bool wasRunning)
        {
            serviceName = string.Empty;
            priorStartType = 0;
            wasRunning = false;

            if (!line.StartsWith(ServiceEntryPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            string[] parts = line.Substring(ServiceEntryPrefix.Length).Split('|');
            if (parts.Length != 3)
                return false;

            serviceName = parts[0];
            if (!uint.TryParse(parts[1], out priorStartType))
                return false;
            wasRunning = parts[2] == "1";
            return true;
        }
    }
}
