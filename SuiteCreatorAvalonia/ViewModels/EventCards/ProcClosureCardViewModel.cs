using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class ProcClosureCardViewModel : EventCardViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private string? _nameOfClosure;

        [ObservableProperty]
        private ProcAction? _typeOfClosure = ProcAction.StopAndBlock;

        public ProcClosureCardViewModel() : this(
            new ProcessClosure(),
            new SuiteCoreManager())
        {
        }

        public ProcClosureCardViewModel(ProcessClosure procEvent, SuiteCoreManager suiteCoreManager) : base(procEvent, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (procEvent != null)
                LoadEvent(procEvent);
            CreateProcClosureCardView();
        }

        private void CreateProcClosureCardView()
        {
            // Clear any existing fields for this card
            Grid mainGrid = new Grid();

            // Process Name(s)
            TextBox serviceNameTextBox = new TextBox();
            serviceNameTextBox.Bind(TextBox.TextProperty, new Binding("NameOfClosure"));
            serviceNameTextBox.PlaceholderText = "e.g WinWord.exe (separate multiple with ; or ,)";
            Help.Annotate(serviceNameTextBox, "Process name(s)", "The executable name(s) of the process(es) to close before the suite continues, e.g. WinWord.exe. Separate multiple names with ; or , to close them all together — no need for a separate card unless they need different settings.");
            Grid.SetColumn(serviceNameTextBox, 0);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            mainGrid.Children.Add(serviceNameTextBox);

            // Closure Type
            ComboBox closureCombo = new ComboBox();
            closureCombo.ItemsSource = Enum.GetValues(typeof(ProcAction));
            closureCombo.Bind(ComboBox.SelectedValueProperty, new Binding("TypeOfClosure"));
            closureCombo.Background = Avalonia.Media.Brushes.Transparent;
            closureCombo.Margin = new Thickness(0, 0, 10, 0);
            closureCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            Help.Annotate(closureCombo, "Closure type", "How the process is closed — e.g. stopped and blocked from restarting while the suite runs, or just stopped.");
            Grid.SetColumn(closureCombo, 1);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            mainGrid.Children.Add(closureCombo);
            CardInnerView = mainGrid;
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is ProcessClosure closure)
            {
                _isLoading = true;
                NameOfClosure = closure.Name;
                TypeOfClosure = closure.Action;
                Schedules.Clear();
                Schedules.AddRange(closure.Schedules);
                // Ensure that the EventStage and Condition in each Schedule is the same instance as in SuiteStages/SuiteConditions for the ComboBox binding to work correctly.
                foreach (Schedule sch in Schedules)
                {
                    if (sch.EventStage != null)
                    {
                        sch.EventStage = SuiteStages.First(s => s.Id == sch.EventStage.Id);
                    }
                    if (sch.Condition != null)
                    {
                        sch.Condition = SuiteRules.First(s => s.Id == sch.Condition.Id);
                    }
                }
                LinkedEvent = closure;
                _isLoading = false;
            }
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is ProcessClosure procClose)
            {
                procClose.Action = TypeOfClosure;
                procClose.Name = NameOfClosure;
                procClose.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateProcessClosureEvent(procClose);
            }
        }
    }
}
