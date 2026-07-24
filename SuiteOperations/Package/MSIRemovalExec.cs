using Logger;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteTools;
using static SuiteTools.MSITools;
using Contexts = SuiteCreatorAvalonia.Enums.Contexts;
using RestartBehaviorEnum = SuiteCreatorAvalonia.Enums.RestartBehavior;

namespace SuiteOperations.Package
{
    public class MSIRemovalExec : MSIRemoval
    {
        private Log _log;

        public MSIRemovalExec(Log log)
        {
            _log = log;
        }

        public MSIRemovalExec() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        private ActionType ApplyRestartBehavior(int exitCode)
        {
            bool restartRequested = exitCode == 3010;
            ActionType baseAction = restartRequested ? ActionType.RestartImmediate : ActionType.Continue;
            return RestartBehavior switch
            {
                RestartBehaviorEnum.AlwaysImmediate => ActionType.RestartImmediate,
                RestartBehaviorEnum.AlwaysDelayed => ActionType.RestartDelayed,
                RestartBehaviorEnum.BasedOnReturnCodeImmediate when restartRequested => ActionType.RestartImmediate,
                RestartBehaviorEnum.BasedOnReturnCodeDelayed when restartRequested => ActionType.RestartDelayed,
                RestartBehaviorEnum.Ignore => ActionType.Continue,
                _ => baseAction
            };
        }

        public ActionType Execute()
        {
            Validate();
            ActionType action;
            if (Context == Contexts.User)
            {
                action = ExecutePerUser();
            }
            else
            {
                action = ExecuteSystem();
            }
            _log.WriteLog("Uninstall Complete");
            return action;
        }

        private ActionType ExecuteSystem()
        {
            if (IsProductRemoval)
            {
                _log.WriteLog($"Running MSI Product removal via ProductCode: {RemovalCode}");
                MSIResult result = MSITools.UninstallMSI(RemovalCode, LogPath,
                    Properties?.ConvertAll(p => new MSITools.MSIProp(p.Name!, p.Value!)));
                _log.WriteLog($"Uninstall command: {result.CommandRun}");
                if (!result.Success)
                {
                    _log.WriteLog($"Uninstall error: {result.ErrorMessage}", "Application", Log.Severity.Error);
                    throw new Exception($"Uninstall error: {result.ErrorMessage}");
                }
                return ApplyRestartBehavior(result.ExitCode);
            }
            else
            {
                _log.WriteLog($"Running MSI Family removal via UpgradeCode: {RemovalCode}");
                MSIResult result = MSITools.UninstallMSIFamily(RemovalCode, LogPath,
                    Properties?.ConvertAll(p => new MSITools.MSIProp(p.Name!, p.Value!)));
                _log.WriteLog($"Uninstall command: {result.CommandRun}");
                if (!result.Success)
                {
                    _log.WriteLog($"Uninstall error: {result.ErrorMessage}", "Application", Log.Severity.Error);
                    throw new Exception($"Uninstall error: {result.ErrorMessage}");
                }
                return ApplyRestartBehavior(result.ExitCode);
            }
        }

        private ActionType ExecutePerUser()
        {
            ActionType worstAction = ActionType.Continue;
            if (IsProductRemoval)
            {
                _log.WriteLog($"Running per-user MSI Product removal via ProductCode: {RemovalCode}");
                List<MSIResult> results = UserTools.ProcessExtensions.RunAsAllUsersImpersonated<MSIResult>(() =>
                    MSITools.UninstallMSI(RemovalCode, LogPath,
                        Properties?.ConvertAll(p => new MSITools.MSIProp(p.Name!, p.Value!)))
                );
                foreach (MSIResult result in results)
                {
                    _log.WriteLog($"Uninstall command: {result.CommandRun}");
                    if (!result.Success)
                    {
                        _log.WriteLog($"Uninstall error: {result.ErrorMessage}", "Application", Log.Severity.Error);
                        throw new Exception($"Uninstall error: {result.ErrorMessage}");
                    }
                    ActionType action = ApplyRestartBehavior(result.ExitCode);
                    if (action == ActionType.RestartImmediate) worstAction = ActionType.RestartImmediate;
                    else if (action == ActionType.RestartDelayed && worstAction != ActionType.RestartImmediate) worstAction = ActionType.RestartDelayed;
                }
            }
            else
            {
                _log.WriteLog($"Running per-user MSI Family removal via UpgradeCode: {RemovalCode}");
                List<MSIResult> results = UserTools.ProcessExtensions.RunAsAllUsersImpersonated<MSIResult>(() =>
                    MSITools.UninstallMSIFamily(RemovalCode, LogPath,
                        Properties?.ConvertAll(p => new MSITools.MSIProp(p.Name!, p.Value!)))
                );
                foreach (MSIResult result in results)
                {
                    _log.WriteLog($"Uninstall command: {result.CommandRun}");
                    if (!result.Success)
                    {
                        _log.WriteLog($"Uninstall error: {result.ErrorMessage}", "Application", Log.Severity.Error);
                        throw new Exception($"Uninstall error: {result.ErrorMessage}");
                    }
                    ActionType action = ApplyRestartBehavior(result.ExitCode);
                    if (action == ActionType.RestartImmediate) worstAction = ActionType.RestartImmediate;
                    else if (action == ActionType.RestartDelayed && worstAction != ActionType.RestartImmediate) worstAction = ActionType.RestartDelayed;
                }
            }
            return worstAction;
        }

        public new void Validate()
        {
            if (string.IsNullOrWhiteSpace(RemovalCode))
            {
                throw new ArgumentNullException("Removal code is null. An Upgrade, or Product code is required (Determined by removal type).");
            }
            if (IsCreateLog && string.IsNullOrWhiteSpace(LogPath))
            {
                throw new ArgumentNullException("Log Path is required when logging is enabled.");
            }
            if (IsCreateLog && !string.IsNullOrWhiteSpace(LogPath) && !Path.IsPathFullyQualified(LogPath))
            {
                throw new ArgumentNullException("Log Path must be a fully qualified path.");
            }
        }

        public PackageExecDetectionResult IsRemovalRequired()
        {
            PackageExecDetectionResult result = new();
            if (string.IsNullOrWhiteSpace(RemovalCode))
                throw new ArgumentNullException(nameof(RemovalCode), "RemovalCode is null or whitespace");
            if (IsProductRemoval)
            {
                result.Result = MSITools.IsMSIProductInstalledForAllUsers(RemovalCode, Architecture);
                result.Summary = $"Checking for installed products matching the Product code: {RemovalCode}";
            }
            else
            {
                result.Summary = $"Checking for products in the upgrade family: {RemovalCode}";
                var related = MSITools.GetRelatedMSIProducts(RemovalCode);
                if (related.Any())
                    result.Result = true;
                else
                    result.Result = false;
            }
            return result;
        }
    }
}
