using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Events
{
    public abstract class EventCoreWithPermanence : EventCore
    {
        public bool IsPermanent { get; set; }

        public virtual void Reverse() 
        {
            if (IsPermanent) return;
            if (Schedules != null && Schedules.Count > 0)
            {
                foreach (Schedule schedule in Schedules)
                {
                    switch (schedule.StageSequence)
                    {
                        case Sequence.DuringInstallAfterStage:
                            schedule.StageSequence = Sequence.DuringRemoveBeforeStage;
                            break;
                        case Sequence.DuringInstallBeforeStage:
                            schedule.StageSequence = Sequence.DuringRemoveAfterStage;
                            break;
                        default:
                            break;
                    }
                }
            }
        }
    }
}
