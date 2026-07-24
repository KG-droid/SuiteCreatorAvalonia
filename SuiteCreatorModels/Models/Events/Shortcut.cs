using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class Shortcut : EventCoreWithPermanence
    {
        public ShortcutAction? ShortAction { get; set; }
        public ShortcutType? ShortcutType { get; set; }
        public Uri? Target { get; set; }
        public string? Arguments { get; set; }
        public string? Name { get; set; }
        public List<ShortcutPlacement>? PlacementList { get; set; }
        public Uri? WorkingDIR { get; set; }
        public Contexts? Context { get; set; }
        public bool IsManualIcon { get; set; }
        public bool IsPathIcon { get; set; }
        public int IconIndex { get; set; }
        public string? IconPath { get; set; }
        public override Shortcut Clone()
        {
            return new Shortcut
            {
                Id = Id,
                Schedules = Schedules,
                ShortAction = ShortAction,
                ShortcutType = ShortcutType,
                Target = Target,
                Arguments = Arguments,
                Name = Name,
                PlacementList = PlacementList != null ? new(PlacementList) : null,
                WorkingDIR = WorkingDIR,
                Context = Context,
                IsManualIcon = IsManualIcon,
                IconIndex = IconIndex,
                IconPath = IconPath,
                IsPathIcon = IsPathIcon,
                IsPermanent = IsPermanent
            };
        }
        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not Shortcut shortcut) return;
            Id = shortcut.Id;
            Schedules = shortcut.Schedules;
            ShortAction = shortcut.ShortAction;
            ShortcutType = shortcut.ShortcutType;
            Target = shortcut.Target;
            Arguments = shortcut.Arguments;
            Name = shortcut.Name;
            PlacementList = shortcut.PlacementList != null ? new(shortcut.PlacementList) : null;
            WorkingDIR = shortcut.WorkingDIR;
            Context = shortcut.Context;
            IsManualIcon = shortcut.IsManualIcon;
            IconIndex = shortcut.IconIndex;
            IconPath = shortcut.IconPath;
            IsPathIcon = shortcut.IsPathIcon;
            IsPermanent = shortcut.IsPermanent;
        }
        public override string? Validate()
        {
            if (ShortAction == null)
            {
                return "ShortAction must be specified for Shortcut event.";
            }
            if (ShortcutType == null)
            {
                return "ShortcutType must be specified for Shortcut event.";
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "Name must be specified for Shortcut event.";
            }
            if (PlacementList == null || PlacementList.Count == 0)
            {
                return "At least one Placement must be specified for Shortcut event.";
            }
            if (Context == null)
            {
                return "Context must be specified for Shortcut event.";
            }
            return null;
        }

        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already a Delete: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (ShortAction == ShortcutAction.Delete) return;
            base.Reverse();
            ShortAction = ShortcutAction.Delete;
        }
    }
}
