using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons.Avalonia;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class RegCardViewModel : EventCardWithPermanenceViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private RegAction? _action = RegAction.AddAmend;

        [ObservableProperty]
        private string _keyPath;

        [ObservableProperty]
        private string _regFilePath;

        [ObservableProperty]
        private RegistryPropertyType? _propertyType = RegistryPropertyType.String;

        [ObservableProperty]
        private string _propertyName;

        [ObservableProperty]
        private string _propertyValue;

        [ObservableProperty]
        private bool _overwrite = true;

        public RegCardViewModel() : this(
            new Registry(),
            new SuiteCoreManager())
        {
        }

        public RegCardViewModel(Registry regEvent, SuiteCoreManager suiteCoreManager) : base(regEvent, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (regEvent != null)
                LoadEvent(regEvent);
            CreateRegCardView();
        }

        partial void OnActionChanged(RegAction? value)
        {
            CreateRegCardView();
        }

        private void CreateRegCardView()
        {
            Grid mainGrid = new Grid();
            CardInnerView = mainGrid;

            // Action
            ComboBox actionCombo = new ComboBox();
            actionCombo.ItemsSource = Enum.GetValues(typeof(RegAction));
            actionCombo.Bind(ComboBox.SelectedValueProperty, new Binding("Action"));
            actionCombo.Background = Avalonia.Media.Brushes.Transparent;
            actionCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            actionCombo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(actionCombo, "Action", "What to do to the registry: Add/Amend a key or value, Remove a value, or Import a .reg file.");
            Grid.SetColumn(actionCombo, 0);
            CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            CardInnerView.Children.Add(actionCombo);

            if (Action == RegAction.Import)
            {
                // Reg File Path 
                TextBox regFilePathTextBox = new TextBox();
                regFilePathTextBox.Bind(TextBox.TextProperty, new Binding("RegFilePath"));
                regFilePathTextBox.PlaceholderText = ".reg file Path";
                regFilePathTextBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                Help.Annotate(regFilePathTextBox, "Reg file path", "Path to a .reg file whose contents will be imported into the registry on the target device.");
                Grid.SetColumn(regFilePathTextBox, 1);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                CardInnerView.Children.Add(regFilePathTextBox);

                Button regFileBrowseButton = new Button();
                regFileBrowseButton.Classes.Add("IconButton");
                regFileBrowseButton.Tag = "CubeUnfolded";
                regFileBrowseButton.Margin = new Avalonia.Thickness(5, 0, 5, 0);
                regFileBrowseButton.Command = new RelayCommand(RegBrowse);
                ToolTip.SetTip(regFileBrowseButton, "Browse for a registry .reg file");
                Help.Annotate(regFileBrowseButton, "Browse", "Browse for a .reg file to import.");
                Grid.SetColumn(regFileBrowseButton, 2);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(regFileBrowseButton);
                CardInnerView.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
                return;
            }

            // Key Path 
            TextBox textBox = new TextBox();
            textBox.Bind(TextBox.TextProperty, new Binding("KeyPath"));
            textBox.PlaceholderText = "Key Path";
            textBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            Help.Annotate(textBox, "Key path", "The full registry key path this event acts on, e.g. HKEY_LOCAL_MACHINE\\SOFTWARE\\MyApp.");
            Grid.SetColumn(textBox, 1);
            CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            CardInnerView.Children.Add(textBox);

            // Property
            if (Action == RegAction.AddAmend)
            {
                // Property Type
                ComboBox propertyTypeCombo = new ComboBox();
                propertyTypeCombo.ItemsSource = Enum.GetValues(typeof(RegistryPropertyType));
                propertyTypeCombo.Bind(ComboBox.SelectedValueProperty, new Binding("PropertyType"));
                propertyTypeCombo.Background = Avalonia.Media.Brushes.Transparent;
                propertyTypeCombo.SelectionChanged += HoldCardOpenAfterComboChange;
                Help.Annotate(propertyTypeCombo, "Property type", "The registry value's data type (String, DWORD, Binary, etc).");
                Grid.SetColumn(propertyTypeCombo, 2);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                CardInnerView.Children.Add(propertyTypeCombo);

                // Property Name
                TextBox typeNameTextBox = new TextBox();
                typeNameTextBox.Bind(TextBox.TextProperty, new Binding("PropertyName"));
                typeNameTextBox.PlaceholderText = "Property Name";
                typeNameTextBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                Help.Annotate(typeNameTextBox, "Property name", "The name of the registry value to create or amend under the key path.");
                Grid.SetColumn(typeNameTextBox, 3);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                CardInnerView.Children.Add(typeNameTextBox);

                // Equals icon
                MaterialIcon equalsIcon = new MaterialIcon { Kind = Material.Icons.MaterialIconKind.Equal };
                equalsIcon.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                Grid.SetColumn(equalsIcon, 4);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                CardInnerView.Children.Add(equalsIcon);

                // Property Value 
                TextBox typeValuetextBox = new TextBox();
                typeValuetextBox.Bind(TextBox.TextProperty, new Binding("PropertyValue"));
                typeValuetextBox.PlaceholderText = "Property Value";
                typeValuetextBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                Help.Annotate(typeValuetextBox, "Property value", "The data written into the registry value.");
                Grid.SetColumn(typeValuetextBox, 5);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                CardInnerView.Children.Add(typeValuetextBox);

                // Overwrite
                ToggleButton continueOnNotFoundToggleButton = new ToggleButton();
                continueOnNotFoundToggleButton.Bind(CheckBox.IsCheckedProperty, new Binding("ContinueOnNotFound"));
                continueOnNotFoundToggleButton.Classes.Add("IconToggleButton");
                continueOnNotFoundToggleButton.Tag = "AlphabeticalOff";
                continueOnNotFoundToggleButton.Margin = new Avalonia.Thickness(0, 0, 10, 0);
                ToolTip.SetTip(continueOnNotFoundToggleButton, "Toggle replace existing");
                Help.Annotate(continueOnNotFoundToggleButton, "Replace existing", "Toggle whether an existing value at this name is overwritten.");
                Grid.SetColumn(continueOnNotFoundToggleButton, 6);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(continueOnNotFoundToggleButton);

                // Toggle Permanent
                ToggleButton permanentFileToggle = new();
                permanentFileToggle.Classes.Add("IconToggleButton");
                permanentFileToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("IsPermanent"));
                permanentFileToggle.Tag = "DiamondStone";
                ToolTip.SetTip(permanentFileToggle, "If enabled, the registry key/value won't be removed on Suite removal");
                Help.Annotate(permanentFileToggle, "Permanent", "If enabled, this registry key/value won't be removed on Suite removal.");
                Grid.SetColumn(permanentFileToggle, 7);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                CardInnerView.Children.Add(permanentFileToggle);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(PropertyName))
                    AddPropertyRemovalTextbox();
                else
                    AddPropertyButton();
            }

        }

        private async void RegBrowse()
        {
            var result = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for a registry .reg file", FileTypeFilter = SysIOPickerTypes.Reg });
            if (null != result && result.Any())
            {
                RegFilePath = result.First();
            }
        }

        private void AddPropertyRemovalTextbox()
        {
            Button? buttonToRemove = CardInnerView?.Children?.Where(x => x.Name == "AddPropertyRemoval_Button")?.FirstOrDefault() as Button;
            if (null != buttonToRemove)
            {
                CardInnerView?.Children?.Remove(buttonToRemove);
            }
            TextBox textBox = new TextBox();
            textBox.Bind(TextBox.TextProperty, new Binding("PropertyName"));
            textBox.Name = "PropertyRemoval_TextBox";
            textBox.PlaceholderText = "Property Name";
            Help.Annotate(textBox, "Property to remove", "The name of the registry value to remove. Leave it as a key path only (no property name) to remove the whole key instead.");
            Grid.SetColumn(textBox, 3);
            CardInnerView?.Children.Add(textBox);

            Button revertToKeyRemovalButton = new Button();
            revertToKeyRemovalButton.Classes.Add("IconButton");
            revertToKeyRemovalButton.Name = "RevertPropertyRemoval_Button";
            revertToKeyRemovalButton.Tag = "Minus";
            revertToKeyRemovalButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
            revertToKeyRemovalButton.Bind(Button.ForegroundProperty, new Binding { Source = Avalonia.Application.Current.FindResource("BinButton") });
            revertToKeyRemovalButton.Command = new RelayCommand(RemoveRevertPropertyButton);
            Grid.SetColumn(revertToKeyRemovalButton, 4);
            CardInnerView?.Children.Add(revertToKeyRemovalButton);

            CardInnerView.ColumnDefinitions = new ColumnDefinitions("Auto,*,10,*,Auto");
        }

        private void RemoveRevertPropertyButton()
        {
            Control? buttonToRemove = CardInnerView?.Children?.Where(x => x.Name == "RevertPropertyRemoval_Button")?.First() as Control;
            if (null != buttonToRemove)
            {
                CardInnerView?.Children?.Remove(buttonToRemove);
            }
            Control? textBoxToRemove = CardInnerView?.Children?.Where(x => x.Name == "PropertyRemoval_TextBox")?.First() as Control;
            if (null != textBoxToRemove)
            {
                CardInnerView?.Children?.Remove(textBoxToRemove);
            }
            AddPropertyButton();
        }

        private void AddPropertyButton()
        {
            Button addValueButton = new Button();
            addValueButton.Classes.Add("IconButton");
            addValueButton.Name = "AddPropertyRemoval_Button";
            addValueButton.Tag = "Plus";
            addValueButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
            addValueButton.Bind(Button.ForegroundProperty, new Binding { Source = Avalonia.Application.Current.FindResource("PlusButton") });
            addValueButton.Command = new RelayCommand(AddPropertyRemovalTextbox);
            ToolTip.SetTip(addValueButton, "Remove a Property");
            Grid.SetColumn(addValueButton, 3);
            CardInnerView.Children.Add(addValueButton);
            CardInnerView.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is Registry reg)
            {
                _isLoading = true;
                Action = reg.Action;
                KeyPath = reg.KeyPath;
                RegFilePath = reg.RegFilePath?.LocalPath;
                PropertyType = reg.PropertyType;
                PropertyName = reg.PropertyName;
                PropertyValue = reg.PropertyValue;
                Overwrite = reg.Overwrite;
                Schedules.Clear();
                Schedules.AddRange(reg.Schedules);
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
                LinkedEvent = reg;
                _isLoading = false;
            }
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is Registry reg)
            {
                reg.Action = Action;
                reg.KeyPath = KeyPath;
                reg.RegFilePath = !string.IsNullOrWhiteSpace(RegFilePath) ? new Uri(RegFilePath) : null;
                reg.PropertyType = PropertyType;
                reg.PropertyName = PropertyName;
                reg.PropertyValue = PropertyValue;
                reg.Overwrite = Overwrite;
                reg.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateRegistryEvent(reg);
            }
        }
    }
}
