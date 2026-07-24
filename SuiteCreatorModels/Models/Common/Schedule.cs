using Newtonsoft.Json;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Rules;
using System.ComponentModel;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class Schedule : INotifyPropertyChanged
    {
        // Start has nothing before it, End has nothing after it, so only the applicable half of Sequence makes sense for those boundary stages.
        private static readonly Sequence[] BeforeAndAfterSequences =
        {
            Sequence.DuringInstallBeforeStage,
            Sequence.DuringInstallAfterStage,
            Sequence.DuringRemoveBeforeStage,
            Sequence.DuringRemoveAfterStage,
            Sequence.AlwaysBeforeStage,
            Sequence.AlwaysAfterStage,
        };

        private static readonly Sequence[] AfterOnlySequences =
        {
            Sequence.DuringInstallAfterStage,
            Sequence.DuringRemoveAfterStage,
            Sequence.AlwaysAfterStage,
        };

        private static readonly Sequence[] BeforeOnlySequences =
        {
            Sequence.DuringInstallBeforeStage,
            Sequence.DuringRemoveBeforeStage,
            Sequence.AlwaysBeforeStage,
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private Stage? _eventStage;

        [JsonIgnore]
        public Stage? EventStage
        {
            get
            {
                return _eventStage;
            }
            set
            {
                if (value == null) return; // An event always requires a Stage

                bool isSameStage = _eventStage?.Id == value.Id;

                _eventStage = value;
                EventStageId = value.Id;

                if (isSameStage) return;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EventStage)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvailableSequences)));

                if (!AvailableSequences.Contains(StageSequence))
                {
                    StageSequence = AvailableSequences[0];
                }
            }
        }

        public Guid? EventStageId { get; set; }

        private Sequence _stageSequence;
        public Sequence StageSequence
        {
            get => _stageSequence;
            set
            {
                if (_stageSequence == value) return;
                _stageSequence = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StageSequence)));
            }
        }

        [JsonIgnore]
        public IReadOnlyList<Sequence> AvailableSequences =>
            EventStage == null ? BeforeAndAfterSequences :
            EventStage.IsStart ? AfterOnlySequences :
            EventStage.IsEnd ? BeforeOnlySequences :
            BeforeAndAfterSequences;

        private RuleSet? _condition;
        public RuleSet? Condition
        {
            get => _condition;
            set
            {
                if (_condition == value) return;
                _condition = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Condition)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConditionDisplay)));
            }
        }

        // What the Condition ComboBox actually binds to: shows the RuleSet.None sentinel instead of
        // a blank row when there's no condition, so "no condition" is a real, selectable, visible state.
        [JsonIgnore]
        public RuleSet ConditionDisplay => Condition ?? RuleSet.None;

        public Schedule Clone()
        {
            return new Schedule
            {
                EventStage = EventStage,
                EventStageId = EventStageId,
                StageSequence = StageSequence,
                Condition = Condition?.Clone(),
            };
        }

        public void UpdateFrom(Schedule other)
        {
            if (other == null) return;
            EventStage = other.EventStage;
            StageSequence = other.StageSequence;
            Condition = other.Condition?.Clone();
        }
    }
}
