using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Package
{
    public enum OtherRemovalType
    {
        CMD,
        MSI,
        RegEx,
        PowerShell,
    }

    public enum OtherInstallType
    {
        CMD,
        PowerShell,
    }

    public partial class OtherRemovalBase : OtherCore
    {
        public override PackageBase Clone()
        {
            return new OtherRemovalBase
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
                ExitCodes = ExitCodes != null ? ExitCodes.Select(e => e.Clone()).ToList() : null,
                InstallType = InstallType,
                PowerShellScriptPath = PowerShellScriptPath,
                PowerShellScriptArgs = PowerShellScriptArgs,
                LegacyLongFilePath = LegacyLongFilePath
            };
        }
        public override void UpdateFrom(Stage stage)
        {
            if (stage is not OtherRemovalBase otherRemovalBase) return;
            Id = otherRemovalBase.Id;
            Name = otherRemovalBase.Name;
            RequirementRuleSetId = otherRemovalBase.RequirementRuleSetId;
            DetectionRuleSetId = otherRemovalBase.DetectionRuleSetId;
            RemovalType = otherRemovalBase.RemovalType;
            Removal = otherRemovalBase.Removal?.Clone();
            Context = otherRemovalBase.Context;
            SecureParams = otherRemovalBase.SecureParams;
            RestartBehavior = otherRemovalBase.RestartBehavior;
            RestartCountdown = otherRemovalBase.RestartCountdown;
            CustomExitCodes = otherRemovalBase.CustomExitCodes;
            ExitCodes = otherRemovalBase.ExitCodes != null ? otherRemovalBase.ExitCodes.Select(e => e.Clone()).ToList() : null;
            InstallType = otherRemovalBase.InstallType;
            PowerShellScriptPath = otherRemovalBase.PowerShellScriptPath;
            PowerShellScriptArgs = otherRemovalBase.PowerShellScriptArgs;
            LegacyLongFilePath = otherRemovalBase.LegacyLongFilePath;
        }
        public override string? Validate()
        {
            string? baseValidation = base.Validate();
            if (baseValidation != null)
            {
                return baseValidation;
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "A name is required.";
            }
            if (RemovalType == OtherRemovalType.PowerShell && string.IsNullOrWhiteSpace(PowerShellScriptPath))
            {
                return "A PowerShell script must be selected from the package files.";
            }
            return null;
        }
    }
}
