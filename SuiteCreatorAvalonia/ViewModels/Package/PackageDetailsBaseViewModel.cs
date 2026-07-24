using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageDetailsBaseViewModel : ViewModelBase
    {
        private SuiteCoreManager _suiteCoreManager;
        private bool _isLoading = false;

        [ObservableProperty]
        private Guid? _id;

        [ObservableProperty]
        private string? _name;

        [ObservableProperty]
        private bool _hasRequirements = false;

        [ObservableProperty]
        private RuleBuilderViewModel _requirementRuleBuilder;

        public PackageDetailsBaseViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
            PropertyChanged += (sender, args) => SavePackage();
        }

        public PackageDetailsBaseViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager)
        {
            PropertyChanged += (sender, args) => SavePackage();
            _suiteCoreManager = suiteCoreManager;
            RequirementRuleBuilder = (RuleBuilderViewModel)viewFactory.GetVM(typeof(RuleBuilderViewModel));
            RequirementRuleBuilder.RulesAmended += (s, e) => SavePackage();
        }

        public virtual void LoadPackage(Guid packageId)
        {
            _isLoading = true;
            try
            {
                var package = _suiteCoreManager.GetPackage(packageId);
                Id = package.Id;
                Name = package.Name;
                HasRequirements = package.RequirementRuleSetId.HasValue;
                if (package.RequirementRuleSetId is Guid reqGuid)
                {
                    RequirementRuleBuilder.LoadRuleSet(_suiteCoreManager.GetRuleSet(reqGuid));
                }
            }
            finally 
            {
                _isLoading = false;
            }
        }

        public virtual void SavePackage()
        {
            if (_isLoading) return;
            PackageBase? package = GetVerifiedPackage<PackageBase>((Guid)Id);
            if (!package.Name.Equals(Name))
            {
                package.Name = Name;
                _suiteCoreManager.UpdatePackage(package);
            }
            ObservableCollection<RuleBase> reqRules = RequirementRuleBuilder.Rules;
            if (reqRules != null && reqRules.Count > 0)
            {
                RuleSet ruleSet;
                if (package.RequirementRuleSetId is not Guid)
                {
                    ruleSet = _suiteCoreManager.NewRuleSet(Name + " (Pkg Req)");
                    package.RequirementRuleSetId = ruleSet.Id;
                }
                else
                {
                    ruleSet = _suiteCoreManager.GetRuleSet((Guid)package.RequirementRuleSetId);
                }
                ruleSet.Rules = reqRules;
                _suiteCoreManager.UpdateRuleSet(ruleSet);
            }
            else if (package.RequirementRuleSetId is Guid existingGuid)
            {
                package.RequirementRuleSetId = null;
                _suiteCoreManager.UpdatePackage(package);
                try
                {
                    _suiteCoreManager.RemoveRuleSet(existingGuid);
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("Cannot remove RuleSet as it is being used"))
                    {
                        throw;
                    }
                }
            }
        }

        protected TPackage GetVerifiedPackage<TPackage>(Guid id) where TPackage : PackageBase
        {
            var package = _suiteCoreManager.GetPackage(id);
            if (package is not TPackage typedPkg)
                throw new ArgumentException($"Package must be of type {typeof(TPackage).Name}", nameof(package));
            return typedPkg;
        }

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (e.PropertyName.Equals("SelectedRemoveMSIs")) return; // Ignore this we dont want to trigger a save for it, its only used for selecting the Remove MSI from the list you want to remove.
            base.OnPropertyChanged(e);
            SavePackage();
        }
    }
}
