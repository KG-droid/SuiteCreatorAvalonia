using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using MSIx = SuiteTools.MSIx;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageMSIxRemoveDetailsViewModel : PackageDetailsBaseViewModel
    {
        private bool _isLoading = false;
        private SuiteCoreManager _suiteCoreManager;

        [ObservableProperty]
        private bool _isForceCloseFamily = true;

        [ObservableProperty]
        private bool _isProductRemoval = false;

        [ObservableProperty]
        private string _removalCode = string.Empty;

        partial void OnIsProductRemovalChanged(bool value)
        {
            if (!_isLoading)
                RemovalCode = string.Empty;
        }

        public PackageMSIxRemoveDetailsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
        }

        public PackageMSIxRemoveDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager) : base(viewFactory, suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
        }

        public override void LoadPackage(Guid packageId)
        {
            _isLoading = true;
            var msixPackage = _suiteCoreManager.GetPackage(packageId) as MSIxRemoval;
            if (msixPackage != null)
            {
                Id = msixPackage.Id;
                Name = msixPackage.Name;
                IsProductRemoval = msixPackage.IsSpecificVersionRemoval;
                RemovalCode = msixPackage.PFN ?? string.Empty;
                HasRequirements = msixPackage.RequirementRuleSetId is Guid;
                RequirementRuleBuilder.Clear();
                if (msixPackage.RequirementRuleSetId is Guid reqGuid)
                {
                    RequirementRuleBuilder.LoadRuleSet(_suiteCoreManager.GetRuleSet(reqGuid));
                }
            }
            _isLoading = false;
        }

        public override void SavePackage()
        {
            if (_isLoading || Id is not Guid) return;
            MSIxRemoval msixPackage = GetVerifiedPackage<MSIxRemoval>((Guid)Id);

            msixPackage.IsSpecificVersionRemoval = IsProductRemoval;
            msixPackage.PFN = RemovalCode;

            IEnumerable<RuleBase>? reqRules = RequirementRuleBuilder.Rules;
            if (reqRules != null && reqRules.Count() > 0)
            {
                RuleSet ruleSet;
                if (msixPackage.RequirementRuleSetId is not Guid)
                {
                    ruleSet = _suiteCoreManager.NewRuleSet(Name + " (Pkg Req)");
                    msixPackage.RequirementRuleSetId = ruleSet.Id;
                }
                else
                {
                    ruleSet = _suiteCoreManager.GetRuleSet((Guid)msixPackage.RequirementRuleSetId);
                }
                ruleSet.Rules = RequirementRuleBuilder.Rules;
                _suiteCoreManager.UpdateRuleSet(ruleSet);
            }
            else if (msixPackage.RequirementRuleSetId is Guid existingGuid)
            {
                msixPackage.RequirementRuleSetId = null;
                _suiteCoreManager.UpdatePackage(msixPackage);
                try
                {
                    _suiteCoreManager.RemoveRuleSet(existingGuid);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Cannot remove RuleSet as it is being used"))
                    {
                        // Dont delete the rule its still being used other events
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            msixPackage.Name = Name;
            _suiteCoreManager.UpdatePackage(msixPackage);
        }

        [RelayCommand]
        private async void BrowseForPFN(string pfnType)
        {
            IEnumerable<string>? filePath = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for an MSIx", FileTypeFilter = SysIOPickerTypes.MSIx });
            if (filePath != null && filePath.Count() > 0)
            {
                using (MSIx msix = new MSIx(filePath.First()))
                {
                    switch (pfnType)
                    {
                        case "PackageFamilyName":
                            RemovalCode = msix.PackageFamilyName;
                            break;
                        case "PackageFullName":
                            RemovalCode = msix.PackageFullName;
                            break;
                    }
                }
            }
        }
    }
}
