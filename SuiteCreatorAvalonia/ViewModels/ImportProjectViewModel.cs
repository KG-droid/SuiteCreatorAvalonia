using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    public partial class ImportProjectViewModel : ViewModelBase
    {
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private string? _suiteExePath;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
        private string? _outputFolder;

        // Parameterless constructor for design-time support
        public ImportProjectViewModel()
        {
        }

        partial void OnSuiteExePathChanged(string? value) => SuiteExePath = TrimQuotes(value);

        partial void OnOutputFolderChanged(string? value) => OutputFolder = TrimQuotes(value);

        private static string? TrimQuotes(string? path) =>
            path != null && path.Length >= 2 && path[0] == '"' && path[^1] == '"'
                ? path[1..^1]
                : path;

        [RelayCommand]
        private async Task BrowseSuiteExe()
        {
            IEnumerable<string>? results = await this.OpenFileDialogAsync(new FilePickerOpenOptions()
            {
                AllowMultiple = false,
                Title = "Select a Suite exe",
                FileTypeFilter = SysIOPickerTypes.SuiteExe
            });
            if (results != null && results.Any())
                SuiteExePath = results.First();
        }

        [RelayCommand]
        private async Task BrowseOutputFolder()
        {
            IEnumerable<string>? results = await this.OpenFolderDialogAsync(new FolderPickerOpenOptions()
            {
                AllowMultiple = false,
                Title = "Select a folder to extract the imported project into"
            });
            if (results != null && results.Any())
                OutputFolder = results.First();
        }

        private bool CanImport() =>
            !string.IsNullOrWhiteSpace(SuiteExePath) && !string.IsNullOrWhiteSpace(OutputFolder);

        [RelayCommand(CanExecute = nameof(CanImport))]
        private void Import()
        {
            // Result is collected by the caller via DialogHost close parameter
            DialogHostAvalonia.DialogHost.Close("MainDialogHost", "Import");
        }

        [RelayCommand]
        private void Cancel()
        {
            DialogHostAvalonia.DialogHost.Close("MainDialogHost", "Cancel");
        }
    }
}
