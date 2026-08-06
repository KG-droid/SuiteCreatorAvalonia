using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteOperations.Package;
using DllImportAttribute = System.Runtime.InteropServices.DllImportAttribute;
using CharSet = System.Runtime.InteropServices.CharSet;
using Log = Logger.Log;
using Microsoft.Win32;

namespace SuiteExecutor
{
    internal partial class Suite
    {
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
            try
            {
                int isOobeComplete = 0;
                OOBEComplete(ref isOobeComplete);
                return isOobeComplete == 0;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to check OOBE/ESP state: {ex.Message}, assuming the device is not in ESP", "SuiteDetection", Log.Severity.Warning);
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

        private bool IsNewerSuiteInFamilyAlreadyRunning()
        {
            _log.WriteLog($"Checking if a newer version or revision of this suite family is already running", "SuiteFamilyCheck", Log.Severity.Info);
            if (!SetFamilyMutexActive())
            {
                _log.WriteLog($"No Suite in this upgrade family is already running", "SuiteFamilyCheck", Log.Severity.Info);
                return false;
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

                        if (activeVersion > thisVersion) return true;
                        if (activeVersion == thisVersion && activeRevision > thisRevision) return true;
                        return false;
                    }
                    else
                    {
                        _log.WriteLog("Suite registry key detected, but no Version found.", "SuiteFamilyCheck", Log.Severity.Info);
                        return false;
                    }
                }
                else
                {
                    _log.WriteLog("Suite registry key not detected in registry, but another suite in the family is running, so something has gone wrong, so will continue with this suite just in case.", "SuiteFamilyCheck", Log.Severity.Info);
                    return false;
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

        private bool IsPackageDetected(PackageBase pkg)
        {
            switch (pkg)
            {
                case MSIExec msi:
                    {
                        _log.WriteLog($"Checking detection for MSI Package: {pkg.Name}", "Startup", Log.Severity.Info);
                        PackageExecDetectionResult result = msi.IsDetected();
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
                        _log.WriteLog($"Checking detection for MSIx Package: {pkg.Name}", "Startup", Log.Severity.Info);
                        PackageExecDetectionResult result = msix.IsDetected();
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
                        _log.WriteLog($"Checking detection for Other Package: {otherPkg.Name}", "Startup", Log.Severity.Info);
                        RuleSet? matchingRuleSet = _suiteConfig.RuleSets.FirstOrDefault(rs => rs.Id == otherPkg.DetectionRuleSetId);
                        if (matchingRuleSet == null) throw new Exception($"No matching RuleSet found for Package {otherPkg.Name} with DetectionRuleSetId {otherPkg.DetectionRuleSetId}");
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
