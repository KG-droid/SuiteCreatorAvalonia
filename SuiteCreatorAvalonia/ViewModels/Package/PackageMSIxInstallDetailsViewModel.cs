using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageMSIxInstallDetailsViewModel : PackageDetailsBaseViewModel
    {
        private bool _isLoading = false;
        private SuiteCoreManager _suiteCoreManager;
        private PropertyChangedEventHandler? _msixDetailsPropHandler;
        private NotifyCollectionChangedEventHandler? _msixDetailsCollHandler;
        private PropertyChangedEventHandler? _msixRollbackPropHandler;
        private NotifyCollectionChangedEventHandler? _msixRollbackCollHandler;

        [ObservableProperty]
        private PackageMSIxInstallSubDetailsViewModel _mSIxDetailsSubView;

        [ObservableProperty]
        private PackageMSIxInstallSubDetailsViewModel _mSIxRollbackView;

        [ObservableProperty]
        private bool _addRollBack = false;

        partial void OnMSIxDetailsSubViewChanged(PackageMSIxInstallSubDetailsViewModel? oldValue, PackageMSIxInstallSubDetailsViewModel value)
        {
            if (oldValue != null)
            {
                if (_msixDetailsPropHandler != null)
                    oldValue.PropertyChanged -= _msixDetailsPropHandler;
                if (_msixDetailsCollHandler != null)
                    oldValue.Dependencies.CollectionChanged -= _msixDetailsCollHandler;
            }
            _msixDetailsPropHandler = (s, e) => OnPropertyChanged(nameof(MSIxDetailsSubView));
            _msixDetailsCollHandler = (s, e) => OnPropertyChanged(nameof(MSIxDetailsSubView));
            value.PropertyChanged += _msixDetailsPropHandler;
            value.Dependencies.CollectionChanged += _msixDetailsCollHandler;
        }

        partial void OnMSIxRollbackViewChanged(PackageMSIxInstallSubDetailsViewModel? oldValue, PackageMSIxInstallSubDetailsViewModel value)
        {
            if (oldValue != null)
            {
                if (_msixRollbackPropHandler != null)
                    oldValue.PropertyChanged -= _msixRollbackPropHandler;
                if (_msixRollbackCollHandler != null)
                    oldValue.Dependencies.CollectionChanged -= _msixRollbackCollHandler;
            }
            _msixRollbackPropHandler = (s, e) => OnPropertyChanged(nameof(MSIxRollbackView));
            _msixRollbackCollHandler = (s, e) => OnPropertyChanged(nameof(MSIxRollbackView));
            value.PropertyChanged += _msixRollbackPropHandler;
            value.Dependencies.CollectionChanged += _msixRollbackCollHandler;
        }

        public PackageMSIxInstallDetailsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
            PropertyChanged += (sender, args) => SavePackage();
        }

        public PackageMSIxInstallDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager) : base(viewFactory, suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
            MSIxDetailsSubView = (PackageMSIxInstallSubDetailsViewModel)viewFactory.GetVM(typeof(PackageMSIxInstallSubDetailsViewModel));
            MSIxRollbackView = (PackageMSIxInstallSubDetailsViewModel)viewFactory.GetVM(typeof(PackageMSIxInstallSubDetailsViewModel));
            MSIxRollbackView.IsRollbackSection = true;
        }

        public override void LoadPackage(Guid packageId)
        {
            _isLoading = true;
            var msixPackage = _suiteCoreManager.GetPackage(packageId) as MSIxPkg;
            if (msixPackage == null) return;
            base.LoadPackage(packageId);
            MSIxDetailsSubView.MsixPath = msixPackage.MSIxPath;
            MSIxDetailsSubView.IsForceCloseFamily = msixPackage.IsForceCloseFamily;
            MSIxDetailsSubView.IsForceThisVersion = msixPackage.IsForceThisVersion;
            MSIxDetailsSubView.HasDependencies = msixPackage.HasDependents;
            MSIxDetailsSubView.Dependencies.Clear();
            MSIxDetailsSubView.DeferInUse = msixPackage.IsDeferInUse;
            MSIxDetailsSubView.RemoveOnSuiteRemoval = msixPackage.RemoveOnSuiteRemoval;
            if (msixPackage.Dependents != null && msixPackage.Dependents.Count > 0)
                MSIxDetailsSubView.Dependencies.AddRange(msixPackage.Dependents);
            if (msixPackage.Rollback is MSIxPkg rollbackPkg && !string.IsNullOrEmpty(rollbackPkg.MSIxPath))
            {
                AddRollBack = true;
                MSIxRollbackView.MsixPath = rollbackPkg.MSIxPath;
                MSIxRollbackView.IsForceCloseFamily = rollbackPkg.IsForceCloseFamily;
                MSIxRollbackView.IsForceThisVersion = rollbackPkg.IsForceThisVersion;
                MSIxRollbackView.HasDependencies = rollbackPkg.HasDependents;
                if (rollbackPkg.Dependents != null && rollbackPkg.Dependents.Count > 0)
                    MSIxRollbackView.Dependencies.AddRange(rollbackPkg.Dependents);
                else
                    MSIxRollbackView.Dependencies.Clear();
                MSIxRollbackView.DeferInUse = rollbackPkg.IsDeferInUse;
            }
            else
            {
                AddRollBack = false;
            }
            RequirementRuleBuilder.Clear();
            if (msixPackage.RequirementRuleSetId is Guid reqGuid)
            {
                RequirementRuleBuilder.LoadRuleSet(_suiteCoreManager.GetRuleSet(reqGuid));
            }
            _isLoading = false;
        }

        public override void SavePackage()
        {
            if (_isLoading || Id is not Guid) return;
            base.SavePackage();
            var msixPackage = GetVerifiedPackage<MSIxPkg>((Guid)Id);
            msixPackage.MSIxPath = MSIxDetailsSubView.MsixPath;
            msixPackage.IsForceCloseFamily = MSIxDetailsSubView.IsForceCloseFamily;
            msixPackage.IsForceThisVersion = MSIxDetailsSubView.IsForceThisVersion;
            msixPackage.HasDependents = MSIxDetailsSubView.HasDependencies;
            msixPackage.Dependents = MSIxDetailsSubView.Dependencies.ToList();
            msixPackage.IsDeferInUse = MSIxDetailsSubView.DeferInUse;
            msixPackage.RemoveOnSuiteRemoval = MSIxDetailsSubView.RemoveOnSuiteRemoval;
            if (AddRollBack)
            {
                msixPackage.Rollback = new MSIxPkg
                {
                    MSIxPath = MSIxRollbackView.MsixPath,
                    IsForceCloseFamily = MSIxRollbackView.IsForceCloseFamily,
                    IsForceThisVersion = MSIxRollbackView.IsForceThisVersion,
                    IsDeferInUse = MSIxRollbackView.DeferInUse,
                    HasDependents = MSIxRollbackView.HasDependencies,
                    Dependents = MSIxRollbackView.Dependencies.ToList()
                };
            }
            _suiteCoreManager.UpdatePackage(msixPackage);
        }
    }
}
