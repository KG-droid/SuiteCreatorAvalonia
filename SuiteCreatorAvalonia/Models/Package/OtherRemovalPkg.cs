using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Tools;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Package
{
    public class OtherRemovalPkg : OtherRemovalBase
    {
        public IEnumerable<FileSystemNode>? Files { get; set; }

        public override OtherRemovalPkg Clone()
        {
            return new OtherRemovalPkg
            {
                Id = Id,
                Name = Name,
                RequirementRuleSetId = RequirementRuleSetId,
                DetectionRuleSetId = DetectionRuleSetId,
                Files = Files != null ? new List<FileSystemNode>(Files.Select(f => f.Clone())) : null,
                RemovalType = RemovalType,
                Removal = Removal?.Clone(),
                Context = Context,
                SecureParams = SecureParams,
                RestartBehavior = RestartBehavior,
                CustomExitCodes = CustomExitCodes,
                ExitCodes = ExitCodes != null ? new List<ReturnCode>(ExitCodes) : null,
                InstallType = InstallType,
                PowerShellScriptPath = PowerShellScriptPath,
                PowerShellScriptArgs = PowerShellScriptArgs,
                LegacyLongFilePath = LegacyLongFilePath,
            };
        }

        public override void UpdateFrom(Stage stage)
        {
            if (stage == null) return;
            if (stage is OtherRemovalPkg other)
            {
                Id = other.Id;
                Name = other.Name;
                RequirementRuleSetId = other.RequirementRuleSetId;
                DetectionRuleSetId = other.DetectionRuleSetId;
                Files = other.Files != null ? new List<FileSystemNode>(other.Files.Select(f => f.Clone())) : null;
                RemovalType = other.RemovalType;
                Removal = other.Removal?.Clone();
                Context = other.Context;
                SecureParams = other.SecureParams;
                RestartBehavior = other.RestartBehavior;
                CustomExitCodes = other.CustomExitCodes;
                ExitCodes = other.ExitCodes != null ? new List<ReturnCode>(other.ExitCodes) : null;
                InstallType = other.InstallType;
                PowerShellScriptPath = other.PowerShellScriptPath;
                PowerShellScriptArgs = other.PowerShellScriptArgs;
                LegacyLongFilePath = other.LegacyLongFilePath;
            }
        }

        public override string? Validate()
        {
            string? baseVal = base.Validate();
            if (!string.IsNullOrWhiteSpace(baseVal))
            {
                return baseVal;
            }
            if (Files != null && Files.First().SubNodes?.Count > 0 && !FileNodeChecker.AllTreeNodeFilesExist(Files.First().SubNodes))
            {
                return "One or more files in the package do not exist.";
            }
            return null;
        }
    }
}
