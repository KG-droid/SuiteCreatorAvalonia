using Newtonsoft.Json;
using SuiteCreatorAvalonia.Converters;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteOperations;
using SuiteOperations.Events;
using SuiteOperations.Package;
using Log = Logger.Log;

namespace SuiteExecutor
{
    public enum SuiteAction
    {
        Deployment,
        Removal,
        Rollback
    }

    public enum SuiteRunMode
    {
        Normal,     // Standard run — honor an active deferral and show the popup.
        Reminder,   // Scheduled deferral reminder firing — skip the deferral check, but still show the popup.
        FailSafe    // Boot/logon recovery of an interrupted run — skip both the deferral check and the popup; just run the suite.
    }

    internal partial class Suite
    {
        private Log _log;
        private const string _uninstallKeyBase32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string _uninstallKeyBase64 = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private string _uninstallRegistryRoot = string.Empty;
        private string _suiteRegistryPath = string.Empty;
        private readonly string _suiteConfigPath;
        private readonly string _suiteRootDir;
        private SuiteExecConfig _suiteConfig;
        private SuiteAction _action = SuiteAction.Deployment;
        private Mutex? _suiteMutex;
        private bool _ownsMutex;
        private string _familyMutexName;
        private string _logPath;
        private PackageBase? _lastFailedPackage;

        // Shared by CopyPopupFiles (Suite.UninstallMedia.cs) and SetPopupConfigPermissionsIfNeeded
        // (Suite.Execution.UserPackagePermissions.cs) — both need to know whether this suite has a
        // blocked-process notice to support, regardless of whether the full warning popup is enabled.
        private bool HasBlockingProcClosures => _suiteConfig.ProcClosureEvents?.Any(p => p.Action == ProcAction.StopAndBlock) ?? false;

        public Suite(string configFilePath)
        {
            _suiteConfigPath = Path.GetFullPath(configFilePath);
            _suiteRootDir = Path.GetDirectoryName(_suiteConfigPath) ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_suiteRootDir) && Directory.Exists(_suiteRootDir))
            {
                // Make relative file access behave as if invoked from the extracted suite directory.
                Directory.SetCurrentDirectory(_suiteRootDir);
            }

            LoadConfig(_suiteConfigPath);
        }

        public void Execute(SuiteAction action, SuiteRunMode runMode = SuiteRunMode.Normal)
        {
            try
            {
                // Startup
                _action = action;
                if (IsNewerSuiteInFamilyAlreadyRunning())
                {
                    _log.WriteLog("A newer version or revision of this suite family is already running, exiting this instance.", "Startup", Log.Severity.Info);
                    return;
                }
                LogStartupInfo();
                LogTimeZoneWarning();

                // Only a Normal run honors a pending deferral. A Reminder run is the deferral firing (skipping this
                // avoids it deferring on its own pending task), and a FailSafe run is a boot/logon recovery of an
                // interrupted run that must just complete the suite.
                if (runMode != SuiteRunMode.Normal)
                {
                    _log.WriteLog($"Run mode is {runMode}; skipping the active-deferral check", "Startup", Log.Severity.Info);
                }
                else if (IsDeferralActive())
                {
                    // Exit 1602 (user deferred), not 0, so the SFX preserves the cached installer the pending reminder needs.
                    _log.WriteLog("An active deferral exists for this suite, exiting so the scheduled reminder can handle it", "Startup", Log.Severity.Info);
                    Environment.Exit(1602);
                }

                // Resolve file paths from bootstrapper extraction before detection or execution
                ResolveExtractedPaths();

                _log.WriteLog($"Running Suite detection", "Startup", Log.Severity.Info);
                bool selfDetected = IsSelfDetected();
                if (action == SuiteAction.Deployment && selfDetected)
                {
                    if (IsAllPackagesDetected())
                    {
                        _log.WriteLog($"Suite and packages detected, and action is {action}, nothing to do", "Startup", Log.Severity.Info);
                        return;
                    }
                }
                if ((action == SuiteAction.Rollback || action == SuiteAction.Removal) && !selfDetected)
                {
                    if (!IsAnyPackagesDetected())
                    {
                        _log.WriteLog($"No Suite or Packages detected, and action is {action}, nothing to do", "Startup", Log.Severity.Info);
                        return;
                    }
                }
                CreateSuiteRunningRegistry();

                // Run popup (If configured). A FailSafe recovery run fires unattended at boot/logon, so it must
                // not prompt or allow deferral — it just runs the suite to finish the interrupted run.
                if (_suiteConfig.PopupSettings.ShowPopupWarning && runMode != SuiteRunMode.FailSafe)
                {
                    ExecutePopup();
                }
                else if (runMode == SuiteRunMode.FailSafe)
                {
                    _log.WriteLog("FailSafe recovery run; skipping the popup and running the suite directly", "Startup", Log.Severity.Info);
                }

                // FailSafe: register a recovery task now that the popup (if any) has been handled, so the
                // suite restarts at logon/startup if the device is interrupted partway through execution
                CreateFailSafeTask();

                // Show install progress popup (If configured)
                StartProgressPopup();

                // Set read/execute permissions for the user on any user context packages (If configured)
                SetUserPackagePermissions();

                // A blocked exe launch attempt shows a notice (see ProcClosureExecEvent.BuildDebuggerValue)
                // that reads the suite's name from this suite's own Popup cache folder. SuiteExecutor runs
                // elevated, but that notice runs in the interactive user's own session, so it needs its own
                // explicit read access to that folder.
                SetPopupConfigPermissionsIfNeeded();

                // Execute the suite, with optional retry on failure
                bool suiteSucceeded = false;
                int maxAttempts = _suiteConfig.BuildSettings.FailureRetry ? 2 : 1;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        if (attempt > 1)
                        {
                            _log.WriteLog($"Retrying suite execution (attempt {attempt}/{maxAttempts})", "Execution", Log.Severity.Info);

                            // If a package failed during deployment, try to uninstall it first so the retry
                            // isn't fighting a half-installed package. If the uninstall itself fails, carry on
                            // and retry anyway — it might still succeed.
                            if (action == SuiteAction.Deployment && _lastFailedPackage != null)
                            {
                                TryUninstallFailedPackage(_lastFailedPackage);
                            }

                            if (_suiteConfig.BuildSettings.RestartOnFailure)
                            {
                                _log.WriteLog("RestartOnFailure is enabled — restarting the device before retrying.", "Execution", Log.Severity.Info);
                                CreateRestartRetryTask();
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                                    SystemPaths.Shutdown,
                                    "/r /t 60 /c \"This computer will restart in 60 seconds to complete a software installation.\""
                                ) { UseShellExecute = false, CreateNoWindow = true });
                                // The recovery task will re-run the suite after restart; exit with the standard
                                // reboot-initiated code (this process triggered the restart itself, unlike 3010
                                // which signals the caller is expected to reboot) so the deployment tool doesn't
                                // read this as a plain, no-further-action success.
                                Environment.Exit(1641);
                            }
                        }

                        _log.WriteLog($"Beginning suite execution for action: {action} (attempt {attempt}/{maxAttempts})", "Execution", Log.Severity.Info);
                        _lastFailedPackage = null;
                        RunSuite();
                        suiteSucceeded = true;
                        break;
                    }
                    catch (Exception retryEx) when (attempt < maxAttempts)
                    {
                        _log.WriteLog($"Suite execution attempt {attempt} failed: {retryEx.Message}", "Execution", Log.Severity.Warning);
                    }
                }

                if (!suiteSucceeded)
                {
                    throw new Exception("Suite execution failed after all retry attempts.");
                }

                CompleteProgressPopup(isError: false);
                // The progress popup is now given a bounded waitTimeout (see StartProgressPopup) so it is
                // force-terminated if it doesn't close on its own; wait a bit longer than that here so this
                // normally observes the real exit (forced or not) before CleanupDeferral runs.
                WaitForProgressPopupExit(TimeSpan.FromSeconds(40));

                // Post-execution: update suite detection registry and clean up any stale deferral
                if (action == SuiteAction.Deployment)
                {
                    CreateUninstallMedia();
                    WriteSuiteDetectionRegistry();
                }
                else if (action == SuiteAction.Removal)
                {
                    RemoveSuiteDetectionRegistry();
                    RemoveUninstallMedia();
                }
                CleanupDeferral();

                // FailSafe: suite completed successfully, remove the recovery task
                RemoveFailSafeTask();

                // RefreshEnv: refresh environment variables and restart Explorer if configured
                RefreshEnvironment();

                _log.WriteLog($"Suite execution completed successfully for action: {action}", "Execution", Log.Severity.Info);

                return;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Suite execution failed, error: {ex.Message} : {ex.InnerException?.Message}", "Execution", Log.Severity.Error);
                CompleteProgressPopup(isError: true);

                // FailSafe: the suite failed outright (not a deliberate restart-retry), so the recovery task
                // must not be left behind — it would otherwise keep re-running a suite that's given up.
                RemoveFailSafeTask();

                Environment.Exit(1603); // Return error code to the bootstrapper
            }
            finally
            {
                _log.WriteLog($"Cleaning up resources", "Execution", Log.Severity.Info);
                // Clear Mutex — only release if this instance actually owns the lock; releasing a mutex we
                // don't own (another family instance is running) throws.
                if (_suiteMutex != null)
                {
                    if (_ownsMutex)
                    {
                        try { _suiteMutex.ReleaseMutex(); }
                        catch (Exception mutexEx) { _log.WriteLog($"Failed to release suite mutex: {mutexEx.Message}", "Execution", Log.Severity.Warning); }
                    }
                    _suiteMutex.Dispose();
                }

                // Clear running registry
                RemoveSuiteRunningRegistry();

                // Reverse process and service blocks
                Unblocks();
                _log.WriteLog($"*****Suite End*****", "Execution", Log.Severity.Info);
            }
        }

        private void LoadConfig(string fileToLoad)
        {
            Console.WriteLine("Loading suite configuration");
            try
            {
                // Use SuiteExecConfig's own (de)serialization settings rather than a separate ad-hoc set here,
                // so the converters used to read the config always match the ones SuiteBuilder used to write it
                // (e.g. TextDocumentToJson for embedded PowerShell scripts, BitmapToJson for images).
                _suiteConfig = new SuiteExecConfig().FromJson(fileToLoad);
                PrepareLog();
                InjectRuntimeDependencies();
                _familyMutexName = $"SuiteExecutor_{_suiteConfig.BuildSettings.UpgradeCode}_{_action}";
                _uninstallRegistryRoot = _suiteConfig.BuildSettings.Architecture == Architecture.x64 ? _uninstallKeyBase64 : _uninstallKeyBase32;
                _suiteRegistryPath = Path.Combine(_uninstallRegistryRoot.Replace("HKEY_LOCAL_MACHINE\\", string.Empty), $"{{{_suiteConfig.BuildSettings.UpgradeCode.ToString()}}}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load suite project, error: {ex.Message}");
                Environment.Exit(1603);
            }
        }

        private void InjectRuntimeDependencies()
        {
            List<RuleSet> ruleSets = _suiteConfig.RuleSets ?? new List<RuleSet>();

            // Packages
            if (_suiteConfig.Packages != null)
            {
                foreach (PackageBase pkg in _suiteConfig.Packages)
                {
                    switch (pkg)
                    {
                        case OtherExec otherExec:
                            otherExec.SetLog(_log);
                            otherExec.SetRuleSets(ruleSets);
                            break;
                        case OtherRemovalExec otherRemExec:
                            otherRemExec.SetLog(_log);
                            otherRemExec.SetRuleSets(ruleSets);
                            break;
                        case MSIExec msiExec:
                            msiExec.SetLog(_log);
                            break;
                        case MSIxExec msixExec:
                            msixExec.SetLog(_log);
                            break;
                        case MSIRemovalExec msiRemExec:
                            msiRemExec.SetLog(_log);
                            break;
                        case MSIxRemovalExec msixRemExec:
                            msixRemExec.SetLog(_log);
                            break;
                    }
                }
            }

            // Events
            if (_suiteConfig.CertEvents != null)
                foreach (CertExecEvent cert in _suiteConfig.CertEvents)
                    cert.SetLog(_log);
            if (_suiteConfig.DriverEvents != null)
                foreach (DriverExecEvent driver in _suiteConfig.DriverEvents)
                    driver.SetLog(_log);
            if (_suiteConfig.ProcClosureEvents != null)
                foreach (ProcClosureExecEvent proc in _suiteConfig.ProcClosureEvents)
                {
                    proc.SetLog(_log);
                    proc.SetSuiteId(_suiteConfig.BuildSettings.UpgradeCode.ToString());
                    proc.SetPopupConfigDir(Path.Combine(_suiteRootDir, "Popup"));
                }
            if (_suiteConfig.EnvironmentEvents != null)
                foreach (EnvExecEvent env in _suiteConfig.EnvironmentEvents)
                    env.SetLog(_log);
            if (_suiteConfig.ExecutableEvents != null)
                foreach (ExecutableExecEvent exe in _suiteConfig.ExecutableEvents)
                    exe.SetLog(_log);
            if (_suiteConfig.ExtensionEvents != null)
                foreach (BrowserExExecEvent ext in _suiteConfig.ExtensionEvents)
                    ext.SetLog(_log);
            if (_suiteConfig.FileEvents != null)
                foreach (FileExecEvent file in _suiteConfig.FileEvents)
                    file.SetLog(_log);
            if (_suiteConfig.PowerShellEvents != null)
                foreach (PowerShellExecEvent ps in _suiteConfig.PowerShellEvents)
                    ps.SetLog(_log);
            if (_suiteConfig.RegistryEvents != null)
                foreach (RegExecEvent reg in _suiteConfig.RegistryEvents)
                    reg.SetLog(_log);
            if (_suiteConfig.ServiceClosureEvents != null)
                foreach (ServiceClosureExecEvent svc in _suiteConfig.ServiceClosureEvents)
                {
                    svc.SetLog(_log);
                    svc.SetSuiteId(_suiteConfig.BuildSettings.UpgradeCode.ToString());
                }
            if (_suiteConfig.ShortcutEvents != null)
                foreach (ShortcutExecEvent shortcut in _suiteConfig.ShortcutEvents)
                    shortcut.SetLog(_log);
        }

        // Reverses anything this run left temporarily blocked/stopped that must not outlive the suite process,
        // regardless of success or failure — StopAndBlock'd processes are always unblocked, and any pending
        // service failsafe (see ServiceClosureExecEvent.ClearFailsafe) is always torn down here too, so a suite
        // that finishes on its own (with or without an explicit Unblock/EnableAndStart step) never leaves a
        // background ONSTART task/watcher armed after this process exits.
        private void Unblocks()
        {
            foreach (ProcClosureExecEvent procClosure in _suiteConfig.ProcClosureEvents ?? new List<ProcClosureExecEvent>())
            {
                if (procClosure.Action == ProcAction.StopAndBlock)
                {
                    procClosure.Action = ProcAction.Unblock;
                    procClosure.ExecuteEvent();
                }
            }

            foreach (ServiceClosureExecEvent serviceClosure in _suiteConfig.ServiceClosureEvents ?? new List<ServiceClosureExecEvent>())
            {
                serviceClosure.ClearFailsafe();
            }
        }
    }
}
