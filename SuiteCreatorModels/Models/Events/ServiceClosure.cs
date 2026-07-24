using SuiteCreatorAvalonia.Enums;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class ServiceClosure : ClosureCore
    {
        public ClosureType Type { get; set; }

        public override ServiceClosure Clone()
        {
            return new ServiceClosure
            {
                Id = Id,
                Name = Name,
                Type = Type,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not ServiceClosure serviceClose) return;
            Id = serviceClose.Id;
            Name = serviceClose.Name;
            Type = serviceClose.Type;
            Schedules = serviceClose.Schedules.ConvertAll(s => s.Clone());
        }

        public override string? Validate()
        {
            if (!GetNames().Any())
            {
                return "At least one service name must be specified for ServiceClosure event.";
            }
            if (!System.Enum.IsDefined(typeof(ClosureType), Type))
            {
                return "Type must be specified for ServiceClosure event.";
            }
            return null;
        }
    }
}
