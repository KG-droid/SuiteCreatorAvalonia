using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class Registry : EventCoreWithPermanence
    {
        public RegAction? Action { get; set; }
        public string? KeyPath { get; set; }
        public Uri? RegFilePath { get; set; }
        public RegistryPropertyType? PropertyType { get; set; }
        public string? PropertyName { get; set; }
        public string? PropertyValue { get; set; }
        public bool Overwrite { get; set; }

        public override Registry Clone()
        {
            return new Registry
            {
                Id = Id,
                Action = Action,
                KeyPath = KeyPath,
                RegFilePath = RegFilePath != null ? new Uri(RegFilePath.ToString()) : null,
                PropertyType = PropertyType,
                PropertyName = PropertyName,
                PropertyValue = PropertyValue,
                Overwrite = Overwrite,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                IsPermanent = IsPermanent
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not Registry reg) return;
            Id = reg.Id;
            Action = reg.Action;
            KeyPath = reg.KeyPath;
            RegFilePath = reg.RegFilePath != null ? new Uri(reg.RegFilePath.ToString()) : null;
            PropertyType = reg.PropertyType;
            PropertyName = reg.PropertyName;
            PropertyValue = reg.PropertyValue;
            Overwrite = reg.Overwrite;
            Schedules = reg.Schedules.ConvertAll(s => s.Clone());
            IsPermanent = reg.IsPermanent;
        }

        public override string? Validate()
        {
            if (Action == null)
            {
                return "Action must be specified for Registry event.";
            }
            if (Action == RegAction.Import)
            {
                if (RegFilePath == null)
                {
                    return "RegFilePath must be specified for Registry Import event.";
                }
                if (!Path.Exists(RegFilePath.LocalPath))
                {
                    return $"Reg file path: {RegFilePath.LocalPath}, does not exist";
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(KeyPath))
                {
                    return "KeyPath must be specified for Registry event.";
                }
                if (Action == RegAction.AddAmend)
                {
                    if (PropertyType == null)
                    {
                        return "PropertyType must be specified for Registry event.";
                    }
                    if (string.IsNullOrWhiteSpace(PropertyName))
                    {
                        return "PropertyName must be specified for Registry event.";
                    }
                }
            }
            return null;
        }

        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already a Remove: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (Action == RegAction.Remove) return;
            base.Reverse();
            Action = RegAction.Remove;
        }
    }
}
