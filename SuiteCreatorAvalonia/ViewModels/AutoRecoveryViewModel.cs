using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogHostAvalonia;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace SuiteCreatorAvalonia.ViewModels
{
    public partial class AutoRecoveryEntryViewModel : ViewModelBase
    {
        public string FilePath { get; }

        [ObservableProperty]
        public string _projectName = "Untitled Suite";

        [ObservableProperty]
        public string _lastModified = string.Empty;

        private readonly Action<AutoRecoveryEntryViewModel> _deleteCallback;

        public AutoRecoveryEntryViewModel(string filePath, Action<AutoRecoveryEntryViewModel> deleteCallback)
        {
            FilePath = filePath;
            _deleteCallback = deleteCallback;
            ProjectName = SuiteCoreManager.PeekProjectName(filePath);
            LastModified = File.GetLastWriteTime(filePath).ToString("dd MMM yyyy, HH:mm:ss");
        }

        [RelayCommand]
        private void Delete() => _deleteCallback(this);
    }

    public partial class AutoRecoveryViewModel : ViewModelBase
    {
        public ObservableCollection<AutoRecoveryEntryViewModel> Entries { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor("RestoreCommand")]
        public AutoRecoveryEntryViewModel? _selectedEntry;

        public AutoRecoveryViewModel(System.Collections.Generic.IReadOnlyList<string> autoSavePaths)
        {
            foreach (string path in autoSavePaths)
                Entries.Add(new AutoRecoveryEntryViewModel(path, DeleteEntry));
        }

        private void DeleteEntry(AutoRecoveryEntryViewModel entry)
        {
            SuiteCoreManager.DeleteAutoSaveAt(entry.FilePath);
            Entries.Remove(entry);
            if (SelectedEntry == entry)
                SelectedEntry = null;
            if (Entries.Count == 0)
                DialogHost.Close("MainDialogHost", null);
        }

        private bool CanRestore() => SelectedEntry is not null;

        [RelayCommand(CanExecute = nameof(CanRestore))]
        private void Restore()
        {
            DialogHost.Close("MainDialogHost", SelectedEntry);
        }

        [RelayCommand]
        private void DiscardAll()
        {
            DialogHost.Close("MainDialogHost", null);
        }

        [RelayCommand]
        private void RemindNextTime()
        {
            DialogHost.Close("MainDialogHost", AutoRecoveryRemindResult.Instance);
        }
    }

    /// <summary>Sentinel result indicating the user wants to be reminded again on next launch.</summary>
    public sealed class AutoRecoveryRemindResult
    {
        public static readonly AutoRecoveryRemindResult Instance = new();
        private AutoRecoveryRemindResult() { }
    }
}
