using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageOtherRemoveDetailsViewModel : PackageOtherBaseDetailsViewModel
    {
        [ObservableProperty]
        private ObservableCollection<FileSystemNode> _powerShellFiles = new();

        [ObservableProperty]
        private FileSystemNode? _selectedPowerShellFile;

        [ObservableProperty]
        private string? _powerShellScriptArgs;

        public PackageOtherRemoveDetailsViewModel()
        {
        }

        public PackageOtherRemoveDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager) : base(viewFactory, suiteCoreManager)
        {
            SetupEvents();
        }

        partial void OnSelectedPowerShellFileChanged(FileSystemNode? value)
        {
            if (!_isLoading)
                SavePackage();
        }

        partial void OnPowerShellScriptArgsChanged(string? value)
        {
            if (!_isLoading)
                SavePackage();
        }

        private void SetupEvents()
        {
            SubscribeToFileTreeRecursive(FileTreeNodes);
        }

        private void SubscribeToFileTreeRecursive(ObservableCollection<FileSystemNode> nodes)
        {
            nodes.CollectionChanged += OnRemoveFileTreeChanged;
            foreach (FileSystemNode node in nodes)
            {
                if (node.SubNodes != null)
                    SubscribeToFileTreeRecursive(node.SubNodes);
            }
        }

        private void OnRemoveFileTreeChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (FileSystemNode node in e.NewItems)
                {
                    if (node.SubNodes != null)
                        SubscribeToFileTreeRecursive(node.SubNodes);
                }
            }
            if (!_isLoading)
                RefreshPowerShellFiles();
        }

        private void RefreshPowerShellFiles()
        {
            string? previousPath = SelectedPowerShellFile?.FullPath;
            PowerShellFiles.Clear();
            foreach (FileSystemNode file in GetFlatFiles(FileTreeNodes).Where(f => f.FullPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)))
            {
                PowerShellFiles.Add(file);
            }
            if (previousPath != null)
                SelectedPowerShellFile = PowerShellFiles.FirstOrDefault(f => f.FullPath == previousPath);
        }

        private IEnumerable<FileSystemNode> GetFlatFiles(IEnumerable<FileSystemNode> nodes)
        {
            foreach (FileSystemNode node in nodes)
            {
                if (node.IsFile)
                    yield return node;
                else if (node.SubNodes != null)
                    foreach (FileSystemNode child in GetFlatFiles(node.SubNodes))
                        yield return child;
            }
        }

        [RelayCommand]
        public async System.Threading.Tasks.Task BrowsePowerShellScript()
        {
            IEnumerable<string>? results = await this.OpenFileDialogAsync(new FilePickerOpenOptions()
            {
                AllowMultiple = false,
                Title = "Select a PowerShell script",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("PowerShell Scripts") { Patterns = new[] { "*.ps1" } }
                }
            });
            if (results == null || !results.Any()) return;
            string path = results.First();
            FileTreeVM.ImportFiles(new[] { path });
            RefreshPowerShellFiles();
            SelectedPowerShellFile = PowerShellFiles.FirstOrDefault(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        public override void LoadPackage(Guid packageId)
        {
            _isLoading = true;
            try
            {
                base.LoadPackage(packageId);
                OtherRemovalPkg otherRemovePkg = GetVerifiedPackage<OtherRemovalPkg>(packageId);
                if (otherRemovePkg == null || string.IsNullOrWhiteSpace(otherRemovePkg.Name)) return;
                UnsubscribeFromFileTree(FileTreeNodes);
                FileSystemNode packageRoot = FileTreeNodes.FirstOrDefault() ?? new FileSystemNode("PackageRoot", new ObservableCollection<FileSystemNode>());
                packageRoot.SubNodes?.Clear();
                FileTreeNodes.Clear();
                if (otherRemovePkg.Files != null && otherRemovePkg.Files.Count() > 0)
                {
                    FileTreeNodes.AddRange(otherRemovePkg.Files);
                }
                else
                {
                    FileTreeNodes.Add(packageRoot);
                }
                SubscribeToFileTree(FileTreeNodes);
                PowerShellFiles.Clear();
                foreach (FileSystemNode file in GetFlatFiles(FileTreeNodes).Where(f => f.FullPath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)))
                {
                    PowerShellFiles.Add(file);
                }
                SelectedRemoveType = otherRemovePkg.RemovalType;
                SelectedPowerShellFile = !string.IsNullOrWhiteSpace(otherRemovePkg.PowerShellScriptPath)
                    ? PowerShellFiles.FirstOrDefault(f => f.FullPath == otherRemovePkg.PowerShellScriptPath)
                    : null;
                PowerShellScriptArgs = otherRemovePkg.PowerShellScriptArgs;
                RequirementRuleBuilder.Clear();
                if (otherRemovePkg.RequirementRuleSetId is Guid reqGuid)
                {
                    RequirementRuleBuilder.LoadRuleSet(_suiteCoreManager.GetRuleSet(reqGuid));
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        public override void SavePackage()
        {
            if (_isLoading || Id is not Guid) return;
            base.SavePackage();
            OtherRemovalPkg otherPkg = GetVerifiedPackage<OtherRemovalPkg>((Guid)Id);
            otherPkg.PowerShellScriptPath = SelectedRemoveType == OtherRemovalType.PowerShell
                ? SelectedPowerShellFile?.FullPath
                : null;
            otherPkg.PowerShellScriptArgs = SelectedRemoveType == OtherRemovalType.PowerShell
                ? PowerShellScriptArgs
                : null;
            otherPkg.Files = FileTreeNodes.ToList();
            _suiteCoreManager.UpdatePackage(otherPkg);
        }
    }
}