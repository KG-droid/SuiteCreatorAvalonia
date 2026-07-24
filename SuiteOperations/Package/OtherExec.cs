using Logger;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using Contexts = SuiteCreatorAvalonia.Enums.Contexts;

namespace SuiteOperations.Package
{
    public class OtherExec : OtherExecBase
    {
        private Log _log;

        public OtherExec(Log log, List<RuleSet> RuleSets) : base(log, RuleSets)
        {
            _log = log;
        }

        public OtherExec() : base() { }

        public override void SetLog(Log log)
        {
            _log = log;
            base.SetLog(log);
        }

        public ActionType ExecuteInstall()
        {
            Validate();
            _log.WriteLog($"Running package install for: {Name}", "OtherPkg", Log.Severity.Info);

            if (InstallType == OtherInstallType.PowerShell)
            {
                return ExecutePowerShellInstall();
            }

            string fullText = ParseAndValidateCommand(InstallCommand, "InstallCommand");
            string args = GetCommandArguments(fullText, GetExePath(fullText));
            string execPath = GetValidatedExePath(fullText);

            if (Context == Contexts.User)
            {
                _log.WriteLog("Running install for all active user sessions.", "OtherPkg", Log.Severity.Info);
                List<ActionType> results = ExecuteAndActionReturnTypePerUser(execPath, args);
                return results.Any(r => r == ActionType.Abort) ? ActionType.Abort
                    : results.Any(r => r == ActionType.RestartImmediate) ? ActionType.RestartImmediate
                    : results.Any(r => r == ActionType.RestartDelayed) ? ActionType.RestartDelayed
                    : ActionType.Continue;
            }

            return ExecuteAndActionReturnType(execPath, SourceDir, args);
        }

        private ActionType ExecutePowerShellInstall()
        {
            if (string.IsNullOrWhiteSpace(PowerShellScriptPath))
                throw new Exception("PowerShell script path must be specified for a PowerShell install.");

            string scriptPath = PowerShellScriptPath;
            if (!Path.IsPathFullyQualified(scriptPath))
                scriptPath = Path.Combine(SourceDir ?? throw new ArgumentNullException(nameof(SourceDir)), scriptPath);

            if (!File.Exists(scriptPath))
                throw new Exception($"PowerShell script not found: {scriptPath}");

            string execPath = "powershell.exe";
            string args = $"-ExecutionPolicy Bypass -NonInteractive -File \"{scriptPath}\"{(string.IsNullOrWhiteSpace(PowerShellScriptArgs) ? string.Empty : " " + PowerShellScriptArgs)}";

            _log.WriteLog($"Running PowerShell script: {scriptPath}", "OtherPkg", Log.Severity.Info);

            if (Context == Contexts.User)
            {
                _log.WriteLog("Running PowerShell install for all active user sessions.", "OtherPkg", Log.Severity.Info);
                List<ActionType> results = ExecuteAndActionReturnTypePerUser(execPath, args, isWindowVisibleToUser: false);
                return results.Any(r => r == ActionType.Abort) ? ActionType.Abort
                    : results.Any(r => r == ActionType.RestartImmediate) ? ActionType.RestartImmediate
                    : results.Any(r => r == ActionType.RestartDelayed) ? ActionType.RestartDelayed
                    : ActionType.Continue;
            }

            return ExecuteAndActionReturnType(execPath, SourceDir, args);
        }

        public ActionType ExecuteUninstall()
        {
            Validate();
            _log.WriteLog($"Running package uninstall for: {Name}", "OtherPkg", Log.Severity.Info);

            if (RemovalType == OtherRemovalType.PowerShell)
                return ExecutePowerShellRemoval();

            if (Removal == null)
            {
                _log.WriteLog("Removal must be specified before executing the package uninstall.", "OtherPkg", Log.Severity.Error);
                throw new Exception("Removal must be specified before executing the package uninstall.");
            }

            switch (Removal)
            {
                case OtherMSIRemoval msi:
                    HandleMSIRemoval(msi);
                    break;
                case OtherRegexRemoval regex:
                    HandleRegexRemoval(regex);
                    break;
                case OtherCMDRemoval cmd:
                    HandleCMDRemoval(cmd);
                    break;
                default:
                    throw new Exception($"Removal type: {Removal?.GetType().Name}, not supported for OtherExec package.");
            }

            WaitForUninstallDetectionToClear(IsDetected);
            return ActionType.Continue;
        }

        private ActionType ExecutePowerShellRemoval()
        {
            if (string.IsNullOrWhiteSpace(RemovePowerShellScriptPath))
                throw new Exception("PowerShell script path must be specified for a PowerShell removal.");

            string scriptPath = RemovePowerShellScriptPath;
            if (!Path.IsPathFullyQualified(scriptPath))
                scriptPath = Path.Combine(SourceDir ?? throw new ArgumentNullException(nameof(SourceDir)), scriptPath);

            if (!File.Exists(scriptPath))
                throw new Exception($"PowerShell script not found: {scriptPath}");

            string execPath = "powershell.exe";
            string args = $"-ExecutionPolicy Bypass -NonInteractive -File \"{scriptPath}\"{(string.IsNullOrWhiteSpace(RemovePowerShellScriptArgs) ? string.Empty : " " + RemovePowerShellScriptArgs)}";

            _log.WriteLog($"Running PowerShell removal script: {scriptPath}", "OtherPkg", Log.Severity.Info);

            if (Context == Contexts.User)
            {
                _log.WriteLog("Running PowerShell removal for all active user sessions.", "OtherPkg", Log.Severity.Info);
                List<ActionType> results = ExecuteAndActionReturnTypePerUser(execPath, args, isWindowVisibleToUser: false);
                ActionType combinedResult = results.Any(r => r == ActionType.Abort) ? ActionType.Abort
                    : results.Any(r => r == ActionType.RestartImmediate) ? ActionType.RestartImmediate
                    : results.Any(r => r == ActionType.RestartDelayed) ? ActionType.RestartDelayed
                    : ActionType.Continue;
                if (combinedResult != ActionType.Abort)
                    WaitForUninstallDetectionToClear(IsDetected);
                return combinedResult;
            }

            ActionType result = ExecuteAndActionReturnType(execPath, SourceDir, args);
            if (result != ActionType.Abort)
                WaitForUninstallDetectionToClear(IsDetected);
            return result;
        }

        public bool HasRollbackCommand => RollbackInstallType == OtherInstallType.PowerShell
            ? !string.IsNullOrWhiteSpace(RollbackPowerShellScriptPath)
            : RollbackCommand != null && RollbackCommand.Any(c => !string.IsNullOrWhiteSpace(c?.GetValue()));

        public ActionType ExecuteRollback()
        {
            Validate();

            _log.WriteLog($"Running package rollback for: {Name}", "OtherPkg", Log.Severity.Info);

            if (RollbackDetectionRuleSetId != null)
            {
                RuleSet? rollbackRuleSet = _ruleSets.FirstOrDefault(r => r.Id == RollbackDetectionRuleSetId);
                if (rollbackRuleSet == null)
                    throw new Exception($"Cannot find the rollback detection ruleset matching ID: {RollbackDetectionRuleSetId}");
                RuleResult rollbackDetection = rollbackRuleSet.ParseRuleSet();
                _log.WriteLog($"Rollback detection check: {rollbackDetection.Summary}", "OtherPkg", Log.Severity.Info);
                if (rollbackDetection.IsMet)
                {
                    _log.WriteLog("Rollback version already detected, skipping rollback install.", "OtherPkg", Log.Severity.Info);
                    return ActionType.Continue;
                }
            }

            _log.WriteLog("Executing uninstall of the new version", "OtherPkg", Log.Severity.Info);
            ExecuteUninstall();

            if (!HasRollbackCommand)
            {
                _log.WriteLog("No rollback command configured, uninstall only.", "OtherPkg", Log.Severity.Info);
                return ActionType.Continue;
            }

            _log.WriteLog("Executing install of the old version", "OtherPkg", Log.Severity.Info);

            if (RollbackInstallType == OtherInstallType.PowerShell)
                return ExecutePowerShellRollback();

            string fullText = ParseAndValidateCommand(RollbackCommand, "RollbackCommand");
            string args = GetCommandArguments(fullText, GetExePath(fullText));
            string execPath = GetValidatedExePath(fullText);

            if (Context == Contexts.User)
            {
                _log.WriteLog("Running rollback install for all active user sessions.", "OtherPkg", Log.Severity.Info);
                List<ActionType> results = ExecuteAndActionReturnTypePerUser(execPath, args);
                return results.Any(r => r == ActionType.Abort) ? ActionType.Abort
                    : results.Any(r => r == ActionType.RestartImmediate) ? ActionType.RestartImmediate
                    : results.Any(r => r == ActionType.RestartDelayed) ? ActionType.RestartDelayed
                    : ActionType.Continue;
            }

            return ExecuteAndActionReturnType(execPath, SourceDir, args);
        }

        private ActionType ExecutePowerShellRollback()
        {
            if (string.IsNullOrWhiteSpace(RollbackPowerShellScriptPath))
                throw new Exception("PowerShell script path must be specified for a PowerShell rollback.");

            string scriptPath = RollbackPowerShellScriptPath;
            if (!Path.IsPathFullyQualified(scriptPath))
                scriptPath = Path.Combine(SourceDir ?? throw new ArgumentNullException(nameof(SourceDir)), scriptPath);

            if (!File.Exists(scriptPath))
                throw new Exception($"PowerShell script not found: {scriptPath}");

            string execPath = "powershell.exe";
            string args = $"-ExecutionPolicy Bypass -NonInteractive -File \"{scriptPath}\"{(string.IsNullOrWhiteSpace(RollbackPowerShellScriptArgs) ? string.Empty : " " + RollbackPowerShellScriptArgs)}";

            _log.WriteLog($"Running PowerShell rollback script: {scriptPath}", "OtherPkg", Log.Severity.Info);

            if (Context == Contexts.User)
            {
                _log.WriteLog("Running PowerShell rollback for all active user sessions.", "OtherPkg", Log.Severity.Info);
                List<ActionType> results = ExecuteAndActionReturnTypePerUser(execPath, args, isWindowVisibleToUser: false);
                return results.Any(r => r == ActionType.Abort) ? ActionType.Abort
                    : results.Any(r => r == ActionType.RestartImmediate) ? ActionType.RestartImmediate
                    : results.Any(r => r == ActionType.RestartDelayed) ? ActionType.RestartDelayed
                    : ActionType.Continue;
            }

            return ExecuteAndActionReturnType(execPath, SourceDir, args);
        }

        public PackageExecDetectionResult IsDetected()
        {
            PackageExecDetectionResult result = new();
            RuleSet? matchingRuleSet = _ruleSets.FirstOrDefault(r => r.Id == DetectionRuleSetId);
            if (matchingRuleSet == null) throw new Exception($"Cannot find the ruleset on the package matching ID: {DetectionRuleSetId}");
            RuleResult ruleResult = matchingRuleSet.ParseRuleSet();
            result.Summary = ruleResult.Summary;
            result.Result = ruleResult.IsMet;
            return result;
        }
    }
}
