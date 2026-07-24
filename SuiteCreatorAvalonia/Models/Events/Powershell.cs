using AvaloniaEdit.Document;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Tools;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class PowerShell : PowerShellBase
    {
        public ObservableCollection<FileSystemNode>? SupportingFiles { get; set; } = new();

        public override PowerShell Clone()
        {
            return new PowerShell
            {
                Id = Id,
                ScriptName = ScriptName,
                ScriptPath = ScriptPath,
                ScriptDoc = ScriptDoc != null ? new TextDocument(ScriptDoc?.Text) : null,
                SupportingFiles = SupportingFiles != null
                    ? new ObservableCollection<FileSystemNode>(
                        SupportingFiles.Select(f => f.Clone()))
                    : null,
                ScriptArgs = ScriptArgs,
                Context = Context,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not PowerShell powershell) return;
            Id = Id;
            ScriptName = powershell.ScriptName;
            ScriptPath = powershell.ScriptPath;
            if (ScriptDoc != null)
                ScriptDoc.Text = powershell.ScriptDoc != null ? powershell.ScriptDoc.Text : string.Empty;
            SupportingFiles = powershell.SupportingFiles != null
                ? new ObservableCollection<FileSystemNode>(
                    powershell.SupportingFiles.Select(f => f.Clone()))
                : null;
            ScriptArgs = powershell.ScriptArgs;
            Context = powershell.Context;
            Schedules = powershell.Schedules.ConvertAll(s => s.Clone());
        }

        public override string? Validate()
        {
            string? baseVal = base.Validate();
            if (!string.IsNullOrWhiteSpace(baseVal))
            {
                return baseVal;
            }
            if (SupportingFiles != null && !FileNodeChecker.AllTreeNodeFilesExist(SupportingFiles))
            {
                return "One or more files in the package do not exist.";
            }
            return null;
        }
    }
}
