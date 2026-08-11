using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageViewModel : ViewModelBase
    {
        private SuiteCoreManager _coreManager;
        private ViewFactory _viewFact;
        private bool _nameChanging = false;

        [ObservableProperty]
        private ObservableCollection<PackageBase> _packageList;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ShowPackageUsagesCommand))]
        private PackageBase? _selectedPackage;

        [ObservableProperty]
        private PackageDetailsBaseViewModel? _currentPackageView;

        [ObservableProperty]
        private string? _packageRemoveError;

        [ObservableProperty]
        private bool _usagesPopupOpen = false;

        [ObservableProperty]
        private ObservableCollection<SuiteUsageItem> _packageUsages = new();

        partial void OnSelectedPackageChanged(PackageBase? value)
        {
            if (value != null)
            {
                if (CurrentPackageView?.Id == value.Id) return;
                if (Enum.TryParse(typeof(PackageType), value.GetType().Name, out object? vmType))
                {
                    if (!PackageTypeToViewModelMapper.Mappings.TryGetValue((PackageType)vmType, out Type? packageVMType))
                        throw new Exception($"Unrecognized package type: {value}");
                    if (packageVMType != CurrentPackageView?.GetType())
                    {
                        CurrentPackageView = (PackageDetailsBaseViewModel)_viewFact.GetVM(packageVMType);
                    }
                    CurrentPackageView.LoadPackage(value.Id);
                }
                else
                {
                    throw new Exception($"Failed to parse package type for: {value}");
                }
            }
            else if (!_nameChanging)
            {
                CurrentPackageView = null;
            }
        }

        public PackageViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
        }

        public PackageViewModel(ViewFactory viewFactory, SuiteCoreManager core)
        {
            _viewFact = viewFactory;
            _coreManager = core;
            PackageList = _coreManager.GetPackages();
            _coreManager.PackageNameChanged += PackageNameChanged;
        }

        private void PackageNameChanged(object? sender, EventArgs e)
        {
            _nameChanging = true;
            try 
            {
                Guid? currentSelectedId = SelectedPackage?.Id;
                PackageList = _coreManager.GetPackages();
                if (currentSelectedId is Guid pkgGuid)
                {
                    SelectedPackage = PackageList.FirstOrDefault(p => p.Id == pkgGuid);
                }
            }
            finally
            {
                _nameChanging = false;
            }
        }

        public void AddPackageByType(string newPackageName, PackageType pkgType)
        {
            string typeName = pkgType.ToString();
            Type? packageType = AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(a => a.GetType($"SuiteCreatorAvalonia.Models.Package.{typeName}"))
                .FirstOrDefault(t => t != null);
            if (packageType == null) throw new Exception($"Could not find package type for: {typeName}");

            PackageBase newPkg = _coreManager.NewPackage(packageType, newPackageName);
            PackageList = _coreManager.GetPackages();
            SelectedPackage = PackageList.Last();
        }

        public void RemoveSelectedPackage()
        {
            if (SelectedPackage == null) return;
            try
            {
                _coreManager.RemovePackage(SelectedPackage);
                PackageList = _coreManager.GetPackages();
                SelectedPackage = PackageList.FirstOrDefault();
            }
            catch (Exception ex)
            {
                PackageRemoveError = ex.Message;
            }
        }

        public void SaveOrder()
        {
            _coreManager.ReorderPackages(PackageList.Select(p => p.Id));
            Guid? currentSelectedId = SelectedPackage?.Id;
            PackageList = _coreManager.GetPackages();
            if (currentSelectedId is Guid pkgId)
            {
                SelectedPackage = PackageList.FirstOrDefault(p => p.Id == pkgId);
            }
        }

        private bool CanShowPackageUsages() => SelectedPackage != null;

        [RelayCommand(CanExecute = nameof(CanShowPackageUsages))]
        public void ShowPackageUsages()
        {
            if (SelectedPackage == null) return;
            List<EventCore> events = _coreManager.GetEventsUsingPackageAsStage(SelectedPackage.Id);
            ObservableCollection<SuiteUsageItem> items = new();
            foreach (EventCore evt in events)
            {
                items.Add(SuiteUsageItem.ForEvent(evt, s => s.EventStageId == SelectedPackage.Id));
            }
            PackageUsages = items;
            UsagesPopupOpen = true;
        }

        public void GoToUsage(SuiteUsageItem item)
        {
            UsagesPopupOpen = false;
            if (item.Event != null)
            {
                AppNavigationService.Instance.NavigateToEvent?.Invoke(item.Event);
            }
        }
    }
}