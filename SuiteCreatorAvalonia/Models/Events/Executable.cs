using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Tools;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Events
{
    public partial class Executable : ExecutableBase
    {
        public List<FileSystemNode>? TreeNodes { get; set; }

        public override Executable Clone()
        {
            return new Executable
            {
                Id = Id,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                Command = Command?.Select(c => c.Clone()).ToList(),
                WorkingDIR = WorkingDIR?.Select(w => w.Clone()).ToList(),
                TreeNodes = TreeNodes?.Select(t => t.Clone()).ToList(),
                ContinueOnError = ContinueOnError,
                ContinueOnNotFound = ContinueOnNotFound,
                SecureParams = SecureParams
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not Executable exec) return;
            Id = exec.Id;
            Schedules = exec.Schedules.ConvertAll(s => s.Clone());
            Command = exec.Command?.Select(c => c.Clone()).ToList();
            WorkingDIR = exec.WorkingDIR?.Select(w => w.Clone()).ToList();
            TreeNodes = exec.TreeNodes?.Select(t => t.Clone()).ToList();
            ContinueOnError = exec.ContinueOnError;
            ContinueOnNotFound = exec.ContinueOnNotFound;
            SecureParams = exec.SecureParams;
        }

        public override string? Validate()
        {
            string? baseValResults = base.Validate();
            if (string.IsNullOrWhiteSpace(baseValResults))
            {
                return baseValResults;
            }
            if (TreeNodes != null)
            {
                if (!FileNodeChecker.AllTreeNodeFilesExist(TreeNodes))
                {
                    return "One or more of the attached files cannot be found.";
                }
            }
            return null;
        }
    }
}
