using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteOperations.Package;
using System.Runtime.InteropServices;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private PackageBase? ResolvePackage(Stage stage)
        {
            return _suiteConfig.Packages.FirstOrDefault(p => p.Id == stage.Id);
        }

        private bool IsPackageRequirementMet(PackageBase package)
        {
            if (package.RequirementRuleSetId == null)
            {
                return true;
            }

            RuleSet? ruleSet = _suiteConfig.RuleSets?.FirstOrDefault(rs => rs.Id == package.RequirementRuleSetId);
            if (ruleSet == null)
            {
                _log.WriteLog($"Requirement RuleSet not found for package: {package.Name}, proceeding anyway", "Execution", Log.Severity.Warning);
                return true;
            }

            RuleResult ruleResult = ruleSet.ParseRuleSet();
            _log.WriteLog($"Requirement check for {package.Name}: {ruleResult.IsMet} - {ruleResult.Summary}", "Execution", Log.Severity.Info);
            return ruleResult.IsMet;
        }

        private bool ShouldPackageExecute(PackageBase package)
        {
            if (!IsPackageRequirementMet(package))
            {
                _log.WriteLog($"Package requirement not met for: {package.Name}, skipping stage, and associated events", "Execution", Log.Severity.Info);
                return false;
            }

            bool isDetected = IsPackageDetected(package);
            bool isRemovalType = package is MSIRemovalExec or MSIxRemovalExec or OtherRemovalExec;

            if (_action == SuiteAction.Deployment)
            {
                if (isRemovalType)
                {
                    // Removal packages run during deployment if their target is still present
                    if (!isDetected)
                    {
                        _log.WriteLog($"Removal target for {package.Name} is not detected, skipping stage, and associated events", "Execution", Log.Severity.Info);
                        return false;
                    }
                }
                else
                {
                    // Install packages run during deployment only if not already detected
                    if (isDetected)
                    {
                        _log.WriteLog($"Package {package.Name} is already detected during {_action}, skipping stage, and associated events", "Execution", Log.Severity.Info);
                        return false;
                    }
                }
            }
            else if (_action == SuiteAction.Removal || _action == SuiteAction.Rollback)
            {
                if (isRemovalType)
                {
                    // Removal packages have no reverse action during removal/rollback
                    _log.WriteLog($"Removal package {package.Name} has no reverse action during {_action}, skipping stage, and associated events", "Execution", Log.Severity.Info);
                    return false;
                }
                else
                {
                    // Install packages run during removal/rollback only if currently detected
                    if (!isDetected)
                    {
                        _log.WriteLog($"Package {package.Name} is not detected during {_action}, skipping stage, and associated events", "Execution", Log.Severity.Info);
                        return false;
                    }
                }
            }

            return true;
        }

        private void ExecutePackage(PackageBase package)
        {
            try
            {
                switch (_action)
                {
                    case SuiteAction.Deployment:
                        ExecutePackageDeploy(package);
                        break;
                    case SuiteAction.Removal:
                        ExecutePackageRemove(package);
                        break;
                    case SuiteAction.Rollback:
                        ExecutePackageRollback(package);
                        break;
                }

                VerifyPackageExecution(package);
            }
            catch (COMException ex)
            {
                _lastFailedPackage = package;
                string detail = ex.Data.Contains("ErrorText")
                    ? ex.Data["ErrorText"]?.ToString() ?? ex.Message
                    : ex.Message;
                if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = $"HRESULT 0x{ex.HResult:X8}";
                }
                string actionVerb = _action switch
                {
                    SuiteAction.Deployment => "deployment",
                    SuiteAction.Removal => "removal",
                    SuiteAction.Rollback => "rollback",
                    _ => "execution"
                };
                throw new InvalidOperationException($"{package.Name} {actionVerb} failed: {detail}", ex);
            }
            catch (Exception ex)
            {
                _lastFailedPackage = package;
                string errorDetail = ex.InnerException != null ? $"{ex.Message} --> {ex.InnerException.Message}" : ex.Message;
                _log.WriteLog($"Package execution failed for {package.Name}: {errorDetail}", "Execution", Log.Severity.Error);
                throw;
            }
        }

        // Best-effort cleanup before a Deployment retry: an install that failed partway through can leave the
        // package in a state where a straight re-run just fails again, so uninstall it first. Removal-type
        // packages (MSIRemovalExec/MSIxRemovalExec/OtherRemovalExec) have no symmetric "undo" action and are
        // skipped. Any failure here is swallowed — the retry should proceed regardless.
        private void TryUninstallFailedPackage(PackageBase package)
        {
            try
            {
                _log.WriteLog($"Attempting to uninstall {package.Name} before retrying deployment, since it failed", "Execution", Log.Severity.Info);
                switch (package)
                {
                    case MSIExec msi:
                        msi.ExecuteUninstall();
                        break;
                    case MSIxExec msix:
                        msix.ExecuteUninstall();
                        break;
                    case OtherExec other:
                        other.ExecuteUninstall();
                        break;
                    default:
                        _log.WriteLog($"No uninstall action available for package type {package.GetType().Name} ({package.Name}), skipping pre-retry uninstall", "Execution", Log.Severity.Info);
                        return;
                }
                _log.WriteLog($"Pre-retry uninstall of {package.Name} completed", "Execution", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Pre-retry uninstall of {package.Name} failed, continuing with retry anyway: {ex.Message}", "Execution", Log.Severity.Warning);
            }
        }

        private void VerifyPackageExecution(PackageBase package)
        {
            bool isRemovalType = package is MSIRemovalExec or MSIxRemovalExec or OtherRemovalExec;

            bool didExecute = _action switch
            {
                SuiteAction.Deployment => true,
                SuiteAction.Removal => !isRemovalType,
                SuiteAction.Rollback => package is MSIExec or OtherExec,
                _ => false
            };

            if (!didExecute)
                return;

            if (package is OtherRemovalExec { RemovalType: OtherRemovalType.RegEx })
            {
                _log.WriteLog($"Skipping post-execution detection check for {package.Name}: RegEx removals have no detection rule set to re-check", "Execution", Log.Severity.Info);
                return;
            }

            bool expectedDetected = _action switch
            {
                SuiteAction.Deployment => !isRemovalType,
                // A configured rollback reinstalls a previous version, so the package is expected to be
                // detected again afterward; a rollback with no configured reinstall just uninstalls.
                SuiteAction.Rollback => package switch
                {
                    MSIExec msi => msi.Rollback != null,
                    OtherExec other => other.HasRollbackCommand,
                    _ => false
                },
                _ => false
            };
            bool isDetected = IsPackageDetected(package);

            if (isDetected == expectedDetected)
            {
                _log.WriteLog($"Post-execution detection verified for {package.Name}: {(expectedDetected ? "detected" : "not detected")} as expected", "Execution", Log.Severity.Info);
                return;
            }

            _log.WriteLog($"Post-execution detection failed for {package.Name}: expected {(expectedDetected ? "detected" : "not detected")}, but found {(isDetected ? "detected" : "not detected")}", "Execution", Log.Severity.Error);
            throw new InvalidOperationException($"Post-execution detection check failed for {package.Name}");
        }

        private void ExecutePackageDeploy(PackageBase package)
        {
            switch (package)
            {
                case MSIExec msi:
                    _log.WriteLog($"Installing MSI package: {msi.Name}", "Execution", Log.Severity.Info);
                    msi.ExecuteInstall();
                    break;
                case MSIRemovalExec msiRem:
                    _log.WriteLog($"Executing MSI removal: {msiRem.Name}", "Execution", Log.Severity.Info);
                    msiRem.Execute();
                    break;
                case MSIxExec msix:
                    _log.WriteLog($"Installing MSIx package: {msix.Name}", "Execution", Log.Severity.Info);
                    msix.ExecuteInstall();
                    break;
                case MSIxRemovalExec msixRem:
                    _log.WriteLog($"Executing MSIx removal: {msixRem.Name}", "Execution", Log.Severity.Info);
                    msixRem.Execute();
                    break;
                case OtherExec other:
                    _log.WriteLog($"Installing Other package: {other.Name}", "Execution", Log.Severity.Info);
                    ActionType otherResult = other.ExecuteInstall();
                    HandleActionType(otherResult, other.Name!, other.RestartCountdown);
                    break;
                case OtherRemovalExec otherRem:
                    _log.WriteLog($"Executing Other removal: {otherRem.Name}", "Execution", Log.Severity.Info);
                    ActionType otherRemResult = otherRem.ExecuteUninstall();
                    HandleActionType(otherRemResult, otherRem.Name!, otherRem.RestartCountdown);
                    break;
                default:
                    _log.WriteLog($"Unknown package type: {package.GetType().Name} for {package.Name}", "Execution", Log.Severity.Warning);
                    break;
            }
        }

        private void ExecutePackageRemove(PackageBase package)
        {
            switch (package)
            {
                case MSIExec msi:
                    _log.WriteLog($"Uninstalling MSI package: {msi.Name}", "Execution", Log.Severity.Info);
                    msi.ExecuteUninstall();
                    break;
                case MSIxExec msix:
                    _log.WriteLog($"Uninstalling MSIx package: {msix.Name}", "Execution", Log.Severity.Info);
                    msix.ExecuteUninstall();
                    break;
                case OtherExec other:
                    _log.WriteLog($"Uninstalling Other package: {other.Name}", "Execution", Log.Severity.Info);
                    ActionType otherResult = other.ExecuteUninstall();
                    HandleActionType(otherResult, other.Name!, other.RestartCountdown);
                    break;
                case MSIRemovalExec:
                case MSIxRemovalExec:
                case OtherRemovalExec:
                    _log.WriteLog($"Skipping {package.GetType().Name} ({package.Name}) during removal - no reverse action available", "Execution", Log.Severity.Info);
                    break;
                default:
                    _log.WriteLog($"Unknown package type for removal: {package.GetType().Name} ({package.Name})", "Execution", Log.Severity.Warning);
                    break;
            }
        }

        private void ExecutePackageRollback(PackageBase package)
        {
            switch (package)
            {
                case MSIExec msi:
                    _log.WriteLog($"Rolling back MSI package: {msi.Name}", "Execution", Log.Severity.Info);
                    msi.ExecuteRollback();
                    break;
                case OtherExec other:
                    _log.WriteLog($"Rolling back Other package: {other.Name}", "Execution", Log.Severity.Info);
                    ActionType otherResult = other.ExecuteRollback();
                    HandleActionType(otherResult, other.Name!, other.RestartCountdown);
                    break;
                case MSIRemovalExec:
                case MSIxRemovalExec:
                case MSIxExec:
                case OtherRemovalExec:
                    _log.WriteLog($"Skipping {package.GetType().Name} ({package.Name}) during rollback", "Execution", Log.Severity.Info);
                    break;
                default:
                    _log.WriteLog($"Unknown package type for rollback: {package.GetType().Name} ({package.Name})", "Execution", Log.Severity.Warning);
                    break;
            }
        }

        private void HandleActionType(ActionType actionType, string packageName, int restartCountdown = 60)
        {
            switch (actionType)
            {
                case ActionType.Continue:
                    break;
                case ActionType.Abort:
                    throw new Exception($"Package {packageName} returned Abort");
                case ActionType.RestartImmediate:
                    _log.WriteLog($"Package {packageName} requested an immediate restart, rebooting in {restartCountdown} seconds", "Execution", Log.Severity.Warning);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        SystemPaths.Shutdown,
                        $"/r /t {restartCountdown} /c \"This computer will restart in {restartCountdown} seconds to complete a software installation.\""
                    ) { UseShellExecute = false, CreateNoWindow = true });
                    Environment.Exit(0);
                    break;
                case ActionType.RestartDelayed:
                    _log.WriteLog($"Package {packageName} requested a delayed restart, exiting with reboot code 3010", "Execution", Log.Severity.Warning);
                    Environment.Exit(3010);
                    break;
            }
        }
    }
}
