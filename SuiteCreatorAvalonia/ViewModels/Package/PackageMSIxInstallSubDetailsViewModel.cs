using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageMSIxInstallSubDetailsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string? _msixPath;

        [ObservableProperty]
        private bool _isForceCloseFamily = true;

        [ObservableProperty]
        private bool _removeOnSuiteRemoval = true;

        [ObservableProperty]
        private bool _isRollbackSection = false;

        [ObservableProperty]
        private bool _isForceThisVersion = false;

        [ObservableProperty]
        private bool _hasDependencies = false;

        [ObservableProperty]
        private bool _deferInUse = false;

        [ObservableProperty]
        private ObservableCollection<string> _dependencies = new();

        [ObservableProperty]
        private ObservableCollection<string> _selectedDependents = new();

        public PackageMSIxInstallSubDetailsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
        }

        public PackageMSIxInstallSubDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager)
        {

        }

        [RelayCommand]
        public async Task BrowseForMSIx()
        {
            IEnumerable<string>? filePath = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for an MSIx", FileTypeFilter = SysIOPickerTypes.MSIx });
            if (filePath != null && filePath.Count() > 0)
                MsixPath = filePath.First();
        }

        [RelayCommand]
        public async Task BrowseForDependent()
        {
            IEnumerable<string>? filePath = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = true, Title = "Browse for an MSIx", FileTypeFilter = SysIOPickerTypes.MSIx });
            if (filePath != null && filePath.Count() > 0)
                Dependencies.AddRange(filePath.ToList());
        }

        [RelayCommand]
        public void RemoveSelectedDependents()
        {
            if (SelectedDependents.Count > 0)
            {
                string[] selectedList = this.SelectedDependents.ToArray();
                foreach (string d in selectedList)
                {
                    Dependencies.Remove(d);
                }
            }
        }
    }
}
