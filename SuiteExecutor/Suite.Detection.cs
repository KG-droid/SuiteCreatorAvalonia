using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteOperations.Package;
using DllImportAttribute = System.Runtime.InteropServices.DllImportAttribute;
using CharSet = System.Runtime.InteropServices.CharSet;
using System.Text.RegularExpressions;
using Log = Logger.Log;
using Microsoft.Win32;
using static SuiteTools.UserTools.UserExtensions;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        // The temporary profile Windows signs into during OOBE/ESP (device setup and account setup) is named
        // DefaultUser, DefaultUser0, DefaultUser1, etc. Treating that logon as ESP catches pre-provisioning
        // and other setup-time logons even where OOBEComplete() alone wouldn't (e.g. white-glove / hybrid
        // scenarios where a technician account is signed in but the real user profile hasn't taken over yet).
        private static readonly Regex _defaultUserPattern = new Regex(@"^DefaultUser\d*$", RegexOptions.IgnoreCase);

        // OOBEComplete reports whether the device has finished its very first-boot experience (OOBE), not
        // whether Autopilot/ESP specifically was involved. It stays false for the entire time OOBE is on
        // screen, and Autopilot's Enrollment Status Page (device + account setup) is still part of OOBE, so
        // this is false throughout ESP until the user reaches the desktop. Devices that never run ESP finish
        // OOBE in a couple of minutes during initial provisioning, long before any suite would ever run, so
        // this reads "not in ESP" for them without needing to know whether Autopilot is involved at all.
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int OOBEComplete(ref int bIsOOBEComplete);

        private bool IsDeviceInEsp()
        {
            bool oobeIncomplete = false;
            try
            {
                int isOobeComplete = 0;
                OOBEComplete(ref isOobeComplete);
                oobeIncomplete = isOobeComplete == 0;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to check OOBE/ESP state: {ex.Message}, assuming OOBE is complete", "SuiteDetection", Log.Severity.Warning);
            }

            if (oobeIncomplete)
            {
                return true;
            }

            return IsLoggedOnUserADefaultUser();
        }

        private bool IsLoggedOnUserADefaultUser()
        {
            try
            {
                List<UserSessionInfo>? sessions = GetUserSessions();
                if (sessions == null)
                {
                    return false;
                }

                foreach (UserSessionInfo session in sessions)
                {
                    if (!string.IsNullOrEmpty(session.UserName) && _defaultUserPattern.IsMatch(session.UserName))
                    {
                        _log.WriteLog($"Logged on user '{session.UserName}' (session {session.SessionID}) matches the DefaultUser pattern, treating as ESP", "SuiteDetection", Log.Severity.Info);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to check logged on user for a DefaultUser profile: {ex.Message}, assuming it is not a DefaultUser", "SuiteDetection", Log.Severity.Warning);
                return false;
            }
        }

        private bool IsSelfDetected()
        {
            Guid detectedGuid = _suiteConfig.BuildSettings.UpgradeCode;
            string detectionKey = Path.Combine(_uninstallKeyBase32, $"{{{detectedGuid.ToString()}}}");
            if (_suiteConfig.BuildSettings.Architecture == Architecture.x64)
            {
                detectionKey = Path.Combine(_uninstallKeyBase64, $"{{{detectedGuid.ToString()}}}");
            }
            bool comparisonResult = false;
            using var key = Registry.LocalMachine.OpenSubKey(detectionKey);
            {
                if (key != null)
                {
                    _log.WriteLog($"Found Suite registry detection key: HKLM:\\{detectionKey}, checking version", "SuiteDetection", Log.Severity.Info);
                    var installedVersionObj = key.GetValue("Version") ?? key.GetValue("DisplayVersion");
                    if (installedVersionObj != null)
                    {
                        string installedVersionStr = installedVersionObj.ToString();
                        Version suiteVersion = _suiteConfig.BuildSettings.SuiteVersion;

                        if (Version.TryParse(installedVersionStr, out var installedVersion))
                        {
                            if (installedVersion >= suiteVersion)
                            {
                                _log.WriteLog($"Installed Version: {installedVersion}, is greater or equal to this suite Version: {suiteVersion}", "SuiteDetection", Log.Severity.Info);
                                return true;
                            }
                            else
                            {
                                _log.WriteLog($"Installed Version: {installedVersion}, is less than this Suite Version: {suiteVersion}", "SuiteDetection", Log.Severity.Info);
                                return false;
                            }
                        }
                        else
                        {
                            _log.WriteLog($"Failed to parse version(s). Installed: {installedVersionStr}, Suite: {suiteVersion}", "SuiteDetection", Log.Severity.Info);
                            return false;
                        }
                    }
                    else
                    {
                        _log.WriteLog("Suite registry key found, but no Version or DisplayVersion found.", "SuiteDetection", Log.Severity.Info);
                        return false;
                    }
                }
                else
                {
                    _log.WriteLog($"Suite detection registry key not found in registry key: {detectionKey}", "SuiteDetection", Log.Severity.Info);
                    return false;
                }
            }
        }

        private enum FamilyRunDecision
        {
            Proceed,        // No other instance of this suite family + action is currently running.
            Skip,           // Another live instance is running a version/revision that is >= ours — never
                             // worth running a second time (a same-version race) and never worth pre-empting
                             // a genuinely newer live run. Exit without running.
            WaitThenProceed // Another live instance is running a strictly older version/revision. Don't kill
                             // it and don't run alongside it — wait for it to finish, then run ourselves so
                             // the machine still ends up on this newer version.
        }

        private FamilyRunDecision ResolveFamilyRunDecision()
        {
            _log.WriteLog($"Checking if this suite family + action is already running", "SuiteFamilyCheck", Log.Severity.Info);
            if (SetFamilyMutexActive())
            {
                _log.WriteLog($"No Suite in this upgrade family is already running", "SuiteFamilyCheck", Log.Severity.Info);
                return FamilyRunDecision.Proceed;
            }
            _log.WriteLog($"Another suite in this upgrade family is running, checking its version and revision", "SuiteFamilyCheck", Log.Severity.Info);
            Guid detectedGuid = _suiteConfig.BuildSettings.UpgradeCode;
            string detectionKey = Path.Combine(_uninstallKeyBase32, $"{{{detectedGuid.ToString()}}}");
            if (_suiteConfig.BuildSettings.Architecture == Architecture.x64)
            {
                detectionKey = Path.Combine(_uninstallKeyBase64, $"{{{detectedGuid.ToString()}}}");
            }
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(detectionKey);
            {
                if (key != null)
                {
                    var activeVersionValue = key.GetValue("LastSuiteVersionRan");
                    var activeRevisionValue = key.GetValue("LastSuiteRevisionRan");
                    if (activeVersionValue != null && activeRevisionValue != null)
                    {
                        Version.TryParse(activeVersionValue.ToString(), out var activeVersion);
                        int.TryParse(activeRevisionValue.ToString(), out var activeRevision);

                        Version thisVersion = _suiteConfig.BuildSettings.SuiteVersion;
                        int thisRevision = _suiteConfig.BuildSettings.Revision;

                        int cmp = (activeVersion ?? new Version(0, 0)).CompareTo(thisVersion);
                        if (cmp == 0) cmp = activeRevision.CompareTo(thisRevision);

                        if (cmp >= 0)
                        {
                            _log.WriteLog($"Running instance is version {activeVersion} revision {activeRevision}, which is the same as or newer than this run ({thisVersion} revision {thisRevision}); exiting this instance.", "SuiteFamilyCheck", Log.Severity.Info);
                            return FamilyRunDecision.Skip;
                        }

                        _log.WriteLog($"Running instance is version {activeVersion} revision {activeRevision}, which is older than this run ({thisVersion} revision {thisRevision}); will wait for it to finish before proceeding.", "SuiteFamilyCheck", Log.Severity.Info);
                        return FamilyRunDecision.WaitThenProceed;
                    }
                    else
                    {
                        // Another instance holds the mutex but hasn't (yet, or no longer) recorded its version —
                        // can't tell whether it's a duplicate of us or a genuine older run. Waiting is always
                        // safe (worst case we wait for a same-version run to finish, which is harmless), whereas
                        // proceeding unconditionally is exactly the double-run this check exists to prevent.
                        _log.WriteLog("Suite registry key detected, but no Version found; waiting for the other instance to finish before proceeding.", "SuiteFamilyCheck", Log.Severity.Info);
                        return FamilyRunDecision.WaitThenProceed;
                    }
                }
                else
                {
                    _log.WriteLog("Suite registry key not detected in registry, but another suite in the family is running; waiting for it to finish before proceeding.", "SuiteFamilyCheck", Log.Severity.Info);
                    return FamilyRunDecision.WaitThenProceed;
                }
            }
        }

        private bool IsAllPackagesDetected()
        {
            List<PackageBase>? detectedPkgs = DetectedPackages();
            if (detectedPkgs == null || detectedPkgs.Count < 1)
            {
                return false;
            }
            List<PackageBase>? suitePkgs = _suiteConfig.Packages;
            foreach (PackageBase pkg in suitePkgs)
            {
                if (detectedPkgs == null || !detectedPkgs.Contains(pkg))
                {
                    return false;
                }
            }
            return true;
        }

        private bool IsAnyPackagesDetected()
        {
            List<PackageBase>? detectedPkgs = DetectedPackages();
            if (detectedPkgs != null && detectedPkgs.Count > 0)
            {
                return true;
            }
            return false;
        }

        private List<PackageBase>? DetectedPackages()
        {
            return _suiteConfig.Packages.Where(pkg => IsPackageDetected(pkg)).ToList();
        }

        private bool IsPackageDetected(PackageBase pkg, bool useRollbackDetection = false)
        {
            switch (pkg)
            {
                case MSIExec msi:
                    {
                        MSIExec detectionTarget = useRollbackDetection && msi.Rollback is MSIExec rollbackMsi ? rollbackMsi : msi;
                        _log.WriteLog($"Checking {(useRollbackDetection ? "rollback " : string.Empty)}detection for MSI Package: {pkg.Name}", "Startup", Log.Severity.Info);
                        PackageExecDetectionResult result = detectionTarget.IsDetected();
                        _log.WriteLog($"Detection result: {result.Result}, summary: {result.Summary}", "Startup", Log.Severity.Info);
                        return result.Result;
                    }
                case MSIRemovalExec msiRem:
                    {
                        _log.WriteLog($"Checking detection for MSI Removal: {pkg.Name}", "Startup", Log.Severity.Info);
                        PackageExecDetectionResult result = msiRem.IsRemovalRequired();
                        _log.WriteLog($"Detection result: {result.Result}, summary: {result.Summary}", "Startup", Log.Severity.Info);
                        return result.Result;
                    }
                case MSIxExec msix:
                    {
                        MSIxExec detectionTarget = useRollbackDetection && msix.Rollback is MSIxExec rollbackMsix ? rollbackMsix : msix;
                        _log.WriteLog($"Checking {(useRollbackDetection ? "rollback " : string.Empty)}detection for MSIx Package: {pkg.Name}", "Startup", Log.Severity.Info);
                        PackageExecDetectionResult result = detectionTarget.IsDetected();
                        _log.WriteLog($"Detection result: {result.Result}, summary: {result.Summary}", "Startup", Log.Severity.Info);
                        return result.Result;
                    }
                case MSIxRemovalExec msixRem:
                    {
                        _log.WriteLog($"Checking detection for MSIx Removal: {pkg.Name}", "Startup", Log.Severity.Info);
                        PackageExecDetectionResult result = msixRem.IsRemovalRequired();
                        _log.WriteLog($"Detection result: {result.Result}, summary: {result.Summary}", "Startup", Log.Severity.Info);
                        return result.Result;
                    }
                case OtherExec otherPkg:
                    {
                        Guid? ruleSetId = useRollbackDetection && otherPkg.RollbackDetectionRuleSetId != null
                            ? otherPkg.RollbackDetectionRuleSetId
                            : otherPkg.DetectionRuleSetId;
                        _log.WriteLog($"Checking {(useRollbackDetection ? "rollback " : string.Empty)}detection for Other Package: {otherPkg.Name}", "Startup", Log.Severity.Info);
                        RuleSet? matchingRuleSet = _suiteConfig.RuleSets.FirstOrDefault(rs => rs.Id == ruleSetId);
                        if (matchingRuleSet == null) throw new Exception($"No matching RuleSet found for Package {otherPkg.Name} with DetectionRuleSetId {ruleSetId}");
                        RuleResult ruleResult = matchingRuleSet.ParseRuleSet();
                        _log.WriteLog($"Detection result: {ruleResult.IsMet}, summary: {ruleResult.Summary}", "Startup", Log.Severity.Info);
                        return ruleResult.IsMet;
                    }
                case OtherRemovalExec otherPkgRem:
                    {
                        _log.WriteLog($"Checking detection for Other Package Removal: {otherPkgRem.Name}", "Startup", Log.Severity.Info);
                        if (otherPkgRem.RemovalType == OtherRemovalType.RegEx)
                        {
                            _log.WriteLog($"Regex removal searches for matching products itself, skipping detection for Package {otherPkgRem.Name}", "Startup", Log.Severity.Info);
                            return true;
                        }
                        RuleSet? matchingRuleSet = _suiteConfig.RuleSets.FirstOrDefault(rs => rs.Id == otherPkgRem.DetectionRuleSetId);
                        if (matchingRuleSet == null) throw new Exception($"No matching RuleSet found for Package {otherPkgRem.Name} with DetectionRuleSetId {otherPkgRem.DetectionRuleSetId}");
                        RuleResult ruleResult = matchingRuleSet.ParseRuleSet();
                        _log.WriteLog($"Detection result: {ruleResult.IsMet}, summary: {ruleResult.Summary}", "Startup", Log.Severity.Info);
                        return ruleResult.IsMet;
                    }
                default:
                    {
                        throw new Exception($"Unknown package type for detection: {pkg.GetType().Name}, on Package: {pkg.Name}");
                    }
            }
        }
    }
}
