using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class Environment : EventCoreWithPermanence
    {
        public string? Name { get; set; }
        public string? Value { get; set; }
        public Environmentbehaviour Behaviour { get; set; }
        public Separator Separator { get; set; }

        public override Environment Clone()
        {
            return new Environment
            {
                Id = Id,
                Name = Name,
                Value = Value,
                Behaviour = Behaviour,
                Separator = Separator,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                IsPermanent = IsPermanent,
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not Environment env) return;
            Id = env.Id;
            Name = env.Name;
            Value = env.Value;
            Behaviour = env.Behaviour;
            Separator = env.Separator;
            Schedules = env.Schedules.ConvertAll(s => s.Clone());
            IsPermanent = env.IsPermanent;
        }

        public override string? Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Name must be specified for Environment event.";
            }
            if (!Enum.IsDefined(typeof(Environmentbehaviour), Behaviour))
            {
                return "Behaviour must be specified for Environment event.";
            }
            if (!Enum.IsDefined(typeof(Separator), Separator))
            {
                return "Separator must be specified for Environment event.";
            }
            return null;
        }

        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already a Remove: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (Behaviour == Environmentbehaviour.Remove) return;
            base.Reverse();
            // Replace overwrote the whole variable, so Value held the entire content rather than one
            // delimited fragment. Clear it so the reversed Remove deletes the whole variable instead of
            // trying to strip Value out of it as a list item.
            if (Behaviour == Environmentbehaviour.Replace)
                Value = null;
            Behaviour = Environmentbehaviour.Remove;
        }
    }
}
