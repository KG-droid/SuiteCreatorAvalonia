using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
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
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class EnvCardViewModel : EventCardWithPermanenceViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private string? _name;

        [ObservableProperty]
        private string? _value;

        [ObservableProperty]
        private Environmentbehaviour _behaviour = Environmentbehaviour.Append;

        [ObservableProperty]
        private Enums.Separator _separator = Enums.Separator.Semicolon;

        partial void OnBehaviourChanged(Environmentbehaviour value)
        {
            CreateEnvCardView();
        }

        public EnvCardViewModel() : this(
            new Environment(),
            new SuiteCoreManager())
        {
        }

        public EnvCardViewModel(Environment env, SuiteCoreManager suiteCoreManager) : base(env, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (env != null)
                LoadEvent(env);
            CreateEnvCardView();
        }

        private void CreateEnvCardView()
        {
            Grid mainGrid = new Grid();
            CardInnerView = mainGrid;

            // Behaviour     
            ComboBox behaviourComboBox = new ComboBox();
            behaviourComboBox.ItemsSource = Enum.GetValues(typeof(Environmentbehaviour));
            behaviourComboBox.Bind(ComboBox.SelectedValueProperty, new Binding("Behaviour"));
            behaviourComboBox.Background = Avalonia.Media.Brushes.Transparent;
            behaviourComboBox.SelectionChanged += HoldCardOpenAfterComboChange;
            behaviourComboBox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(behaviourComboBox, "Behaviour", "How this event changes the variable: Append/Prepend a value, Replace the whole variable, or Remove a value/the variable.");
            Grid.SetColumn(behaviourComboBox, 0);
            CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            mainGrid.Children.Add(behaviourComboBox);

            // Name
            TextBox nameTextBox = new TextBox();
            nameTextBox.Bind(TextBox.TextProperty, new Binding("Name"));
            nameTextBox.PlaceholderText = "Variable name";
            Help.Annotate(nameTextBox, "Variable name", "The environment variable to set, e.g. PATH.");
            nameTextBox.Name = "Name_TextBox";
            nameTextBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(nameTextBox, 1);
            mainGrid.Children.Add(nameTextBox);

            // Rest of the card
            switch (Behaviour)
            {
                case Environmentbehaviour.Append:
                case Environmentbehaviour.Prepend:
                case Environmentbehaviour.Replace:
                default:
                    AddValueFields();
                    break;
                case Environmentbehaviour.Remove:
                    AddRemovalValueButton();
                    break;
            }

        }

        private void AddValueFields()
        {
            // Remove button if it exists from a Remove behaviour
            if (CardInnerView.Children.Last() is Button btn)
            {
                CardInnerView.Children.Remove(btn);
            }

            // Equals icon
            MaterialIcon equalsIcon = new MaterialIcon { Kind = Material.Icons.MaterialIconKind.Equal };
            equalsIcon.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            equalsIcon.Margin = new Avalonia.Thickness(10, 0, 10, 0);
            Grid.SetColumn(equalsIcon, 2);
            CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            CardInnerView.Children.Add(equalsIcon);

            // Value
            TextBox valueTextBox = new TextBox();
            valueTextBox.Bind(TextBox.TextProperty, new Binding("Value"));
            valueTextBox.PlaceholderText = "The value";
            valueTextBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            Help.Annotate(valueTextBox, "Value", "The value appended, prepended to, or replacing the variable, depending on the Behaviour selected.");
            CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(valueTextBox, 3);
            CardInnerView.Children.Add(valueTextBox);

            if (Behaviour != Environmentbehaviour.Replace)
            {
                // Toggle Permanent
                ToggleButton permanentFileToggle = new();
                permanentFileToggle.Classes.Add("IconToggleButton");
                permanentFileToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("IsPermanent"));
                permanentFileToggle.Tag = "DiamondStone";
                ToolTip.SetTip(permanentFileToggle, "If enabled, the Environment Variable/Value won't be removed on Suite removal");
                Help.Annotate(permanentFileToggle, "Permanent", "If enabled, this Environment Variable/Value won't be removed on Suite removal.");
                Grid.SetColumn(permanentFileToggle, 4);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                CardInnerView.Children.Add(permanentFileToggle);

                // Set Custom Separator button
                Button setCustomSeparatorButton = new Button();
                setCustomSeparatorButton.Classes.Add("IconButton");
                setCustomSeparatorButton.Tag = "SlashForwardBox";
                setCustomSeparatorButton.Margin = new Avalonia.Thickness(0, 0, 10, 0);
                ToolTip.SetTip(setCustomSeparatorButton, "Set the variable value separator (Defaults to Semi Colon)(For when a variable can have multiple values, and a Semi Colon wont do)");
                Help.Annotate(setCustomSeparatorButton, "Separator", "Sets the separator character used between values in this variable. Defaults to semicolon; change it if the variable needs a different separator.");
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                Grid.SetColumn(setCustomSeparatorButton, 5);
                CardInnerView.Children.Add(setCustomSeparatorButton);

                // Separator config Flyout   
                Flyout separatorFlyout = new();
                ComboBox separatorComboBox = new ComboBox();
                separatorComboBox.ItemsSource = Enum.GetValues(typeof(Enums.Separator));
                ToolTip.SetTip(separatorComboBox, "The separator symbol between values");
                separatorComboBox.Bind(ComboBox.SelectedValueProperty, new Binding("Separator"));
                separatorComboBox.Background = Avalonia.Media.Brushes.Transparent;
                separatorComboBox.SelectionChanged += HoldCardOpenAfterComboChange;
                separatorComboBox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                separatorComboBox.Margin = new Avalonia.Thickness(0, 0, 10, 0);
                separatorFlyout.Content = separatorComboBox;
                setCustomSeparatorButton.Flyout = separatorFlyout;
            }

            if (Behaviour == Environmentbehaviour.Replace)
                valueTextBox.Margin = new Avalonia.Thickness(0, 0, 10, 0);

            if (Behaviour == Environmentbehaviour.Remove)
            {
                Button addRemoveValueButton = new Button();
                addRemoveValueButton.Classes.Add("IconButton");
                addRemoveValueButton.Tag = "Minus";
                addRemoveValueButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
                ToolTip.SetTip(addRemoveValueButton, "Remove a whole variable instead");
                Help.Annotate(addRemoveValueButton, "Remove whole variable", "Switches to removing the entire variable instead of just one value from it.");
                addRemoveValueButton.Command = new RelayCommand(RemoveValueFields);
                addRemoveValueButton.Bind(Button.ForegroundProperty, new Binding { Source = Avalonia.Application.Current.FindResource("BinButton") });
                Grid.SetColumn(addRemoveValueButton, 5);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                CardInnerView.Children.Add(addRemoveValueButton);
            }
        }

        private void AddRemovalValueButton()
        {
            Button removalValueButton = new Button();
            removalValueButton.Classes.Add("IconButton");
            removalValueButton.Tag = "Plus";
            removalValueButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
            ToolTip.SetTip(removalValueButton, "Remove a Value instead of the whole variable");
            Help.Annotate(removalValueButton, "Remove a value", "Switches to removing a single value from this variable instead of the whole variable.");
            removalValueButton.Command = new RelayCommand(AddValueFields);
            removalValueButton.Bind(Button.ForegroundProperty, new Binding { Source = Avalonia.Application.Current.FindResource("PlusButton") });
            Grid.SetColumn(removalValueButton, 2);
            CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            CardInnerView.Children.Add(removalValueButton);
        }

        private void RemoveValueFields()
        {
            var nameControl = CardInnerView.Children.Where(c => c.Name == "Name_TextBox").First();
            int nameControlSiblingIndex = (int)CardInnerView.Children.IndexOf(nameControl) + 1;
            CardInnerView.Children.RemoveRange(nameControlSiblingIndex, CardInnerView.Children.Count - nameControlSiblingIndex);
            AddRemovalValueButton();
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is Environment env)
            {
                _isLoading = true;
                Name = env.Name;
                Value = env.Value;
                Behaviour = env.Behaviour;
                Separator = env.Separator;
                Schedules.Clear();
                Schedules.AddRange(env.Schedules);
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
                LinkedEvent = env;
                _isLoading = false;
            }
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is Environment env)
            {
                env.Name = Name;
                env.Value = Value;
                env.Behaviour = Behaviour;
                env.Separator = Separator;
                env.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateEnvironmentEvent(env);
            }
        }
    }
}
