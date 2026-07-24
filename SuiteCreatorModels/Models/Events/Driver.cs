using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class Driver : EventCoreWithPermanence
    {
        public required DriverAction Action { get; set; }
        public string? InfPath { get; set; }

        // Windows renames INFs to oemNN.inf in the driver store, so removal identifies the
        // driver by its original INF file name (e.g. mydriver.inf) via pnputil /enum-drivers.
        public string? InfName { get; set; }

        public override Driver Clone()
        {
            return new Driver
            {
                Id = Id,
                Action = Action,
                InfPath = InfPath,
                InfName = InfName,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                IsPermanent = IsPermanent,
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not Driver driver) return;
            Id = driver.Id;
            Action = driver.Action;
            InfPath = driver.InfPath;
            InfName = driver.InfName;
            Schedules = driver.Schedules.ConvertAll(s => s.Clone());
            IsPermanent = driver.IsPermanent;
        }

        public override string? Validate()
        {
            if (!System.Enum.IsDefined(typeof(DriverAction), Action))
            {
                return "Action must be specified for Driver event.";
            }
            if (Action == DriverAction.Install)
            {
                if (string.IsNullOrWhiteSpace(InfPath))
                {
                    return "An .inf file path must be specified for Driver Install event.";
                }
                if (!Path.GetExtension(InfPath).Equals(".inf", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Driver file path: {InfPath}, must point to an .inf file";
                }
                if (!Path.Exists(InfPath))
                {
                    return $"Driver file path: {InfPath}, does not exist";
                }
            }
            if (Action == DriverAction.Remove && string.IsNullOrWhiteSpace(InfName))
            {
                return "The original .inf file name must be specified for Driver Remove event.";
            }
            return null;
        }

        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already a Remove: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (Action == DriverAction.Remove) return;
            base.Reverse();
            if (string.IsNullOrWhiteSpace(InfName) && !string.IsNullOrWhiteSpace(InfPath))
            {
                InfName = Path.GetFileName(InfPath);
            }
            Action = DriverAction.Remove;
        }
    }
}
