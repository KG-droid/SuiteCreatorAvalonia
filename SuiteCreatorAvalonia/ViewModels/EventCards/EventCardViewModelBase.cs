using Avalonia.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;
using RuleSet = SuiteCreatorAvalonia.Models.Rules.RuleSet;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal abstract partial class EventCardViewModelBase : ViewModelBase
    {
        private bool _isLoading = false;

        [ObservableProperty]
        private EventCore _linkedEvent;

        [ObservableProperty]
        private List<Stage> _suiteStages;

        [ObservableProperty]
        private List<RuleSet> _suiteRules;

        [ObservableProperty]
        private ObservableCollection<Schedule> _schedules;

        [ObservableProperty]
        private bool _isSelected = false;

        [ObservableProperty]
        private bool _isHighlighted = false;

        public bool IsSelectable
        {
            get => !IsRemoveMode;
        }

        [ObservableProperty]
        private bool _isMarkedForDeletion = false;

        [ObservableProperty]
        private bool _isRemoveMode = false;

        [ObservableProperty]
        private bool _isCloneMode = false;

        [ObservableProperty]
        private bool _isMarkedForClone = false;

        [ObservableProperty]
        private Grid _cardInnerView = new Grid
        {
            Children = {
                new TextBlock { Text = "This is an example card, using a base VM. For a specific view use a specific card VM" }
            }
        };

        partial void OnIsRemoveModeChanged(bool value)
        {
            if (value)
                IsMarkedForDeletion = false;
        }

        partial void OnIsCloneModeChanged(bool value)
        {
            if (!value)
                IsMarkedForClone = false;
        }

        // Paramless constructor with example values for design view
        public EventCardViewModelBase() : this(
            new Environment(),
            new SuiteCoreManager())
        {
        }

        public EventCardViewModelBase(EventCore eventCore, SuiteCoreManager suiteCoreManager)
        {
            SuiteStages = suiteCoreManager.GetStages().ToList();
            // RuleSet.None is prepended so the Condition ComboBox always has a "no condition" entry
            // the user can explicitly pick, instead of clearing a condition being unreachable via the UI.
            SuiteRules = new List<RuleSet> { RuleSet.None }.Concat(suiteCoreManager.GetRuleSets()).ToList();
            Schedules = new();
            Schedules.CollectionChanged += (s, e) =>
            {
                if (_isLoading || LinkedEvent == null) return;
                if (e.NewItems != null)
                {
                    if (e.Action == NotifyCollectionChangedAction.Add)
                        foreach (Schedule schedule in e.NewItems)
                        {
                            LinkedEvent.Schedules.Add(schedule);
                        }
                }
                if (e.OldItems != null)
                {
                    if (e.Action == NotifyCollectionChangedAction.Remove)
                        foreach (Schedule schedule in e.OldItems)
                        {
                            LinkedEvent.Schedules.Remove(schedule);
                        }
                }
                SaveEvent();
            };
            PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null &&
                    !e.PropertyName.Equals("IsSelected", StringComparison.OrdinalIgnoreCase) &&
                    !e.PropertyName.Equals("IsHighlighted", StringComparison.OrdinalIgnoreCase))
                {
                    SaveEvent();
                }
            };
            if (Design.IsDesignMode)
            {
                IsSelected = true;
            }
        }

        [RelayCommand]
        public void AddSchedule()
        {
            Schedule schedule = new Schedule
            {
                EventStage = SuiteStages.First(),
                StageSequence = Sequence.DuringInstallAfterStage
            };
            Schedules.Add(schedule);
        }

        public abstract void LoadEvent(EventCore eventCore);

        public abstract void SaveEvent();

        /// <summary>
        /// Re-points each schedule's EventStage/Condition at the live SuiteStages/SuiteRules instances (by Id) so
        /// the Stage/Condition ComboBoxes bind correctly. Falls back to a safe default if the original stage or
        /// rule no longer exists (e.g. it was a package that has since been deleted), rather than crashing.
        /// </summary>
        protected void NormalizeSchedules()
        {
            foreach (Schedule sch in Schedules)
            {
                if (sch.EventStage != null)
                {
                    sch.EventStage = SuiteStages.FirstOrDefault(s => s.Id == sch.EventStage.Id)
                        ?? SuiteStages.FirstOrDefault(s => s.Id == Stage.EndStageId)
                        ?? SuiteStages.FirstOrDefault();
                }
                if (sch.Condition != null)
                {
                    sch.Condition = SuiteRules.FirstOrDefault(s => s.Id == sch.Condition.Id);
                }
            }
        }

        public async void HoldCardOpenAfterComboChange(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                IOEventView? ioParent = comboBox.FindAncestorOfType<IOEventView>();
                if (ioParent != null)
                {
                    IOEventViewModel? iOEventViewModel = ioParent.DataContext as IOEventViewModel;
                    if (iOEventViewModel != null)
                    {
                        iOEventViewModel.IsEventsSelectable = false;
                        await Task.Delay(1000);
                        iOEventViewModel.IsEventsSelectable = true;
                    }
                }
            }
        }

        /// <summary>Briefly pulses a highlight ring around the card, e.g. after navigating here from elsewhere — doesn't affect selection/hover state.</summary>
        public async void TriggerHighlightPulse()
        {
            IsHighlighted = true;
            await Task.Delay(3000);
            IsHighlighted = false;
        }

        [RelayCommand]
        public void MarkForDeletion()
        {
            IsMarkedForDeletion = !IsMarkedForDeletion;
        }

        [RelayCommand]
        public void MarkForClone()
        {
            IsMarkedForClone = !IsMarkedForClone;
        }
    }
}
