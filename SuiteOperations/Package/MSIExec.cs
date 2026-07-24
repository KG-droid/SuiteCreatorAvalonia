using Logger;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteTools;
using System.Collections;
using System.Runtime.InteropServices;
using static SuiteTools.MSITools;
using Contexts = SuiteCreatorAvalonia.Enums.Contexts;
using RestartBehaviorEnum = SuiteCreatorAvalonia.Enums.RestartBehavior;

namespace SuiteOperations.Package
{
    public class MSIExec : MSI
    {
        private Log _log;
        public string? MSIFile { get; set; }
        public string? TransformsFile { get; set; }
        public string? PatchFile { get; set; }
        public string? ProductCode { get; set; }
        public string? UpgradeCode { get; set; }
        public Architecture Architecture { get; set; }
        public string? RollbackMSIFile { get; set; }
        public string? RollbackTransformsFile { get; set; }
        public string? RollbackPatchFile { get; set; }

        public MSIExec(Log log)
        {
            _log = log;
        }

        public MSIExec() { }

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

        public ActionType ExecuteInstall()
        {
            Validate();
            _log.WriteLog($"Running MSI package install for: {Path.GetFileName(MSIFile)}");
            ActionType action;

            if (Context == Contexts.User)
            {
                action = ExecuteInstallPerUser();
            }
            else
            {
                action = ExecuteInstallSystem();
            }
            _log.WriteLog($"Install Complete");
            return action;
        }

        private string GetEffectivePath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return path!;
            if (LegacyLongFilePath && !path.StartsWith(@"\\?\"))
                return @"\\?\" + path;
            return path;
        }

        private string? GetEffectiveLogPath(string? logPath)
        {
            return IsCreateLog ? logPath : null;
        }

        private ActionType ExecuteInstallSystem()
        {
            MSIResult result = MSITools.InstallMSI(
                GetEffectivePath(MSIFile),
                GetEffectivePath(TransformsFile),
                SecureTransforms,
                GetEffectivePath(PatchFile),
                Properties?.ConvertAll(
                    p => new MSITools.MSIProp(p.Name!, p.Value!)
                ),
                GetEffectiveLogPath(LogPath)
            );
            _log.WriteLog($"Install command was: {result.CommandRun}");
            if (!result.Success)
            {
                throw new Exception($"Install error: {result.ErrorMessage}");
            }
            return ApplyRestartBehavior(result.ExitCode);
        }

        private ActionType ExecuteInstallPerUser()
        {
            // Advertise the MSI as SYSTEM first (machine assignment) so a later msiexec /i run by a
            // non-admin user auto-elevates to complete it, instead of requiring AlwaysInstallElevated.
            _log.WriteLog($"Advertising MSI as SYSTEM for per-user install: {Path.GetFileName(MSIFile)}");
            MSITools.AdvertiseMSI(GetEffectivePath(MSIFile)!, GetEffectivePath(TransformsFile));

            _log.WriteLog("Running per-user MSI install for all active user sessions.");
            string msiPath = GetEffectivePath(MSIFile)!;
            string args = MSITools.BuildInstallArguments(
                msiPath,
                GetEffectivePath(TransformsFile),
                SecureTransforms,
                GetEffectivePath(PatchFile),
                Properties?.ConvertAll(
                    p => new MSITools.MSIProp(p.Name!, p.Value!)
                ),
                GetEffectiveLogPath(LogPath)
            );

            string msiexecPath = MSITools.GetMSIExecPath();
            string commandRun = $"{ msiexecPath} {args}";
            List<UserTools.ProcessExtensions.ImpersonatedProcessResult> procResults = UserTools.ProcessExtensions.StartProcessAsAllUsers(
                msiexecPath,
                args,
                Path.GetDirectoryName(msiPath)!,
                true,
                true
            );

            ActionType worstAction = ActionType.Continue;
            foreach (UserTools.ProcessExtensions.ImpersonatedProcessResult procResult in procResults)
            {
                _log.WriteLog($"Install command was: {commandRun}, as user: {procResult.UserName}");
                if (procResult.ErrorMessage != null)
                    throw new Exception($"Install error: {procResult.ErrorMessage}");
                if (procResult.ExitCode != 0 && procResult.ExitCode != 3010)
                    throw new Exception($"Install error: MSI installation failed with exit code {procResult.ExitCode} for user {procResult.UserName}. Error: {MSITools.GetMSIErrorDescription(procResult.ExitCode)}");
                ActionType action = ApplyRestartBehavior(procResult.ExitCode);
                if (action == ActionType.RestartImmediate) worstAction = ActionType.RestartImmediate;
                else if (action == ActionType.RestartDelayed && worstAction != ActionType.RestartImmediate) worstAction = ActionType.RestartDelayed;
            }
            return worstAction;
        }

        private string? GetUninstallLogPath()
        {
            string? logPath = GetEffectiveLogPath(LogPath);
            if (string.IsNullOrEmpty(logPath)) return null;
            string extension = Path.GetExtension(logPath);
            if (string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(
                    Path.GetDirectoryName(logPath)!,
                    Path.GetFileNameWithoutExtension(logPath) + "_Uninstall" + extension
                );
            }
            return logPath + "_Uninstall";
        }

        public ActionType ExecuteUninstall()
        {
            ActionType action;
            if (Context == Contexts.User)
            {
                action = ExecuteUninstallPerUser();
            }
            else
            {
                action = ExecuteUninstallSystem();
            }
            _log.WriteLog($"Uninstall Complete");
            return action;
        }

        private ActionType ExecuteUninstallSystem()
        {
            string uninstallLogPath = GetUninstallLogPath();
            if (IsRemoveFamily)
            {
                if (string.IsNullOrEmpty(UpgradeCode))
                    throw new Exception("Could not determine UpgradeCode from the MSI");
                _log.WriteLog($"Running MSI Family uninstall via UpgradeCode: {UpgradeCode}");
                MSIResult result = MSITools.UninstallMSIFamily(UpgradeCode, uninstallLogPath);
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
                if (string.IsNullOrEmpty(ProductCode))
                    throw new Exception("Could not determine ProductCode from the MSI");
                _log.WriteLog($"Running MSI Product uninstall via ProductCode: {ProductCode}");
                MSIResult result = MSITools.UninstallMSI(ProductCode, uninstallLogPath);
                _log.WriteLog($"Uninstall command: {result.CommandRun}");
                if (!result.Success)
                {
                    _log.WriteLog($"Uninstall error: {result.ErrorMessage}", "Application", Log.Severity.Error);
                    throw new Exception($"Uninstall error: {result.ErrorMessage}");
                }
                return ApplyRestartBehavior(result.ExitCode);
            }
        }

        private ActionType ExecuteUninstallPerUser()
        {
            string? uninstallLogPath = GetUninstallLogPath();
            ActionType worstAction = ActionType.Continue;
            if (IsRemoveFamily)
            {
                if (string.IsNullOrEmpty(UpgradeCode))
                    throw new Exception("Could not determine UpgradeCode from the MSI");
                _log.WriteLog($"Running per-user MSI Family uninstall via UpgradeCode: {UpgradeCode}");
                List<MSIResult> results = UserTools.ProcessExtensions.RunAsAllUsersImpersonated<MSIResult>(() =>
                    MSITools.UninstallMSIFamily(UpgradeCode, uninstallLogPath)
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
                if (string.IsNullOrEmpty(ProductCode))
                    throw new Exception("Could not determine ProductCode from the MSI");
                _log.WriteLog($"Running per-user MSI Product uninstall via ProductCode: {ProductCode}");
                List<MSIResult> results = UserTools.ProcessExtensions.RunAsAllUsersImpersonated<MSIResult>(() =>
                    MSITools.UninstallMSI(ProductCode, uninstallLogPath)
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

        public PackageExecDetectionResult IsDetected()
        {
            PackageExecDetectionResult result = new();
            if (IsRemoveFamily)
            {
                if (string.IsNullOrEmpty(UpgradeCode)) 
                    throw new Exception("Could not determine UpgradeCode from the MSI");
                List<MSIProductInfo>? related = GetRelatedMSIProducts(UpgradeCode)?.Where(p => p.Architecture == Architecture).ToList();
                result.Summary = $"Checked for products in the upgrade family: {UpgradeCode}, Found: {related?.Count ?? 0}, Product codes: {string.Join(", ", related?.Select(p => p.ProductCode) ?? Array.Empty<string>())}";
                result.Result = related != null && related.Count > 0;
            }
            else 
            {
                if (string.IsNullOrEmpty(ProductCode)) 
                    throw new Exception("Could not determine ProductCode from the MSI");
                result.Result = IsMSIProductInstalledForAllUsers(ProductCode, Architecture);
                result.Summary = $"Checked for specific product code: {ProductCode}";
            }
            return result;
        }

        public void ExecuteRollback()
        {
            _log.WriteLog($"Rolling back MSI package: {Name}");
            _log.WriteLog("Uninstalling current version as part of rollback.");
            ExecuteUninstall();
            if (Rollback == null)
            {
                _log.WriteLog("No rollback version configured, uninstall only.");
                return;
            }
            _log.WriteLog("Installing rollback version.");
            MSIExec rollbackExec = new(_log)
            {
                MSIFile = RollbackMSIFile,
                TransformsFile = RollbackTransformsFile,
                PatchFile = RollbackPatchFile,
                SecureTransforms = Rollback.SecureTransforms,
                Context = Rollback.Context,
                IsCreateLog = Rollback.IsCreateLog,
                IsRemoveFamily = Rollback.IsRemoveFamily,
                LegacyLongFilePath = Rollback.LegacyLongFilePath,
                LogPath = Rollback.LogPath,
                Properties = Rollback.Properties?.Select(p => p.Clone()).ToList(),
                ProductCode = ProductCode,
                UpgradeCode = UpgradeCode,
                Architecture = Architecture
            };
            rollbackExec.ExecuteInstall();
            _log.WriteLog("Rollback complete.");
        }

        public new void Validate()
        {
            if (string.IsNullOrEmpty(MSIFile))
            {
                throw new ArgumentNullException("MSIFile must be set before executing.", nameof(MSIFile));
            }
            if (!File.Exists(MSIFile))
            {
                throw new FileNotFoundException("The specified MSI file does not exist.", MSIFile);
            }
        }
    }
}