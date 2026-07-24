using AvaloniaEdit.Document;
using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Events
{
    public partial class PowerShellBase : EventCore
    {
        public string? ScriptName { get; set; }
        public string? ScriptPath { get; set; }
        public TextDocument? ScriptDoc { get; set; }
        public string? ScriptArgs { get; set; }
        public Contexts? Context { get; set; }

        // Populated at runtime by SuiteExecutor path resolution when the deployed script package
        // contains a "Support" subfolder — the script is launched with this as its working directory.
        public string? SupportFilesDir { get; set; }
        public override PowerShellBase Clone()
        {
            return new PowerShellBase
            {
                Id = Id,
                ScriptName = ScriptName,
                ScriptPath = ScriptPath,
                ScriptDoc = ScriptDoc != null ? new TextDocument(ScriptDoc?.Text) : null,
                ScriptArgs = ScriptArgs,
                Context = Context,
                SupportFilesDir = SupportFilesDir,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
            };
        }
        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not PowerShellBase powershell) return;
            Id = Id;
            ScriptName = powershell.ScriptName;
            ScriptPath = powershell.ScriptPath;
            if (ScriptDoc != null)
                ScriptDoc.Text = powershell.ScriptDoc != null ? powershell.ScriptDoc.Text : string.Empty;
            ScriptArgs = powershell.ScriptArgs;
            Context = powershell.Context;
            SupportFilesDir = powershell.SupportFilesDir;
            Schedules = powershell.Schedules.ConvertAll(s => s.Clone());
        }
        public override string? Validate()
        {
            if (string.IsNullOrWhiteSpace(ScriptName))
            {
                return "Script name cannot be empty.";
            }
            if (string.IsNullOrWhiteSpace(ScriptPath) && string.IsNullOrWhiteSpace(ScriptDoc?.Text))
            {
                return "No Script path and document provided";
            }
            if (Context == null || !System.Enum.IsDefined(typeof(Contexts), Context))
            {
                return "A Context must be defined";
            }
            return null;
        }
    }
}
