using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Events
{
    public abstract class EventCore
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public List<Schedule> Schedules { get; set; } = new();

        public abstract EventCore Clone();

        public abstract void UpdateFrom(EventCore source);

        public abstract string? Validate();
    }
}