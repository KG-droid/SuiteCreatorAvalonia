using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.IDataTemplates;
using SuiteCreatorAvalonia.Models.Rules;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using RuleSet = SuiteCreatorAvalonia.Models.Rules.RuleSet;

namespace SuiteCreatorAvalonia.ViewModels.RuleBuilder
{
    internal partial class RuleBuilderViewModel : ViewModelBase
    {
        private bool _isLoading = false;

        [ObservableProperty]
        private ItemsControl _ruleItemsControl = new();

        [ObservableProperty]
        private bool _isRemoveMode = false;

        [ObservableProperty]
        private bool _isRuleTypePickerOpen = false;

        [ObservableProperty]
        private ObservableCollection<RuleBase> _rules = new();

        public event EventHandler<EventArgs> RulesAmended;

        public RuleBuilderViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }))
        {
        }

        public RuleBuilderViewModel(ViewFactory viewFactory)
        {
            RuleItemsControl.DataTemplates.Add(new RuleDataTemplate(viewFactory, this));
            RuleItemsControl.ItemsSource = Rules;
            Rules.CollectionChanged += (s, e) =>
            {
                if (!_isLoading && e.Action != NotifyCollectionChangedAction.Reset)
                    RulesAmended?.Invoke(this, EventArgs.Empty);
            };
        }

        public void Clear()
        {
            Rules.Clear();
        }

        public void TriggerRulesAmended()
        {
            RulesAmended?.Invoke(this, EventArgs.Empty);
        }

        public string IsRuleSetComplete()
        {
            return "All rules are fully configured";
        }

        [RelayCommand]
        public void ToggleRuleTypePicker()
        {
            IsRuleTypePickerOpen = !IsRuleTypePickerOpen;
        }

        [RelayCommand]
        public void AddNewItem(string ruleType)
        {
            if (ruleType == "Group")
            {
                var groupOpenChilds = Rules.Where(c => c.Type == RuleType.GroupOpen);
                var groupCloseChilds = Rules.Where(c => c.Type == RuleType.GroupClose);
                if (groupOpenChilds.Count() == groupCloseChilds.Count())
                {
                    AddItem(new Rule { Type = RuleType.GroupOpen });
                }
                else
                {
                    AddItem(new Rule { Type = RuleType.GroupClose });
                }
            }
            else
            {
                AddItem(new Rule { Type = (RuleType)Enum.Parse(typeof(RuleType), ruleType) });
            }
        }

        public void AddItem(RuleBase rule)
        {
            Rules.Add(rule);
            IsRuleTypePickerOpen = false;
        }

        [RelayCommand]
        public void RemoveAllItems()
        {
            Rules.Clear();
            RulesAmended?.Invoke(null, new EventArgs());
        }

        [RelayCommand]
        public void ToggleRemoveMode()
        {
            IsRemoveMode = !IsRemoveMode;
        }

        public void LoadRuleSet(RuleSet ruleset)
        {
            if (ruleset == null) return;
            _isLoading = true;
            Clear();
            if (ruleset.Rules != null && ruleset.Rules.Count() > 0)
            {
                foreach (RuleBase rule in ruleset.Rules)
                {
                    AddItem(rule.Clone());
                }
            }
            _isLoading = false;
        }
    }
}