using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Tools;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Package
{
    public partial class OtherPkg : OtherBase
    {
        public IEnumerable<FileSystemNode>? Files { get; set; }

        public override OtherPkg Clone()
        {
            OtherPkg clone = new();
            clone.UpdateFrom(this);
            clone.Files = Files != null ? new List<FileSystemNode>(Files.Select(f => f.Clone())) : null;
            return clone;
        }

        public override void UpdateFrom(Stage stage)
        {
            if (stage == null) return;
            if (stage is OtherPkg other)
            {
                base.UpdateFrom(stage);
                Files = other.Files != null ? new List<FileSystemNode>(other.Files.Select(f => f.Clone())) : null;
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
