using SuiteCreatorAvalonia.Enums;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class ProcessClosure : ClosureCore
    {
        public ProcAction? Action { get; set; }

        public override ProcessClosure Clone()
        {
            return new ProcessClosure
            {
                Id = Id,
                Name = Name,
                Action = Action,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not ProcessClosure procClose) return;
            Id = procClose.Id;
            Name = procClose.Name;
            Action = procClose.Action;
            Schedules = procClose.Schedules.ConvertAll(s => s.Clone());
        }

        public override string? Validate()
        {
            if (!GetNames().Any())
            {
                return "At least one process name must be specified for ProcessClosure event.";
            }
            if (Action == null)
            {
                return "Action must be specified for ProcessClosure event.";
            }
            return null;
        }
    }
}
