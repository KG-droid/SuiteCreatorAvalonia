using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {

        public MainWindowViewModel() : this(new ViewFactory(
            type =>
            {
                return (ViewModelBase)Activator.CreateInstance(type)!;
            })
        )
        {
        }

        public MainWindowViewModel(ViewFactory fact)
        {
            MainView = fact.GetVM(typeof(MainViewModel));
        }

        [ObservableProperty]
        internal ViewModelBase _mainView;

        [RelayCommand]
        public async Task<IEnumerable<string>?> DirectoryBrowse(FolderPickerOpenOptions folderPickerOpenOptions)
        {
            return await this.OpenFolderDialogAsync(folderPickerOpenOptions);
        }

        [RelayCommand]
        public async Task<IEnumerable<string>?> FileBrowse(FilePickerOpenOptions filePickerOptions)
        {
            return await this.OpenFileDialogAsync(filePickerOptions);
        }

        [RelayCommand]
        public async Task Save()
        {
            if (MainView is MainViewModel mVM)
            {
                await mVM.SaveProject();
            }
        }
    }
}
