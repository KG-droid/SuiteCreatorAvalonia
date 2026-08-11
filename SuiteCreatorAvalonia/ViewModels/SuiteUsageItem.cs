using Avalonia.Media;
using Material.Icons;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using System;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    /// <summary>A single row in a "where is this used" popup — wraps either a Package or an Event that references some other item (a RuleSet, or a Package used as a schedule's stage point).</summary>
    public class SuiteUsageItem
    {
        public PackageBase? Package { get; private init; }
        public EventCore? Event { get; private init; }
        public required string Title { get; init; }
        public string? SubTitle { get; init; }
        public required MaterialIconKind IconKind { get; init; }
        public required IBrush IconColor { get; init; }

        public static SuiteUsageItem ForPackage(PackageBase pkg) => new SuiteUsageItem
        {
            Package = pkg,
            Title = pkg.Name ?? "(Unnamed Package)",
            IconKind = EventTabMappings.Packages.IconKind,
            IconColor = EventTabMappings.Packages.IconColor,
        };

        /// <summary>scheduleMatch identifies which of the event's schedules is the one doing the referencing, so its sequence can be shown as a subtitle.</summary>
        public static SuiteUsageItem ForEvent(EventCore evt, Func<Schedule, bool> scheduleMatch)
        {
            EventTabMappings.EventTab? tab = EventTabMappings.GetEventTabByModelType(evt.GetType());
            Schedule? matchedSchedule = evt.Schedules.FirstOrDefault(scheduleMatch);
            return new SuiteUsageItem
            {
                Event = evt,
                Title = tab?.Header ?? evt.GetType().Name,
                SubTitle = matchedSchedule != null ? matchedSchedule.StageSequence.ToString() : null,
                IconKind = tab?.IconKind ?? MaterialIconKind.CalendarClock,
                IconColor = tab?.IconColor ?? Brushes.Gray,
            };
        }

        public static SuiteUsageItem ForEvent(EventCore evt, Guid ruleSetId) =>
            ForEvent(evt, s => s.Condition != null && s.Condition.Id == ruleSetId);
    }
}
