using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using RuleSet = SuiteCreatorAvalonia.Models.Rules.RuleSet;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class RulesViewModel : ViewModelBase
    {
        private bool _loadingRuleSet = false;
        private SuiteCoreManager _suiteCoreManager;

        [ObservableProperty]
        private string? _ruleRemoveError = null;

        [ObservableProperty]
        private RuleBuilderViewModel _ruleBuilder;

        [ObservableProperty]
        private ObservableCollection<RuleSet> _ruleSets = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RemoveSelectedRuleSetCommand))]
        private RuleSet? _selectedRuleSet = null;

        partial void OnSelectedRuleSetChanged(RuleSet? value)
        {
            try
            {
                _loadingRuleSet = true;
                if (value is RuleSet)
                {
                    RuleSet? matching = _suiteCoreManager.GetRuleSets().FirstOrDefault(r => r.Id == value.Id);
                    if (matching != null)
                    {
                        RuleBuilder.LoadRuleSet(matching);
                    }
                    else
                    {
                        throw new Exception($"Cannot find a rule with ID: {value.Id}, in the suite core config");
                    }
                }
            }
            finally
            {
                _loadingRuleSet = false;
            }
        }

        public RulesViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
        }

        public RulesViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
            RuleBuilder = (RuleBuilderViewModel)viewFactory.GetVM(typeof(RuleBuilderViewModel));
            RuleBuilder.RulesAmended += (s, e) => SaveRuleSet();
            LoadRuleSets();
            if (RuleSets.Count > 0)
            {
                SelectedRuleSet = RuleSets.FirstOrDefault();
            }
        }

        public void AddRuleSet(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                newName = "New Rule Set";
            }
            RuleSet ruleset = _suiteCoreManager.NewRuleSet(newName);
            RuleSets = _suiteCoreManager.GetRuleSets();
            SelectedRuleSet = RuleSets.LastOrDefault();
        }

        private bool CanRemoveRuleSet() => SelectedRuleSet != null;

        [RelayCommand(CanExecute = nameof(CanRemoveRuleSet))]
        public void RemoveSelectedRuleSet()
        {
            if (SelectedRuleSet != null)
            {
                try
                {
                    _suiteCoreManager.RemoveRuleSet(SelectedRuleSet);
                    LoadRuleSets();
                }
                catch (Exception ex)
                {
                    RuleRemoveError = ex.Message;
                }
            }
        }

        public void EditRuleSetName(string newName)
        {
            if (SelectedRuleSet != null && !string.IsNullOrEmpty(newName))
            {
                SelectedRuleSet.Name = newName;
                SaveRuleSet();
                LoadRuleSets();
                SelectedRuleSet = RuleSets.FirstOrDefault();
            }
        }

        public void LoadRuleSets()
        {
            _loadingRuleSet = true;
            RuleSets = _suiteCoreManager.GetRuleSets();
            _loadingRuleSet = false;
        }

        public void SaveRuleSet()
        {
            if (_loadingRuleSet || SelectedRuleSet == null) return;
            SelectedRuleSet.Rules = RuleBuilder.Rules;
            _suiteCoreManager.UpdateRuleSet(SelectedRuleSet);
        }
    }
}
