using Logger;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using Contexts = SuiteCreatorAvalonia.Enums.Contexts;

namespace SuiteOperations.Package
{
    public class OtherRemovalExec : OtherExecBase
    {
        private Log _log;
        public OtherRemovalExec(Log log, List<RuleSet> RuleSets) : base(log, RuleSets)
        {
            _log = log;
        }

        public OtherRemovalExec() : base() { }

        public override void SetLog(Log log)
        {
            _log = log;
            base.SetLog(log);
        }

        public new void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceDir))
            {
                throw new ArgumentNullException(nameof(SourceDir), "SourceDir must be specified before executing the package.");
            }

            if (RemovalType == OtherRemovalType.PowerShell)
            {
                if (string.IsNullOrWhiteSpace(PowerShellScriptPath))
                    throw new Exception("A PowerShell script must be specified for a PowerShell removal.");
            }
            else if (Removal == null)
            {
                throw new Exception("Removal must be specified before executing the package uninstall.");
            }
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

            // RegEx removals have no detection rule set to re-check: IsRemovalRequired() always
            // reports true for them, which would spin the wait out to the full timeout for nothing.
            if (RemovalType != OtherRemovalType.RegEx)
                WaitForUninstallDetectionToClear(IsRemovalRequired);
            return ActionType.Continue;
        }

        private ActionType ExecutePowerShellRemoval()
        {
            if (string.IsNullOrWhiteSpace(PowerShellScriptPath))
                throw new Exception("PowerShell script path must be specified for a PowerShell removal.");

            string scriptPath = PowerShellScriptPath;
            if (!Path.IsPathFullyQualified(scriptPath))
                scriptPath = Path.Combine(SourceDir ?? throw new ArgumentNullException(nameof(SourceDir)), scriptPath);

            if (!File.Exists(scriptPath))
                throw new Exception($"PowerShell script not found: {scriptPath}");

            string execPath = "powershell.exe";
            string args = $"-ExecutionPolicy Bypass -NonInteractive -File \"{scriptPath}\"{(string.IsNullOrWhiteSpace(PowerShellScriptArgs) ? string.Empty : " " + PowerShellScriptArgs)}";

            _log.WriteLog($"Running PowerShell removal script: {scriptPath}", "OtherPkg", Log.Severity.Info);

            if (Context == Contexts.User)
            {
                _log.WriteLog("Running PowerShell removal for all active user sessions.", "OtherPkg", Log.Severity.Info);
                List<ActionType> results = ExecuteAndActionReturnTypePerUser(execPath, args);
                ActionType combinedResult = results.Any(r => r == ActionType.Abort) ? ActionType.Abort
                    : results.Any(r => r == ActionType.RestartImmediate) ? ActionType.RestartImmediate
                    : results.Any(r => r == ActionType.RestartDelayed) ? ActionType.RestartDelayed
                    : ActionType.Continue;
                if (combinedResult != ActionType.Abort)
                    WaitForUninstallDetectionToClear(IsRemovalRequired);
                return combinedResult;
            }

            ActionType result = ExecuteAndActionReturnType(execPath, SourceDir, args);
            if (result != ActionType.Abort)
                WaitForUninstallDetectionToClear(IsRemovalRequired);
            return result;
        }

        public PackageExecDetectionResult IsRemovalRequired()
        {
            PackageExecDetectionResult result = new();
            if (RemovalType == OtherRemovalType.RegEx)
            {
                result.Summary = "Regex removal searches for matching products itself, no detection rule set is required.";
                result.Result = true;
                return result;
            }
            RuleSet? matchingRuleSet = _ruleSets.FirstOrDefault(r => r.Id == DetectionRuleSetId);
            if (matchingRuleSet == null) throw new Exception($"Cannot find the ruleset on the package matching ID: {DetectionRuleSetId}");
            RuleResult ruleResult = matchingRuleSet.ParseRuleSet();
            result.Summary = ruleResult.Summary;
            result.Result = ruleResult.IsMet;
            return result;
        }
    }
}
