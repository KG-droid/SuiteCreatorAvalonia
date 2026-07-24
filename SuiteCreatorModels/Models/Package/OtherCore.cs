using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Package
{
    public abstract class OtherCore : PackageBase
    {
        public Guid? DetectionRuleSetId { get; set; }
        public Guid? RollbackDetectionRuleSetId { get; set; }
        public OtherRemovalType RemovalType { get; set; }
        public OtherRemovalObject? Removal { get; set; }
        public Contexts Context { get; set; } = Contexts.System;
        public bool SecureParams { get; set; } = false;
        public bool LegacyLongFilePath { get; set; } = false;
        public RestartBehavior RestartBehavior { get; set; }
        public int RestartCountdown { get; set; } = 60;
        public bool CustomExitCodes { get; set; } = false;
        public IEnumerable<ReturnCode>? ExitCodes { get; set; }
        public OtherInstallType InstallType { get; set; } = OtherInstallType.CMD;
        public string? PowerShellScriptPath { get; set; }
        public string? PowerShellScriptArgs { get; set; }
        public OtherInstallType RollbackInstallType { get; set; } = OtherInstallType.CMD;
        public string? RollbackPowerShellScriptPath { get; set; }
        public string? RollbackPowerShellScriptArgs { get; set; }
        public string? RemovePowerShellScriptPath { get; set; }
        public string? RemovePowerShellScriptArgs { get; set; }
        public override string? Validate()
        {
            string? baseValidation = base.Validate();
            if (baseValidation != null)
            {
                return baseValidation;
            }
            if (!Enum.IsDefined(typeof(OtherRemovalType), RemovalType))
            {
                return "Removal Type must be specified.";
            }
            if (!Enum.IsDefined(typeof(Contexts), Context))
            {
                return "Context must be specified.";
            }
            if (!Enum.IsDefined(typeof(RestartBehavior), RestartBehavior))
            {
                return "Restart Behavior must be specified.";
            }
            if (RemovalType != OtherRemovalType.RegEx && (!DetectionRuleSetId.HasValue || DetectionRuleSetId.Value == Guid.Empty))
            {
                return $"Package '{Name}' must have a detection rule set configured.";
            }
            if (ExitCodes != null && ExitCodes.Count() > 0)
            {
                foreach (ReturnCode returnCode in ExitCodes)
                {
                    if (returnCode.Code == default)
                    {
                        return "Exit Code must be specified on all custom return codes";
                    }
                }
            }
            return null;
        }
    }

    public abstract class OtherRemovalObject
    {
        public abstract OtherRemovalObject Clone();
        public abstract void UpdateFrom(OtherRemovalObject otherRemObject);
    }

    public class OtherCMDRemoval : OtherRemovalObject
    {
        public List<List<VariableText>>? RemoveCommands { get; set; }
        public override OtherCMDRemoval Clone()
        {
            return new OtherCMDRemoval
            {
                RemoveCommands = RemoveCommands != null
                    ? RemoveCommands.Select(cmdList => cmdList.Select(cmd => cmd.Clone()).ToList()).ToList()
                    : null
            };
        }
        public override void UpdateFrom(OtherRemovalObject otherRemObject)
        {
            if (otherRemObject == null) return;
            if (otherRemObject is OtherCMDRemoval otherCMDRemoval)
                RemoveCommands = otherCMDRemoval.RemoveCommands != null
                    ? otherCMDRemoval.RemoveCommands.Select(cmdList => cmdList.Select(cmd => cmd.Clone()).ToList()).ToList()
                    : null;
        }
    }

    public class OtherRegexRemoval : OtherRemovalObject
    {
        public string? Manufacturer { get; set; }
        public string? ProductName { get; set; }
        public string? RemovalParams { get; set; }
        public override OtherRegexRemoval Clone()
        {
            return new OtherRegexRemoval
            {
                Manufacturer = Manufacturer,
                ProductName = ProductName,
                RemovalParams = RemovalParams
            };
        }

        public override void UpdateFrom(OtherRemovalObject otherRemObject)
        {
            if (otherRemObject == null) return;
            if (otherRemObject is OtherRegexRemoval otherCMDRemoval)
            {
                Manufacturer = otherCMDRemoval.Manufacturer;
                ProductName = otherCMDRemoval.ProductName;
                RemovalParams = otherCMDRemoval.RemovalParams;
            }
        }
    }

    public class OtherMSIRemoval : OtherRemovalObject
    {
        public List<MSIInstallRemovalItem>? MSIRemovalItems { get; set; }

        public override OtherMSIRemoval Clone()
        {
            return new OtherMSIRemoval
            {
                MSIRemovalItems = MSIRemovalItems != null
                    ? MSIRemovalItems.Select(item => item.Clone()).ToList()
                    : null
            };
        }

        public override void UpdateFrom(OtherRemovalObject otherRemObject)
        {
            if (otherRemObject == null) return;
            if (otherRemObject is OtherMSIRemoval otherMSIRemoval)
                MSIRemovalItems = otherMSIRemoval.MSIRemovalItems != null
                    ? otherMSIRemoval.MSIRemovalItems.Select(item => item.Clone()).ToList()
                    : null;
        }
    }
}
