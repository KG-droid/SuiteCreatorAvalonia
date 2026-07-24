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
    internal partial class ServiceClosureCardViewModel : EventCardViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private string? _nameOfClosure;

        [ObservableProperty]
        private ClosureType _typeOfClosure;

        public ServiceClosureCardViewModel() : this(
            new ServiceClosure(),
            new SuiteCoreManager())
        {
        }

        public ServiceClosureCardViewModel(ServiceClosure servEvent, SuiteCoreManager suiteCoreManager) : base(servEvent, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (servEvent != null)
                LoadEvent(servEvent);
            CreateRegCardView();
        }

        private void CreateRegCardView()
        {
            // Clear any existing fields for this card
            Grid mainGrid = new Grid();

            mainGrid.Children.Clear();

            // Service Name(s)
            TextBox serviceNameTextBox = new TextBox();
            serviceNameTextBox.Bind(TextBox.TextProperty, new Binding("NameOfClosure"));
            serviceNameTextBox.PlaceholderText = "The name of a service (separate multiple with ; or ,)";
            Help.Annotate(serviceNameTextBox, "Service name(s)", "The Windows service name(s) to close before the suite continues. Separate multiple names with ; or , to close them all together — no need for a separate card unless they need different settings.");
            Grid.SetColumn(serviceNameTextBox, 0);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            mainGrid.Children.Add(serviceNameTextBox);

            // Closure Type
            ComboBox closureCombo = new ComboBox();
            closureCombo.ItemsSource = Enum.GetValues(typeof(ClosureType));
            closureCombo.Bind(ComboBox.SelectedValueProperty, new Binding("TypeOfClosure"));
            closureCombo.Background = Avalonia.Media.Brushes.Transparent;
            closureCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            Help.Annotate(closureCombo, "Closure type", "How the service is closed — e.g. stopped and blocked from restarting while the suite runs, or just stopped.");
            closureCombo.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(closureCombo, 2);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            mainGrid.Children.Add(closureCombo);
            CardInnerView = mainGrid;
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is ServiceClosure closure)
            {
                _isLoading = true;
                NameOfClosure = closure.Name;
                TypeOfClosure = closure.Type;
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
            if (LinkedEvent is ServiceClosure servClose)
            {
                servClose.Name = NameOfClosure;
                servClose.Type = TypeOfClosure;
                servClose.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateServiceClosureEvent(servClose);
            }
        }
    }
}
