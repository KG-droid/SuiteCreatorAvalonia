using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PowerShellWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private TextDocument _scriptDoc = new TextDocument();

        [ObservableProperty]
        private Contexts? _context = Contexts.System;

        [ObservableProperty]
        private string? _scriptName;

        [ObservableProperty]
        private string? _scriptArgs;

        [ObservableProperty]
        private string? _scriptOutput;

        [ObservableProperty]
        private string? _filePath;

        [ObservableProperty]
        public bool _isScriptOutputVisible = false;

        [ObservableProperty]
        public bool _isScriptRunning = false;

        [ObservableProperty]
        public bool _isFilePathVisible = false;

        [ObservableProperty]
        public bool _hasSupportingFiles = false;

        [ObservableProperty]
        public bool _isRuleMode = false;

        [ObservableProperty]
        public ObservableCollection<FileSystemNode> _supportingFiles = new();

        [ObservableProperty]
        public ObservableCollection<FileSystemNode> _selectedSupportFiles = new();

        partial void OnScriptDocChanged(TextDocument? value)
        {
            ScriptOutput = null;
        }

        partial void OnFilePathChanged(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                IsFilePathVisible = false;
            }
            else
            {
                IsFilePathVisible = true;
                LoadScript();
            }
        }

        partial void OnScriptOutputChanged(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                IsScriptOutputVisible = false;
            }
            else
            {
                IsScriptOutputVisible = true;
            }
        }

        public PowerShellWindowViewModel()
        {
            SupportingFiles.CollectionChanged += (sender, e) =>
            {
                if (SupportingFiles.Any())
                    HasSupportingFiles = true;
                else
                    HasSupportingFiles = false;
            };
        }

        public void LoadScript()
        {
            if (string.IsNullOrWhiteSpace(FilePath))
                return;
            try
            {
                using (FileStream fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
                using (StreamReader reader = new StreamReader(fileStream))
                {
                    string txt = reader.ReadToEnd();
                    ScriptDoc = new TextDocument(txt);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLog.Warning($"Failed to load linked PowerShell script from: {FilePath}, error: {ex.Message}", "PowerShell");
                ScriptOutput = $"Could not load the linked script file: {ex.Message}";
            }
        }

        [RelayCommand]
        public async void ScriptBrowse()
        {
            FilePickerOpenOptions filePickerOptions = new();
            filePickerOptions.Title = "Select a PowerShell Script";
            filePickerOptions.AllowMultiple = false;
            filePickerOptions.FileTypeFilter = SysIOPickerTypes.PowerShell;
            var result = await this.OpenFileDialogAsync(filePickerOptions);
            if (result != null && result.Any())
            {
                FilePath = result.First();
            }
        }

        [RelayCommand]
        public async void UnlinkScript()
        {
            FilePath = null;
        }

        [RelayCommand]
        public void RemoveSelectedSupportFiles()
        {
            if (null != SelectedSupportFiles && SelectedSupportFiles.Any())
            {
                var selected = SelectedSupportFiles.ToList();
                foreach (var item in selected)
                {
                    SupportingFiles.Remove(item);
                }
            }
        }

        [RelayCommand]
        public async void AddSupportingFiles()
        {
            FilePickerOpenOptions filePickerOptions = new();
            filePickerOptions.Title = "Select a PowerShell Script";
            filePickerOptions.AllowMultiple = true;
            filePickerOptions.FileTypeFilter = SysIOPickerTypes.Any;
            var result = await this.OpenFileDialogAsync(filePickerOptions);
            if (result != null && result.Any())
            {
                List<FileSystemNode> list = new();
                foreach (string item in result)
                {
                    if (SupportingFiles.Where(f => f.FullPath == item).Any()) { continue; }
                    string name = Path.GetFileName(item);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(item))
                    {
                        continue;
                    }
                    SupportingFiles.Add(new FileSystemNode(item));
                }
            }
            OnPropertyChanged(nameof(SupportingFiles));
        }

        [RelayCommand]
        public async Task TestPowerShellScript()
        {
            if (ScriptDoc == null || string.IsNullOrWhiteSpace(ScriptDoc.Text))
            {
                return;
            }
            string workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            try
            {
                using (PowerShell powerShell = PowerShell.Create())
                {
                    if (null != SupportingFiles && SupportingFiles.Any())
                    {
                        Directory.CreateDirectory(workingDirectory);
                        foreach (FileSystemNode file in SupportingFiles)
                        {
                            File.Copy(file.FullPath, Path.Combine(workingDirectory, file.Name), true);
                        }
                        powerShell.AddScript($"Set-Location -Path \"{workingDirectory}\"");
                    }
                    powerShell.AddScript(ScriptDoc.Text);
                    try
                    {
                        IsScriptRunning = true;
                        var results = await Task.Factory.FromAsync<PSDataCollection<PSObject>>(
                            powerShell.BeginInvoke(),
                            powerShell.EndInvoke
                        );
                        ScriptOutput = string.Join(Environment.NewLine, results.Select(e => e.BaseObject));
                        if (string.IsNullOrWhiteSpace(ScriptOutput))
                            ScriptOutput = "Successfully executed";
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warning($"PowerShell script test run failed: {ex.Message}", "PowerShell");
                        ScriptOutput = $"An error occurred: {ex.Message}";
                    }
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                IsScriptRunning = false;
                if (Directory.Exists(workingDirectory))
                {
                    Directory.Delete(workingDirectory, true);
                }
            }

        }
    }
}
