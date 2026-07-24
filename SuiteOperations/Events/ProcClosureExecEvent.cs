using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using System.Diagnostics;
using WinRegistry = Microsoft.Win32.Registry;

namespace SuiteOperations.Events
{
    public partial class ProcClosureExecEvent : ProcessClosure
    {
        private Log _log;
        private string? _suiteId;
        private string? _popupConfigDir;

        public ProcClosureExecEvent(Log log)
        {
            _log = log;
        }

        public ProcClosureExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void SetSuiteId(string suiteId)
        {
            _suiteId = suiteId;
        }

        // The suite's own Popup cache folder (_suiteRootDir/Popup) — the same folder the normal
        // warning popup already reads from. Set by Suite.InjectRuntimeDependencies.
        public void SetPopupConfigDir(string popupConfigDir)
        {
            _popupConfigDir = popupConfigDir;
        }

        public void ExecuteEvent()
        {
            if (Action == null)
            {
                _log.WriteLog("No process closure action specified.");
                throw new InvalidOperationException("No process closure action specified.");
            }
            List<string> processNames = GetNames().ToList();
            if (processNames.Count == 0)
            {
                _log.WriteLog("Process name is empty.");
                throw new InvalidOperationException("Process name is empty.");
            }
            foreach (string rawName in processNames)
            {
                ExecuteForProcess(rawName);
            }
        }

        private void ExecuteForProcess(string rawName)
        {
            // Ensure .exe extension
            string processExe = rawName;
            if (!processExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processExe += ".exe";
            string regPath = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{processExe}";
            try
            {
                switch (Action)
                {
                    case ProcAction.Unblock:
                    {
                        // The failsafe task/list are shared by every process (and service — see
                        // ServiceClosureExecEvent) touched during this suite run. Only tear the task down once
                        // the list is fully empty.
                        string taskName = ClosureFailsafe.SharedFailsafeTaskName();
                        string blockListPath = ClosureFailsafe.SharedBlockListPath(taskName, _suiteId);

                        ClosureFailsafe.RemoveProcessEntry(blockListPath, processExe);

                        if (ClosureFailsafe.ListEmpty(blockListPath))
                        {
                            ClosureFailsafe.DeleteFailsafeTask(taskName, blockListPath);
                            _log.WriteLog($"Cleaned up shared failsafe task: {taskName}");
                        }

                        ClosureFailsafe.UnblockProcess(processExe, _suiteId);
                        _log.WriteLog($"Unblocked process: {processExe}");
                        break;
                    }
                    case ProcAction.StopAndBlock:
                    {
                        // Failsafe setup MUST succeed before blocking. If the failsafe can't be established,
                        // we must not block the process as it could be left permanently bricked.
                        // A single shared task + watcher covers every process (and service) touched during this
                        // suite run — each StopAndBlock just adds its exe to the shared unblock list.
                        string taskName = ClosureFailsafe.SharedFailsafeTaskName();
                        string blockListPath = ClosureFailsafe.SharedBlockListPath(taskName, _suiteId);

                        ClosureFailsafe.EnsureFailsafeTaskAndWatcher(_log, taskName, blockListPath);
                        ClosureFailsafe.AddProcessEntry(blockListPath, processExe);

                        // Both failsafes are confirmed — now safe to block and kill
                        using (Microsoft.Win32.RegistryKey blockKey = WinRegistry.LocalMachine.CreateSubKey(regPath))
                        {
                            blockKey.SetValue("Debugger", BuildDebuggerValue(processExe), Microsoft.Win32.RegistryValueKind.String);
                        }
                        _log.WriteLog($"Blocked process: {processExe}");

                        foreach (Process runningProc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processExe)))
                        {
                            try { runningProc.Kill(); runningProc.WaitForExit(5000); }
                            catch (Exception ex)
                            {
                                _log.WriteLog($"Failed to kill process {runningProc.Id}: {ex.Message}", "Application", Log.Severity.Error);
                                throw;
                            }
                        }
                        _log.WriteLog($"Killed all processes named: {processExe}");
                        break;
                    }
                    case ProcAction.Stop:
                        // Kill all processes
                        foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processExe)))
                        {
                            try { proc.Kill(); proc.WaitForExit(5000); }
                            catch (Exception ex)
                            {
                                _log.WriteLog($"Failed to kill process {proc.Id}: {ex.Message}", "Application", Log.Severity.Error);
                                throw;
                            }
                        }
                        _log.WriteLog($"Killed all processes named: {processExe}");
                        break;
                    default:
                        _log.WriteLog($"Unknown process closure action: {Action}");
                        throw new InvalidOperationException($"Unknown process closure action: {Action}");
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Exception in ExecuteEvent for process {processExe}: {ex.Message}", "Application", Log.Severity.Error);
                throw;
            }
        }

        // Windows appends the blocked exe's own path and args after whatever we put here, so this
        // must tolerate trailing garbage. SuiteUserPopup.exe only reads its own recognised switches
        // and ignores anything appended after them, same as "cmd.exe /c exit" did before it.
        private string BuildDebuggerValue(string processExe)
        {
            string installedExecutorDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "SuiteExecutor");
            string popupExePath = Path.Combine(installedExecutorDir, "SuiteUserPopup.exe");

            if (!File.Exists(popupExePath))
            {
                _log.WriteLog($"SuiteUserPopup.exe not found at {popupExePath}; blocking {processExe} without a user notice.", "Application", Log.Severity.Warning);
                return "cmd.exe /c exit"; // Harmless block, no notice shown
            }

            string arguments = $"--Blocked --ProcessName \"{processExe}\"";

            // The notice shows the blocked app's own icon rather than any suite branding, but it still
            // reads the suite's name from popconfig.json — the same file, in the same suite cache
            // folder, that the normal warning popup already reads from (Suite.Popup.cs). That folder
            // exists for the life of the suite run (and any deferral), which covers the whole window a
            // block can actually be active, unlike the suite's uninstall media (only written once the
            // suite has already finished — by which point this notice wouldn't be needed anyway).
            if (!string.IsNullOrWhiteSpace(_popupConfigDir))
            {
                string configPath = Path.Combine(_popupConfigDir, "popconfig.json");
                arguments += $" --Config \"{configPath}\"";
            }

            // Tags the notice's window title with this suite's stable ID, so ClosureFailsafe can find and
            // close it later purely by enumerating window titles — no marker file needed (see
            // App.BlockedNoticeTitlePrefix / ClosureFailsafe.KillLingeringBlockedNotices).
            if (!string.IsNullOrWhiteSpace(_suiteId))
                arguments += $" --SuiteId \"{_suiteId}\"";

            // "--" marks the end of our own args — Windows appends the real blocked exe's path (and its
            // original args) after this whole string, and SuiteUserPopup.exe looks for this exact marker
            // to find where that appended data starts, regardless of how many switches we added above it.
            arguments += " --";

            return $"\"{popupExePath}\" {arguments}";
        }
    }
}
