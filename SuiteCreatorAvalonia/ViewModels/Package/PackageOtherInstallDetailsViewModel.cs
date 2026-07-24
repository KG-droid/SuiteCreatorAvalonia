using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageOtherInstallDetailsViewModel : PackageOtherBaseDetailsViewModel
    {
        private PathVarsTextBoxViewModel _installCMDView;
        public PathVarsTextBoxViewModel InstallCMDView
        {
            get => _installCMDView;
        }

        [ObservableProperty]
        private OtherInstallType _selectedInstallType = OtherInstallType.CMD;

        [ObservableProperty]
        private OtherInstallType _selectedRollbackType = OtherInstallType.CMD;

        [ObservableProperty]
        private ObservableCollection<FileSystemNode> _powerShellFiles = new();

        [ObservableProperty]
        private FileSystemNode? _selectedPowerShellFile;

        [ObservableProperty]
        private string? _powerShellScriptArgs;

        [ObservableProperty]
        private FileSystemNode? _selectedRollbackPowerShellFile;

        [ObservableProperty]
        private string? _rollbackPowerShellScriptArgs;

        [ObservableProperty]
        private FileSystemNode? _selectedRemovePowerShellFile;

        [ObservableProperty]
        private string? _removePowerShellScriptArgs;

        [ObservableProperty]
        private bool _addRollBack = false;

        private PathVarsTextBoxViewModel _rollbackCMDView;
        public PathVarsTextBoxViewModel RollbackCMDView
        {
            get => _rollbackCMDView;
        }

        [ObservableProperty]
        private RuleBuilderViewModel _rollbackDetectionBuilder;

        public PackageOtherInstallDetailsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
        }

        public PackageOtherInstallDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager) : base(viewFactory, suiteCoreManager)
        {
            RollbackDetectionBuilder = (RuleBuilderViewModel)viewFactory.GetVM(typeof(RuleBuilderViewModel));
            SetupEvents();
        }

        partial void OnSelectedInstallTypeChanged(OtherInstallType value)
        {
            if (!_isLoading)
                SavePackage();
        }

        partial void OnSelectedRollbackTypeChanged(OtherInstallType value)
        {
            if (!_isLoading)
                SavePackage();
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

        partial void OnSelectedRollbackPowerShellFileChanged(FileSystemNode? value)
        {
            if (!_isLoading)
                SavePackage();
        }

        partial void OnRollbackPowerShellScriptArgsChanged(string? value)
        {
            if (!_isLoading)
                SavePackage();
        }

        partial void OnSelectedRemovePowerShellFileChanged(FileSystemNode? value)
        {
            if (!_isLoading)
                SavePackage();
        }

        partial void OnRemovePowerShellScriptArgsChanged(string? value)
        {
            if (!_isLoading)
                SavePackage();
        }

        private void SetupEvents()
        {
            _rollbackCMDView = new PathVarsTextBoxViewModel(FileTreeNodes);
            _rollbackCMDView.CMDUpdated += (s, e) =>
            {
                if (!_isLoading)
                    SavePackage();
            };
            _installCMDView = new PathVarsTextBoxViewModel(FileTreeNodes);
            _installCMDView.CMDUpdated += (s, e) =>
            {
                if (!_isLoading)
                    SavePackage();
            };
            RollbackDetectionBuilder.RulesAmended += (s, e) =>
            {
                if (!_isLoading)
                    SavePackage();
            };
            FileTreeNodes.CollectionChanged += OnInstallFileTreeChanged;
        }

        private void OnInstallFileTreeChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
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
                OtherPkg otherPkg = GetVerifiedPackage<OtherPkg>(packageId);
                if (otherPkg == null || string.IsNullOrWhiteSpace(otherPkg.Name)) return;
                if (otherPkg.RollbackDetectionRuleSetId is Guid rolllbackDectGuid)
                    RollbackDetectionBuilder.LoadRuleSet(_suiteCoreManager.GetRuleSet(rolllbackDectGuid));
                UnsubscribeFromFileTree(FileTreeNodes);
                FileSystemNode packageRoot = FileTreeNodes.FirstOrDefault() ?? new FileSystemNode("PackageRoot", new ObservableCollection<FileSystemNode>());
                packageRoot.SubNodes?.Clear();
                FileTreeNodes.Clear();
                if (otherPkg.Files != null && otherPkg.Files.Count() > 0)
                {
                    FileTreeNodes.AddRange(otherPkg.Files);
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
                SelectedInstallType = otherPkg.InstallType;
                SelectedPowerShellFile = !string.IsNullOrWhiteSpace(otherPkg.PowerShellScriptPath)
                    ? PowerShellFiles.FirstOrDefault(f => f.FullPath == otherPkg.PowerShellScriptPath)
                    : null;
                PowerShellScriptArgs = otherPkg.PowerShellScriptArgs;
                SelectedRollbackType = otherPkg.RollbackInstallType;
                SelectedRollbackPowerShellFile = !string.IsNullOrWhiteSpace(otherPkg.RollbackPowerShellScriptPath)
                    ? PowerShellFiles.FirstOrDefault(f => f.FullPath == otherPkg.RollbackPowerShellScriptPath)
                    : null;
                RollbackPowerShellScriptArgs = otherPkg.RollbackPowerShellScriptArgs;
                SelectedRemovePowerShellFile = !string.IsNullOrWhiteSpace(otherPkg.RemovePowerShellScriptPath)
                    ? PowerShellFiles.FirstOrDefault(f => f.FullPath == otherPkg.RemovePowerShellScriptPath)
                    : null;
                RemovePowerShellScriptArgs = otherPkg.RemovePowerShellScriptArgs;
                RollbackCMDView.VariablePath.Clear();
                bool hasCmdRollback = otherPkg.RollbackCommand != null && (otherPkg.RollbackCommand.Count >= 2 || otherPkg.RollbackCommand.OfType<LiteralText>().Any(cmd => !string.IsNullOrEmpty(cmd.Value)));
                bool hasPowerShellRollback = otherPkg.RollbackInstallType == OtherInstallType.PowerShell && !string.IsNullOrWhiteSpace(otherPkg.RollbackPowerShellScriptPath);
                if (!hasCmdRollback && !hasPowerShellRollback)
                {
                    AddRollBack = false;
                }
                else
                {
                    AddRollBack = true;
                    if (otherPkg.RollbackCommand != null)
                        RollbackCMDView.VariablePath.AddRange(otherPkg.RollbackCommand);
                }
                InstallCMDView.VariablePath.Clear();
                if (otherPkg.InstallCommand != null)
                    InstallCMDView.VariablePath.AddRange(otherPkg.InstallCommand);
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
            OtherPkg otherPkg = GetVerifiedPackage<OtherPkg>((Guid)Id);
            otherPkg.InstallType = SelectedInstallType;
            otherPkg.PowerShellScriptPath = SelectedInstallType == OtherInstallType.PowerShell
                ? SelectedPowerShellFile?.FullPath
                : null;
            otherPkg.PowerShellScriptArgs = SelectedInstallType == OtherInstallType.PowerShell
                ? PowerShellScriptArgs
                : null;
            otherPkg.RollbackInstallType = SelectedRollbackType;
            otherPkg.RollbackPowerShellScriptPath = SelectedRollbackType == OtherInstallType.PowerShell
                ? SelectedRollbackPowerShellFile?.FullPath
                : null;
            otherPkg.RollbackPowerShellScriptArgs = SelectedRollbackType == OtherInstallType.PowerShell
                ? RollbackPowerShellScriptArgs
                : null;
            otherPkg.RemovePowerShellScriptPath = SelectedRemoveType == OtherRemovalType.PowerShell
                ? SelectedRemovePowerShellFile?.FullPath
                : null;
            otherPkg.RemovePowerShellScriptArgs = SelectedRemoveType == OtherRemovalType.PowerShell
                ? RemovePowerShellScriptArgs
                : null;
            otherPkg.InstallCommand = InstallCMDView.VariablePath.ToList();
            if (AddRollBack)
            {
                otherPkg.RollbackCommand = SelectedRollbackType == OtherInstallType.CMD
                    ? RollbackCMDView.VariablePath.ToList()
                    : null;
            }
            else
            {
                otherPkg.RollbackCommand = null;
            }
            otherPkg.Files = FileTreeNodes.ToList();
            IEnumerable<RuleBase>? rollbackDectRules = RollbackDetectionBuilder.Rules;
            if (rollbackDectRules != null && rollbackDectRules.Count() > 0)
            {
                RuleSet ruleSet;
                if (otherPkg.RollbackDetectionRuleSetId is not Guid)
                {
                    ruleSet = _suiteCoreManager.NewRuleSet(Name + " (Pkg Rollback Dect)");
                    otherPkg.RollbackDetectionRuleSetId = ruleSet.Id;
                }
                else
                {
                    ruleSet = _suiteCoreManager.GetRuleSet((Guid)otherPkg.RollbackDetectionRuleSetId);
                }
                ruleSet.Rules = RollbackDetectionBuilder.Rules;
                _suiteCoreManager.UpdateRuleSet(ruleSet);
            }
            else if (otherPkg.RollbackDetectionRuleSetId is Guid existingGuid)
            {
                otherPkg.RollbackDetectionRuleSetId = null;
                _suiteCoreManager.UpdatePackage(otherPkg);
                try
                {
                    _suiteCoreManager.RemoveRuleSet(existingGuid);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Cannot remove Rollback detection RuleSet as it is being used"))
                    {
                        // Dont delete the rule its still being used other events
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            _suiteCoreManager.UpdatePackage(otherPkg);
        }
    }
}