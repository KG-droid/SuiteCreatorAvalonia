using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.IDataTemplates;
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
using System.ComponentModel;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageOtherBaseDetailsViewModel : PackageDetailsBaseViewModel
    {
        private readonly Dictionary<MSIRemoveItemViewModel, PropertyChangedEventHandler> _msiRemoveHandlers = new();
        private readonly DispatcherTimer _saveDebounceTimer;
        protected SuiteCoreManager _suiteCoreManager;
        protected bool _isLoading = false;

        [ObservableProperty]
        private FileTreeViewModel _fileTreeVM;

        private ObservableCollection<FileSystemNode> _fileTreeNodes = new ObservableCollection<FileSystemNode> { new FileSystemNode("PackageRoot", new ObservableCollection<FileSystemNode>()) };   

        public ObservableCollection<FileSystemNode> FileTreeNodes
        {
            get => _fileTreeNodes;
        }

        [ObservableProperty]
        private RuleBuilderViewModel _detectionBuilder;

        [ObservableProperty]
        private OtherRemovalType _selectedRemoveType = OtherRemovalType.MSI;

        [ObservableProperty]
        private ObservableCollection<ReturnCode> _customReturnCodes = new();

        [ObservableProperty]
        private bool _isUserContext = false;

        [ObservableProperty]
        private bool _isSecureCMD = false;

        [ObservableProperty]
        private bool _legacyLongFilePath = false;

        [ObservableProperty]
        private bool _isCustomExitCodes = false;

        [ObservableProperty]
        private ObservableCollection<List<VariableText>> _removeCMDs = new();

        [ObservableProperty]
        private ItemsControl _removeCMDsItemsControl = new();

        [ObservableProperty]
        private ObservableCollection<MSIRemoveItemViewModel> _removeMSIs = new();

        [ObservableProperty]
        private MSIRemoveItemViewModel? _selectedRemoveMSIs = new();

        [ObservableProperty]
        private string? _regexVendor;

        [ObservableProperty]
        private string? _regexProductName;

        [ObservableProperty]
        private string? _regexRemoveParams;

        [ObservableProperty]
        private RestartBehavior _restartBehavior = RestartBehavior.Ignore;

        partial void OnSelectedRemoveTypeChanged(OtherRemovalType value)
        {
            switch (value)
            {
                case OtherRemovalType.MSI:
                    if (RemoveMSIs.Count < 1)
                    {
                        AddMSIRemoval();
                    }
                    break;
                case OtherRemovalType.CMD:
                    if (RemoveCMDs.Count < 1)
                    {
                        AddRemoveCMDCommand();
                    }
                    break;
                default:
                    break;
            }
        }

        public PackageOtherBaseDetailsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager())
        {
            PropertyChanged += (sender, args) => SavePackage();
        }

        public PackageOtherBaseDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager) : base(viewFactory, suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
            _saveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _saveDebounceTimer.Tick += (s, e) => { _saveDebounceTimer.Stop(); SavePackage(); };
            DetectionBuilder = (RuleBuilderViewModel)viewFactory.GetVM(typeof(RuleBuilderViewModel));
            FileTreeVM = new FileTreeViewModel(FileTreeNodes);
            RemoveCMDsItemsControl.DataTemplates.Add(new CommandDataTemplate(FileTreeNodes, RemoveCMDs));
            RemoveCMDsItemsControl.ItemsSource = RemoveCMDs;
            RemoveCMDs.Add(new List<VariableText> { new LiteralText(string.Empty) });
            RemoveMSIs.CollectionChanged += (s, e) =>
            {
                if (e.OldItems != null)
                {
                    foreach (MSIRemoveItemViewModel old in e.OldItems)
                        DetachMSIRemoveHandler(old);
                }
                if (e.NewItems != null)
                {
                    foreach (MSIRemoveItemViewModel added in e.NewItems)
                        AttachMSIRemoveHandler(added);
                }
                if (!_isLoading)
                    SavePackage();
            };
            RemoveCMDs.CollectionChanged += (s, e) =>
            {
                if (!_isLoading)
                    SavePackage();
            };
            CustomReturnCodes.CollectionChanged += (s, e) =>
            {
                if (!_isLoading)
                    SavePackage();
            };
            DetectionBuilder.RulesAmended += (s, e) =>
            {
                if (!_isLoading)
                    SavePackage();
            };
        }

        protected void SubscribeToFileTree(ObservableCollection<FileSystemNode> nodes)
        {
            nodes.CollectionChanged += OnFileTreeCollectionChanged;
            foreach (FileSystemNode node in nodes)
            {
                if (node.SubNodes != null)
                    SubscribeToFileTree(node.SubNodes);
            }
        }

        protected void UnsubscribeFromFileTree(ObservableCollection<FileSystemNode> nodes)
        {
            nodes.CollectionChanged -= OnFileTreeCollectionChanged;
            foreach (FileSystemNode node in nodes)
            {
                if (node.SubNodes != null)
                    UnsubscribeFromFileTree(node.SubNodes);
            }
        }

        private void OnFileTreeCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (FileSystemNode node in e.NewItems)
                {
                    if (node.SubNodes != null)
                        SubscribeToFileTree(node.SubNodes);
                }
            }
            if (e.OldItems != null)
            {
                foreach (FileSystemNode node in e.OldItems)
                {
                    if (node.SubNodes != null)
                        UnsubscribeFromFileTree(node.SubNodes);
                }
            }
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        [RelayCommand]
        public void AddCustomReturnCode()
        {
            CustomReturnCodes.Add(new ReturnCode { Action = ActionType.Continue, Code = 0 });
        }

        [RelayCommand]
        public void RemoveCustomReturnCode(ReturnCode code)
        {
            CustomReturnCodes.Remove(code);
        }

        [RelayCommand]
        public void AddMSIRemoval()
        {
            MSIRemoveItemViewModel newMSIRemVM = new() { IsProductRemoval = false };
            RemoveMSIs.Add(newMSIRemVM);
        }

        [RelayCommand]
        public void RemoveMSIRemoval()
        {
            if (SelectedRemoveMSIs != null)
            {
                if (_msiRemoveHandlers.TryGetValue(SelectedRemoveMSIs, out PropertyChangedEventHandler? handler))
                {
                    SelectedRemoveMSIs.PropertyChanged -= handler;
                    _msiRemoveHandlers.Remove(SelectedRemoveMSIs);
                }
                RemoveMSIs.Remove(SelectedRemoveMSIs);
            }
        }

        [RelayCommand]
        public void AddRemoveCMDCommand()
        {
            RemoveCMDs.Add(new List<VariableText> { new LiteralText(string.Empty) });
        }

        public override void LoadPackage(Guid packageId)
        {
            bool wasLoading = _isLoading;
            _isLoading = true;
            try
            {
                base.LoadPackage(packageId);
                OtherCore otherCore = GetVerifiedPackage<OtherCore>(packageId);
                if (otherCore == null || string.IsNullOrWhiteSpace(otherCore.Name)) return;
                IsUserContext = otherCore.Context == Contexts.User;
                IsSecureCMD = otherCore.SecureParams;
                IsCustomExitCodes = otherCore.CustomExitCodes;
                LegacyLongFilePath = otherCore.LegacyLongFilePath;
                CustomReturnCodes.Clear();
                if (otherCore.ExitCodes != null && otherCore.ExitCodes.Count() > 0)
                    CustomReturnCodes.AddRange(otherCore.ExitCodes);
                DetectionBuilder.Clear();
                if (otherCore.DetectionRuleSetId is Guid dectGuid)
                    DetectionBuilder.LoadRuleSet(_suiteCoreManager.GetRuleSet(dectGuid));
                SelectedRemoveType = otherCore.RemovalType;
                RestartBehavior = otherCore.RestartBehavior;
                RegexVendor = null;
                RegexProductName = null;
                RegexRemoveParams = null;
                RemoveMSIs.Clear();
                RemoveCMDs.Clear();
                DetachAllMSIRemoveHandlers();
                switch (otherCore.Removal)
                {
                    case OtherMSIRemoval msiRemoval:
                        if (msiRemoval.MSIRemovalItems != null)
                        {
                            foreach (MSIInstallRemovalItem item in msiRemoval.MSIRemovalItems)
                            {
                                MSIRemoveItemViewModel newMSIRemVM = new MSIRemoveItemViewModel
                                {
                                    AdditionalParams = item.AdditionalParams,
                                    Code = item.Code,
                                    LogPath = item.LogPath,
                                    IsProductRemoval = item.IsProductRemoval
                                };
                                RemoveMSIs.Add(newMSIRemVM);
                            }
                        }
                        break;
                    case OtherRegexRemoval regexRemoval:
                        RegexVendor = regexRemoval.Manufacturer;
                        RegexProductName = regexRemoval.ProductName;
                        RegexRemoveParams = regexRemoval.RemovalParams;
                        break;
                    case OtherCMDRemoval cmdRemoval:
                        if (cmdRemoval.RemoveCommands != null)
                        {
                            RemoveCMDs.AddRange(cmdRemoval.RemoveCommands);
                        }
                        break;
                    default:
                        throw new NotImplementedException("Removal type not implemented.");
                }
            }
            finally
            {
                _isLoading = wasLoading;
            }
        }

        public override void SavePackage()
        {
            if (_isLoading) return;
            base.SavePackage();
            if (Id is not Guid id) return;
            OtherCore otherCore = GetVerifiedPackage<OtherCore>(id);
            otherCore.SecureParams = IsSecureCMD;
            otherCore.Context = IsUserContext ? Contexts.User : Contexts.System;
            otherCore.CustomExitCodes = IsCustomExitCodes;
            otherCore.LegacyLongFilePath = LegacyLongFilePath;
            otherCore.RemovalType = SelectedRemoveType;
            otherCore.RestartBehavior = RestartBehavior;
            switch (SelectedRemoveType)
            {
                case OtherRemovalType.MSI:
                    if (otherCore.Removal is OtherMSIRemoval msiRemoval)
                    {
                        msiRemoval.MSIRemovalItems = RemoveMSIs
                            .Select(vm => new MSIInstallRemovalItem
                            {
                                AdditionalParams = vm.AdditionalParams,
                                Code = vm.Code,
                                LogPath = vm.LogPath,
                                IsProductRemoval = vm.IsProductRemoval
                            })
                            .ToList();
                    }
                    else
                    {
                        otherCore.Removal = new OtherMSIRemoval
                        {
                            MSIRemovalItems = RemoveMSIs
                                .Select(vm => new MSIInstallRemovalItem
                                {
                                    AdditionalParams = vm.AdditionalParams,
                                    Code = vm.Code,
                                    LogPath = vm.LogPath,
                                    IsProductRemoval = vm.IsProductRemoval
                                })
                                .ToList()
                        };
                    }
                    break;
                case OtherRemovalType.CMD:
                    otherCore.Removal = new OtherCMDRemoval
                    {
                        RemoveCommands = RemoveCMDs.ToList()
                    };
                    break;
                case OtherRemovalType.RegEx:
                    otherCore.Removal = new OtherRegexRemoval
                    {
                        Manufacturer = RegexVendor,
                        ProductName = RegexProductName,
                        RemovalParams = RegexRemoveParams
                    };
                    break;
            }
            otherCore.ExitCodes = CustomReturnCodes.ToList();
            IEnumerable<RuleBase>? dectRules = DetectionBuilder.Rules;
            if (dectRules != null && dectRules.Count() > 0)
            {
                RuleSet ruleSet;
                if (otherCore.DetectionRuleSetId is not Guid)
                {
                    ruleSet = _suiteCoreManager.NewRuleSet(Name + " (Pkg Dect)");
                    otherCore.DetectionRuleSetId = ruleSet.Id;
                }
                else
                {
                    ruleSet = _suiteCoreManager.GetRuleSet((Guid)otherCore.DetectionRuleSetId);
                }
                ruleSet.Rules = DetectionBuilder.Rules;
                _suiteCoreManager.UpdateRuleSet(ruleSet);
            }
            else if (otherCore.DetectionRuleSetId is Guid existingGuid)
            {
                otherCore.DetectionRuleSetId = null;
                _suiteCoreManager.UpdatePackage(otherCore);
                try
                {
                    _suiteCoreManager.RemoveRuleSet(existingGuid);
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("Cannot remove RuleSet as it is being used"))
                    {
                        throw;
                    }
                }
            }
            _suiteCoreManager.UpdatePackage(otherCore);
        }

        private void AttachMSIRemoveHandler(MSIRemoveItemViewModel vm)
        {
            PropertyChangedEventHandler handler = (sender, args) => SavePackage();
            vm.PropertyChanged += handler;
            _msiRemoveHandlers[vm] = handler;
        }

        private void DetachMSIRemoveHandler(MSIRemoveItemViewModel vm)
        {
            if (vm == null) return;
            if (_msiRemoveHandlers.TryGetValue(vm, out PropertyChangedEventHandler? handler))
            {
                vm.PropertyChanged -= handler;
                _msiRemoveHandlers.Remove(vm);
            }
        }

        private void DetachAllMSIRemoveHandlers()
        {
            foreach (var kv in _msiRemoveHandlers.ToList())
            {
                kv.Key.PropertyChanged -= kv.Value;
            }
            _msiRemoveHandlers.Clear();
        }
    }

}
