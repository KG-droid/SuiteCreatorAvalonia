using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Services;
using System;
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
        private PackageBase? _selectedPackage;

        [ObservableProperty]
        private PackageDetailsBaseViewModel? _currentPackageView;

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
            _coreManager.RemovePackage(SelectedPackage);
            PackageList = _coreManager.GetPackages();
            SelectedPackage = PackageList.FirstOrDefault();
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
    }
}