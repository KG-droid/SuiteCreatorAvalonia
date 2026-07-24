using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class DriverCardViewModel : EventCardWithPermanenceViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private DriverAction _action = DriverAction.Install;

        [ObservableProperty]
        private string? _infPath;

        [ObservableProperty]
        private string? _infName;

        partial void OnActionChanged(DriverAction value)
        {
            CreateDriverCardView();
        }

        public DriverCardViewModel() : this(
            new Driver { Action = DriverAction.Install },
            new SuiteCoreManager())
        {
        }

        public DriverCardViewModel(Driver driver, SuiteCoreManager suiteCoreManager) : base(driver, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (driver != null)
                LoadEvent(driver);
            else
                CreateDriverCardView();
        }

        private void CreateDriverCardView()
        {
            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));

            // Action
            ComboBox operationCombo = new ComboBox();
            operationCombo.ItemsSource = Enum.GetValues(typeof(DriverAction));
            operationCombo.Bind(ComboBox.SelectedValueProperty, new Binding("Action"));
            operationCombo.Background = Avalonia.Media.Brushes.Transparent;
            operationCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            operationCombo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(operationCombo, "Action", "Whether to Install a driver from an .inf file, or Remove one from the driver store by its original .inf file name.");
            Grid.SetColumn(operationCombo, 0);
            Grid.SetRow(operationCombo, 0);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            mainGrid.Children.Add(operationCombo);

            if (Action == DriverAction.Install)
            {
                // Inf File Path
                TextBox textBox = new TextBox();
                textBox.Bind(TextBox.TextProperty, new Binding("InfPath"));
                textBox.PlaceholderText = "File Path to the driver .inf file. The whole folder containing this .inf will be bundled (e.g. .sys, .cat, coinstaller files)";
                textBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                ToolTip.SetTip(textBox, "The whole folder this file is in will be bundled into the Suite, so any adjacent .sys/.cat/coinstaller files the vendor ships are included automatically.");
                Help.Annotate(textBox, "Driver .inf file", "Path to the driver's .inf file. The whole folder it sits in (including .sys/.cat files) is bundled into the Suite.");
                Grid.SetColumn(textBox, 1);
                Grid.SetRow(textBox, 0);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                mainGrid.Children.Add(textBox);

                Button browseButton = new Button();
                browseButton.Classes.Add("IconButton");
                browseButton.Tag = "Chip";
                browseButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
                browseButton.Command = new RelayCommand(DriverBrowse);
                ToolTip.SetTip(browseButton, "Browse for a driver .inf file");
                Help.Annotate(browseButton, "Browse", "Browse for a driver .inf file.");
                Grid.SetColumn(browseButton, 2);
                Grid.SetRow(browseButton, 0);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(browseButton);

                // Toggle Permanent
                ToggleButton permanentToggle = new();
                permanentToggle.Classes.Add("IconToggleButton");
                permanentToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("IsPermanent"));
                permanentToggle.Tag = "DiamondStone";
                ToolTip.SetTip(permanentToggle, "If enabled, the Driver won't be removed on Suite removal");
                Help.Annotate(permanentToggle, "Permanent", "If enabled, this driver won't be removed on Suite removal.");
                Grid.SetColumn(permanentToggle, 3);
                Grid.SetRow(permanentToggle, 0);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(permanentToggle);
            }
            else
            {
                // Original inf name
                TextBox infNameTextBox = new TextBox();
                infNameTextBox.Bind(TextBox.TextProperty, new Binding("InfName"));
                infNameTextBox.PlaceholderText = "Original .inf file name of the driver to remove, e.g. mydriver.inf";
                infNameTextBox.MinWidth = 400;
                infNameTextBox.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                Help.Annotate(infNameTextBox, "Original .inf name", "Windows renames driver INFs to oemNN.inf in the driver store, so the driver to remove is identified by its original .inf file name (shown by 'pnputil /enum-drivers' as Original Name).");
                Grid.SetColumn(infNameTextBox, 1);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(infNameTextBox);
            }
            CardInnerView = mainGrid;
        }

        private async void DriverBrowse()
        {
            IEnumerable<string>? result = (await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for a driver .inf file", FileTypeFilter = SysIOPickerTypes.Inf }));
            if (result != null && result.Any())
            {
                InfPath = result.First();
            }
        }

        public override void LoadEvent(EventCore eventCore)
        {
            _isLoading = true;
            if (eventCore is Driver driver)
            {
                Action = driver.Action;
                InfPath = driver.InfPath;
                InfName = driver.InfName;
                IsPermanent = driver.IsPermanent;
                Schedules.Clear();
                Schedules.AddRange(driver.Schedules);
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
                LinkedEvent = driver;
            }
            CreateDriverCardView();
            _isLoading = false;
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is Driver driver)
            {
                driver.Action = Action;
                driver.InfPath = InfPath;
                driver.InfName = string.IsNullOrWhiteSpace(InfName) && !string.IsNullOrWhiteSpace(InfPath)
                    ? Path.GetFileName(InfPath)
                    : InfName;
                driver.IsPermanent = IsPermanent;
                driver.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateDriverEvent(driver);
            }
        }
    }
}
