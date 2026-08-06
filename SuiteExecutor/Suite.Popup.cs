using SuiteCreatorAvalonia.Models.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static SuiteTools.UserTools.ProcessExtensions;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private static readonly string _installedPopupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SuiteExecutor");
        private static readonly string _userPopExe = Path.Combine(_installedPopupDir, "SuiteUserPopup.exe");
        private static int _delayDaysRemaining = 0;
        private bool _popupWasShown = false;

        // These MUST match the exit codes emitted by SuiteUserPopup.exe:
        //   0 Continue, 1 DeviceLocked (session locked), 2 Error (missing config/logo args),
        //   3 Defer (user picked a reminder time, written to stdout), 4 TimerExpired, 5 LogoMissing.
        private enum PopupExitType
        {
            Continue = 0,
            DeviceLocked = 1,
            Error = 2,
            Defer = 3,
            TimerExpired = 4,
            LogoMissing = 5,
        }

        private void DisableSuiteSkip()
        {
            try
            {
                _suiteConfig.PopupSettings.LockedAction = PopupAction.Continue;
                _suiteConfig.PopupSettings.LoggedOffAction = PopupAction.Continue;
                _suiteConfig.PopupSettings.TimerExpireAction = PopupAction.Continue;
                _delayDaysRemaining = 0;
                _log.WriteLog($"Suite skip has been disabled by setting all popup actions to continue", "Popup", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to disable suite skip: {ex.Message}", "Popup", Log.Severity.Error);
            }
        }

        // A user skip/deferral leaves the suite via Environment.Exit, which bypasses the normal end-of-run
        // cleanup. Proactively remove the FailSafe recovery task (so its boot/logon trigger can't re-run the
        // suite and pre-empt a scheduled reminder) and the running-state registry, which are both created
        // before the popup runs. Does not touch any SuiteReminder_* deferral task.
        private void CleanupBeforeUserSkipExit()
        {
            try { RemoveFailSafeTask(); }
            catch (Exception ex) { _log.WriteLog($"Skip-exit cleanup: failed to remove FailSafe task: {ex.Message}", "Popup", Log.Severity.Warning); }
            try { RemoveSuiteRunningRegistry(); }
            catch (Exception ex) { _log.WriteLog($"Skip-exit cleanup: failed to remove running registry: {ex.Message}", "Popup", Log.Severity.Warning); }
        }

        // Returns true only when the script ran successfully and explicitly output $False - that's the sole
        // case where skipping the popup is intentional. An empty/whitespace script is treated as no condition
        // (show the popup). Anything that couldn't be evaluated cleanly (process failed to start, non-bool or
        // missing output, an exception) is treated as fail-safe and also shows the popup - a user might
        // otherwise lose data if apps get force-closed with no warning because a condition script broke.
        private bool IsPopupConditionExplicitlyNotMet(string? base64Condition, string logLabel)
        {
            if (string.IsNullOrWhiteSpace(base64Condition))
            {
                return false;
            }

            _log.WriteLog($"Checking {logLabel}", "Popup", Log.Severity.Info);
            string tempScriptPath = Path.Combine(Path.GetTempPath(), $"popup_condition_{Guid.NewGuid()}.ps1");
            try
            {
                byte[] psBytes = Convert.FromBase64String(base64Condition);
                string psScript = Encoding.UTF8.GetString(psBytes);
                if (string.IsNullOrWhiteSpace(psScript))
                {
                    _log.WriteLog($"{logLabel} is configured but the script is empty, skipping the check", "Popup", Log.Severity.Info);
                    return false;
                }

                File.WriteAllText(tempScriptPath, psScript);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = SystemPaths.PowerShell,
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null)
                {
                    _log.WriteLog($"{logLabel} failed with error: Failed to start PowerShell process.", "Popup", Log.Severity.Error);
                    _log.WriteLog($"The condition could not be evaluated, showing the popup so the user has a chance to save their work.", "Popup", Log.Severity.Warning);
                    return false;
                }

                string output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (string.IsNullOrWhiteSpace(output))
                {
                    _log.WriteLog($"{logLabel} failed with error: Script produced no output, expected $True or $False.", "Popup", Log.Severity.Error);
                    _log.WriteLog($"The condition could not be evaluated, showing the popup so the user has a chance to save their work.", "Popup", Log.Severity.Warning);
                    return false;
                }

                if (!bool.TryParse(output.Trim(), out bool conditionMet))
                {
                    _log.WriteLog($"{logLabel} failed with error: Script did not return a bool value, output was: {output.Trim()}", "Popup", Log.Severity.Error);
                    _log.WriteLog($"The condition could not be evaluated, showing the popup so the user has a chance to save their work.", "Popup", Log.Severity.Warning);
                    return false;
                }

                if (!conditionMet)
                {
                    _log.WriteLog($"Popup is configured but the {logLabel} was not met.", "Popup", Log.Severity.Info);
                    _log.WriteLog($"Continuing with the suite execution, as the {logLabel} wasn't met", "Popup", Log.Severity.Info);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"{logLabel} failed with error: Failed to evaluate PowerShell condition: {ex.Message}", "Popup", Log.Severity.Error);
                _log.WriteLog($"The condition could not be evaluated, showing the popup so the user has a chance to save their work.", "Popup", Log.Severity.Warning);
                return false;
            }
            finally
            {
                if (File.Exists(tempScriptPath))
                {
                    File.Delete(tempScriptPath);
                }
            }
        }

        private void ExecutePopup()
        {
            int delayDaysConfigured = (int)_suiteConfig.PopupSettings.DelayDays;
            _delayDaysRemaining = delayDaysConfigured;
            try
            {
                DateTime? initialPopupDate = GetSuitePopupInitialDateRegistry();
                if (initialPopupDate != null)
                {
                    _delayDaysRemaining = Math.Max(0, delayDaysConfigured - (int)(DateTime.Now - initialPopupDate.Value).TotalDays);
                    if (_delayDaysRemaining <= 0)
                    {
                        _log.WriteLog($"The maximum delay days of: {delayDaysConfigured}, for the popup has been reached. The skip option will be disabled", "Popup", Log.Severity.Info);
                        DisableSuiteSkip();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to check popup initial date in registry, error: {ex.Message}", "Popup", Log.Severity.Error);
                DisableSuiteSkip(); // Fail safe and disable skip if there is any issue with the registry, rather than risk the user being stuck in an infinite loop of being able to always skip the popup.
            }
            
            string popupDir = Path.Combine(_suiteRootDir, "Popup");
            string popConfigPath = Path.Combine(popupDir, "popconfig.json");
            string suiteLogoPath = Path.Combine(popupDir, "SuiteLogo.png");
            string companyLogoPath = Path.Combine(popupDir, "CompanyLogo.png");
            if (!File.Exists(companyLogoPath))
            {
                companyLogoPath = Path.Combine(popupDir, "CompanyLogo.gif");
            }
            if (!Directory.Exists(popupDir))
            {
                throw new FileNotFoundException($"Popup directory does not exist: {popupDir}. This should have been created by the SFX boostrapper executable, the suite was built into, or should be placed with the Uninstall Media", popupDir);
            }
            if (!File.Exists(popConfigPath))
            {
                throw new FileNotFoundException($"Popup config path does not exist: {popConfigPath}. This should have been created by the SFX boostrapper executable, the suite was built into.", popConfigPath);
            }
            if (!File.Exists(suiteLogoPath))
            {
                throw new FileNotFoundException($"Popup suite logo path does not exist: {suiteLogoPath}. This should have been created by the SFX boostrapper executable, the suite was built into.", suiteLogoPath);
            }
            if (!File.Exists(companyLogoPath))
            {
                throw new FileNotFoundException($"Popup company logo png or gif does not exist in DIR: {popupDir}. This should have been created by the SFX boostrapper executable, the suite was built into.", companyLogoPath);
            }

            if (!Directory.Exists(_installedPopupDir))
            {
                throw new FileNotFoundException($"Suite executor directory does not exist: {_installedPopupDir}. This should have been created by the SFX boostrapper executable, the suite was built into.", _installedPopupDir);
            }
            if (!File.Exists(_userPopExe))
            {
                throw new FileNotFoundException($"The SuiteUserPopup.exe does not exist: {_userPopExe}. This should have been created by the SFX boostrapper executable, the suite was built into.", _userPopExe);
            }

            // Check the popup condition if it exists
            if (_suiteConfig.PopupSettings.LinkToProcClosures)
            {
                IEnumerable<string?> procClosureExeNames = _suiteConfig.ProcClosureEvents.Select(pc => pc.Name);
                HashSet<string> procNamesToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string? exeName in procClosureExeNames)
                {
                    if (!string.IsNullOrWhiteSpace(exeName))
                    {
                        string name = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? exeName[..^4]
                            : exeName;
                        procNamesToCheck.Add(name);
                    }
                }
                bool anyProcClosureRunning = false;
                Process[] runningProcesses = Process.GetProcesses();
                try
                {
                    foreach (Process process in runningProcesses)
                    {
                        try
                        {
                            if (procNamesToCheck.Contains(process.ProcessName))
                            {
                                anyProcClosureRunning = true;
                                break;
                            }
                        }
                        catch
                        {
                            // Ignore processes that cannot be accessed
                        }
                    }
                }
                finally
                {
                    foreach (Process process in runningProcesses)
                        process.Dispose();
                }
                if (!anyProcClosureRunning)
                {
                    _log.WriteLog($"Popup is configured, but it is linked to Process Closures, and none of those processes are running", "Popup", Log.Severity.Info);
                    _log.WriteLog($"Continuing with the suite execution", "Popup", Log.Severity.Info);
                    return;
                }
            }
            if (IsDeviceInEsp())
            {
                _log.WriteLog($"Device is in ESP (Autopilot Enrollment Status Page / initial setup)", "Popup", Log.Severity.Info);
                if (_suiteConfig.PopupSettings.ESPAction == PopupAction.Skip)
                {
                    _log.WriteLog($"ESPAction is Skip, skipping this suite run", "Popup", Log.Severity.Info);
                    CleanupBeforeUserSkipExit();
                    Environment.Exit(1602);
                }
                _log.WriteLog($"ESPAction is Continue, continuing with the suite execution", "Popup", Log.Severity.Info);
            }
            if (_suiteConfig.PopupSettings.HasGlobalPSCondition && IsPopupConditionExplicitlyNotMet(_suiteConfig.PopupSettings.GlobalPSCondition, "global popup condition"))
            {
                return;
            }
            if (_suiteConfig.PopupSettings.HasPSCondition && IsPopupConditionExplicitlyNotMet(_suiteConfig.PopupSettings.PSCondition, "popup condition"))
            {
                return;
            }
            try
            {
                // Set PopConfig to suite exec action
                string popConfigJson = File.ReadAllText(popConfigPath);
                JsonNode? popConfigNode = JsonNode.Parse(popConfigJson);
                if (popConfigNode == null)
                {
                    _log.WriteLog($"Popup config at {popConfigPath} could not be parsed. For safety purposes in case this is an important package, proceeding with the suite.", "Popup", Log.Severity.Error);
                    return;
                }
                popConfigNode["Action"] = _action.ToString();
                _log.WriteLog($"Setting popup {_action} text", "Popup", Log.Severity.Info);
                if (_action == SuiteAction.Deployment)
                {

                    popConfigNode["MainText"] = _suiteConfig.PopupSettings.InstallTxt;
                }
                else
                {
                    popConfigNode["MainText"] = _suiteConfig.PopupSettings.UninstallTxt;
                }
                popConfigNode["MaxDelayDays"] = _delayDaysRemaining;
                popConfigNode["ActionIconKind"] = (int)_suiteConfig.PopupSettings.DeployIconKind;
                File.WriteAllText(popConfigPath, popConfigNode?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? popConfigJson);

                // Run the popup and wait for the response
                _log.WriteLog($"launching Popup", "Popup", Log.Severity.Info);
                _popupWasShown = true;
                CreateSuitePopupInitialDateRegistry(); // Run this first so that if the popup kept failing, it wont just give infinite skips.
                TimeSpan popTimeSpan = TimeSpan.FromMinutes(_suiteConfig.PopupSettings.Timer);
                string popArguments = $"--Config \"{popConfigPath}\" --SuiteLogo \"{suiteLogoPath}\" --CompanyLogo \"{companyLogoPath}\"";
                _log.WriteLog($"Popup command: \"{_userPopExe}\" {popArguments}", "Popup", Log.Severity.Info);
                ImpersonatedProcessResult? result = StartProcessAsCurrentUser(_userPopExe, popArguments, popupDir, true, true, popTimeSpan);
                switch ((PopupExitType?)result?.ExitCode)
                {
                    case PopupExitType.Continue:
                        _log.WriteLog($"Popup result was: {PopupExitType.Continue}", "Popup", Log.Severity.Info);
                        _log.WriteLog($"Continuing with the suite execution", "Popup", Log.Severity.Info);
                        return;
                    case PopupExitType.DeviceLocked:
                        _log.WriteLog($"Popup result was: {PopupExitType.DeviceLocked}", "Popup", Log.Severity.Info);
                        if (_suiteConfig.PopupSettings.LockedAction == PopupAction.Skip)
                        {
                            _log.WriteLog($"Device is locked, skipping suite execution", "Popup", Log.Severity.Info);
                            CleanupBeforeUserSkipExit();
                            Environment.Exit(1602);
                        }

                        _log.WriteLog($"Device is locked, continuing with the suite execution", "Popup", Log.Severity.Info);
                        return;
                    case PopupExitType.Defer:
                        _log.WriteLog($"Popup result was: {PopupExitType.Defer}", "Popup", Log.Severity.Info);
                        if (TimeOnly.TryParse(result?.StandardOutput, out TimeOnly reminderTime))
                        {

                            if (_delayDaysRemaining > 0)
                            {
                                _log.WriteLog($"User deferred suite execution to {reminderTime}", "Popup", Log.Severity.Info);
                                ScheduleReminder(reminderTime);
                                // Remove the FailSafe recovery task so its boot/logon trigger can't pre-empt the
                                // reminder we just scheduled. (Removes SuiteFailSafe_*, not the SuiteReminder_* task.)
                                CleanupBeforeUserSkipExit();
                                Environment.Exit(1602);
                            }

                            return;
                        }
                        _log.WriteLog($"Failed to parse reminder time from popup output: {result?.StandardOutput}", "Popup", Log.Severity.Error);
                        _log.WriteLog($"For safety purposes in case this is an important package, proceeding with the suite.", "Popup", Log.Severity.Warning);
                        return;
                    case PopupExitType.TimerExpired:
                        _log.WriteLog($"Popup result was: {PopupExitType.TimerExpired}", "Popup", Log.Severity.Info);
                        _log.WriteLog($"Popup timer expired, will {_suiteConfig.PopupSettings.TimerExpireAction} the suite execution", "Popup", Log.Severity.Info);
                        // When the delay window is exhausted, DisableSuiteSkip() has already forced this to
                        // Continue, so a remaining Skip here means we are still within the allowed deferral window.
                        if (_suiteConfig.PopupSettings.TimerExpireAction == PopupAction.Skip)
                        {
                            _log.WriteLog($"Timer expired and TimerExpireAction is Skip, skipping this suite run", "Popup", Log.Severity.Info);
                            CleanupBeforeUserSkipExit();
                            Environment.Exit(1602);
                        }

                        return;
                    default:
                        _log.WriteLog($"Popup result was an error/unexpected exit code: {result?.ExitCode}", "Popup", Log.Severity.Info);
                        _log.WriteLog($"Popup failed, Exit code was: {result?.ExitCode}, Error: {result?.ErrorMessage}, Ran as user: {result?.UserName}", "Popup", Log.Severity.Error);
                        _log.WriteLog($"For safety purposes in case this is an important package, proceeding with the suite.", "Popup", Log.Severity.Warning);
                        return;
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Popup failed with error: {ex.Message}", "Popup", Log.Severity.Error);
                _log.WriteLog($"For safety purposes in case this is an important package, proceeding with the suite.", "Popup", Log.Severity.Warning);
            }
        }
    }
}
