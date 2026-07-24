using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Package
{
    public partial class OtherBase : OtherCore
    {
        public List<VariableText>? InstallCommand { get; set; }
        public List<VariableText>? RollbackCommand { get; set; }

        public override PackageBase Clone()
        {
            return new OtherBase
            {
                Id = Id,
                Name = Name,
                RequirementRuleSetId = RequirementRuleSetId,
                DetectionRuleSetId = DetectionRuleSetId,
                RemovalType = RemovalType,
                Removal = Removal?.Clone(),
                Context = Context,
                SecureParams = SecureParams,
                RestartBehavior = RestartBehavior,
                RestartCountdown = RestartCountdown,
                CustomExitCodes = CustomExitCodes,
                LegacyLongFilePath = LegacyLongFilePath,
                ExitCodes = ExitCodes != null ? ExitCodes.Select(e => e.Clone()).ToList() : null,
                InstallType = InstallType,
                PowerShellScriptPath = PowerShellScriptPath,
                PowerShellScriptArgs = PowerShellScriptArgs,
                RollbackInstallType = RollbackInstallType,
                RollbackPowerShellScriptPath = RollbackPowerShellScriptPath,
                RollbackPowerShellScriptArgs = RollbackPowerShellScriptArgs,
                RemovePowerShellScriptPath = RemovePowerShellScriptPath,
                RemovePowerShellScriptArgs = RemovePowerShellScriptArgs,
                InstallCommand = InstallCommand != null ? InstallCommand.Select(c => c.Clone()).ToList() : null,
                RollbackCommand = RollbackCommand != null ? RollbackCommand.Select(c => c.Clone()).ToList() : null,
                RollbackDetectionRuleSetId = RollbackDetectionRuleSetId
            };
        }
        public override void UpdateFrom(Stage stage)
        {
            if (stage is not OtherBase otherBase) return;
            Id = otherBase.Id;
            Name = otherBase.Name;
            RequirementRuleSetId = otherBase.RequirementRuleSetId;
            DetectionRuleSetId = otherBase.DetectionRuleSetId;
            RemovalType = otherBase.RemovalType;
            Removal = otherBase.Removal?.Clone();
            Context = otherBase.Context;
            SecureParams = otherBase.SecureParams;
            RestartBehavior = otherBase.RestartBehavior;
            RestartCountdown = otherBase.RestartCountdown;
            CustomExitCodes = otherBase.CustomExitCodes;
            LegacyLongFilePath = otherBase.LegacyLongFilePath;
            ExitCodes = otherBase.ExitCodes != null ? otherBase.ExitCodes.Select(e => e.Clone()).ToList() : null;
            InstallType = otherBase.InstallType;
            PowerShellScriptPath = otherBase.PowerShellScriptPath;
            PowerShellScriptArgs = otherBase.PowerShellScriptArgs;
            RollbackInstallType = otherBase.RollbackInstallType;
            RollbackPowerShellScriptPath = otherBase.RollbackPowerShellScriptPath;
            RollbackPowerShellScriptArgs = otherBase.RollbackPowerShellScriptArgs;
            RemovePowerShellScriptPath = otherBase.RemovePowerShellScriptPath;
            RemovePowerShellScriptArgs = otherBase.RemovePowerShellScriptArgs;
            InstallCommand = otherBase.InstallCommand != null ? otherBase.InstallCommand.Select(c => c.Clone()).ToList() : null;
            RollbackCommand = otherBase.RollbackCommand != null ? otherBase.RollbackCommand.Select(c => c.Clone()).ToList() : null;
            RollbackDetectionRuleSetId = otherBase.RollbackDetectionRuleSetId;
        }

        public override string? Validate()
        {
            string? baseValidation = base.Validate();
            if (baseValidation != null)
            {
                return baseValidation;
            }
            if (InstallType == OtherInstallType.PowerShell)
            {
                if (string.IsNullOrWhiteSpace(PowerShellScriptPath))
                {
                    return "A PowerShell script must be selected from the package files.";
                }
            }
            else
            {
                if (InstallCommand == null ||
                    InstallCommand.Count == 0 ||
                    InstallCommand.All(c => string.IsNullOrWhiteSpace(c?.GetValue())))
                {
                    return "Command must be specified for a package event.";
                }
            }
            if (RemovalType == OtherRemovalType.PowerShell && string.IsNullOrWhiteSpace(RemovePowerShellScriptPath))
            {
                return "A PowerShell script must be selected from the package files for the removal command.";
            }
            return null;
        }

        public string ParseVarPath(List<VariableText> varPath)
        {
            if (varPath == null)
            {
                throw new ArgumentNullException(nameof(varPath), "Cannot parse a null File Event path");
            }
            SpecialDIR? specialDIR = varPath.OfType<SpecialDIR>().FirstOrDefault();
            if (specialDIR != null)
            {
                string parsedSpecial = Environment.GetFolderPath((Environment.SpecialFolder)specialDIR.Value);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (parsedSpecial.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
                {
                    // If the special directory is under the user profile, we need to get paths for all users

                }
            }
            List<string> converted = varPath
                .Select(v => v.GetValue())
                .Where(val => val != null)
                .ToList()!;
            if (converted.Count > 1)
            {
                // Add the seprator if the user hasn't already
                for (int i = 0; i < converted.Count - 1; i++)
                {
                    if (!converted[i].EndsWith('\\') && !converted[i + 1].StartsWith('\\'))
                    {
                        converted[i] += '\\';
                    }
                }
                return string.Join("", converted);
            }
            return converted.First();
        }
    }
}
