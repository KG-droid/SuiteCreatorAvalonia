using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.Services;
using SuiteTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Architecture = System.Runtime.InteropServices.Architecture;
using RestartBehaviorEnum = SuiteCreatorAvalonia.Enums.RestartBehavior;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageMSIRemoveDetailsViewModel : PackageDetailsBaseViewModel
    {
        private AppSettingsControl _settingsCtrl;
        private bool _isLoading = false;
        private SuiteCoreManager _suiteCoreManager;
        private readonly Dictionary<MSIProperty, PropertyChangedEventHandler> _propertyHandlers = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GenerateLogPathCommand))]
        private string? _msiFilePath;

        [ObservableProperty]
        private string? _removalCode;

        [ObservableProperty]
        private bool _isProductCodeRemoval = false;

        [ObservableProperty]
        private bool _isUserContext = false;

        [ObservableProperty]
        private Architecture _architecture = Architecture.X86;

        [ObservableProperty]
        private bool _isCreateLog = true;

        [ObservableProperty]
        private string? _logPath;

        [ObservableProperty]
        private RestartBehaviorEnum _restartBehavior = RestartBehaviorEnum.Ignore;

        [ObservableProperty]
        private string? _cmdPreview;

        [ObservableProperty]
        private ObservableCollection<MSIProperty> _properties = new();

        [ObservableProperty]
        private string? _newPropertyName;

        [ObservableProperty]
        private string? _newPropertyValue;

        [ObservableProperty]
        private MSIProperty? _selectedProperty;

        partial void OnIsProductCodeRemovalChanged(bool value)
        {
            if (_isLoading) return;
            if (string.IsNullOrWhiteSpace(MsiFilePath) || !Path.Exists(MsiFilePath) || !MsiFilePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                RemovalCode = string.Empty;
            else
                GetMSIRemovalCode(MsiFilePath);
        }

        public PackageMSIRemoveDetailsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new SuiteCoreManager(), new AppSettingsControl())
        {
        }

        public PackageMSIRemoveDetailsViewModel(ViewFactory viewFactory, SuiteCoreManager suiteCoreManager, AppSettingsControl settingsCtrl) : base(viewFactory, suiteCoreManager)
        {
            _settingsCtrl = settingsCtrl;
            _suiteCoreManager = suiteCoreManager;
            Properties.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    foreach (var kv in _propertyHandlers.ToList())
                        kv.Key.PropertyChanged -= kv.Value;
                    _propertyHandlers.Clear();
                }
                else
                {
                    if (e.OldItems != null)
                        foreach (MSIProperty removed in e.OldItems)
                            DetachPropertyHandler(removed);
                    if (e.NewItems != null)
                        foreach (MSIProperty added in e.NewItems)
                            AttachPropertyHandler(added);
                }
                if (!_isLoading)
                    SavePackage();
            };
        }

        private void AttachPropertyHandler(MSIProperty property)
        {
            PropertyChangedEventHandler handler = (s, e) =>
            {
                if (!_isLoading)
                    SavePackage();
            };
            property.PropertyChanged += handler;
            _propertyHandlers[property] = handler;
        }

        private void DetachPropertyHandler(MSIProperty property)
        {
            if (_propertyHandlers.TryGetValue(property, out PropertyChangedEventHandler? handler))
            {
                property.PropertyChanged -= handler;
                _propertyHandlers.Remove(property);
            }
        }

        public override void LoadPackage(Guid packageId)
        {
            _isLoading = true;
            base.LoadPackage(packageId);
            var msiRemovePackage = _suiteCoreManager.GetPackage(packageId) as MSIRemoval;
            if (msiRemovePackage == null) return;
            RemovalCode = msiRemovePackage.RemovalCode;
            IsProductCodeRemoval = msiRemovePackage.IsProductRemoval;
            IsUserContext = msiRemovePackage.Context == Contexts.User;
            Architecture = msiRemovePackage.Architecture;
            IsCreateLog = msiRemovePackage.IsCreateLog;
            LogPath = msiRemovePackage.LogPath;
            RestartBehavior = msiRemovePackage.RestartBehavior;
            Properties.Clear();
            if (msiRemovePackage.Properties != null)
            {
                foreach (MSIProperty property in msiRemovePackage.Properties)
                    Properties.Add(property.Clone());
            }
            RequirementRuleBuilder.Clear();
            if (msiRemovePackage.RequirementRuleSetId is Guid reqGuid)
            {
                RequirementRuleBuilder.LoadRuleSet(_suiteCoreManager.GetRuleSet(reqGuid));
            }
            _isLoading = false;
        }

        public override void SavePackage()
        {
            if (_isLoading || Id is not Guid) return;
            base.SavePackage();
            var packageMSIRemove = GetVerifiedPackage<MSIRemoval>((Guid)Id);
            packageMSIRemove.RemovalCode = RemovalCode;
            packageMSIRemove.IsProductRemoval = IsProductCodeRemoval;
            packageMSIRemove.Context = IsUserContext ? Contexts.User : Contexts.System;
            packageMSIRemove.Architecture = Architecture;
            packageMSIRemove.IsCreateLog = IsCreateLog;
            packageMSIRemove.LogPath = LogPath;
            packageMSIRemove.RestartBehavior = RestartBehavior;
            packageMSIRemove.Properties = Properties.Count > 0 ? Properties.Select(p => p.Clone()).ToList() : null;
            _suiteCoreManager.UpdatePackage(packageMSIRemove);
        }

        private bool CanGenerateLogPath()
        {
            return !string.IsNullOrEmpty(MsiFilePath) && MsiFilePath.EndsWith(".msi", System.StringComparison.OrdinalIgnoreCase) && Path.Exists(MsiFilePath);
        }

        [RelayCommand]
        private void AddProperty()
        {
            if (string.IsNullOrWhiteSpace(NewPropertyName))
                return;
            if (!Properties.Any(p => p.Name.Equals(NewPropertyName, StringComparison.OrdinalIgnoreCase)))
            {
                Properties.Add(new MSIProperty { Name = NewPropertyName, Value = NewPropertyValue });
            }
            else
            {
                MSIProperty existingProperty = Properties.First(p => p.Name.Equals(NewPropertyName, StringComparison.OrdinalIgnoreCase));
                int index = Properties.IndexOf(existingProperty);
                Properties.RemoveAt(index);
                Properties.Insert(index, new MSIProperty { Name = NewPropertyName, Value = NewPropertyValue });
            }
        }

        [RelayCommand]
        private void RemoveProperty()
        {
            if (SelectedProperty != null)
            {
                Properties.Remove(SelectedProperty);
            }
        }

        [RelayCommand]
        private async Task GetMSIRemovalCode(string? msiPath = null)
        {
            Hashtable msiProps;
            if (string.IsNullOrWhiteSpace(msiPath))
            {
                IEnumerable<string>? filePath = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for an MSI", FileTypeFilter = SysIOPickerTypes.MSI });
                if (filePath != null && filePath.Count() > 0)
                    MsiFilePath = filePath.First();
                else
                    return;
                msiProps = MSITools.GetMSIProperties(MsiFilePath);
                msiPath = MsiFilePath;
            }
            else
            {
                msiProps = MSITools.GetMSIProperties(msiPath);
            }
            Architecture = MSITools.GetMSISummaryInfoFromMSI(msiPath).Is64Bit() ? Architecture.X64 : Architecture.X86;
            if (IsProductCodeRemoval)
            {
                RemovalCode = msiProps["ProductCode"]?.ToString();
            }
            else
            {
                RemovalCode = msiProps["UpgradeCode"]?.ToString();
            }
        }


        [RelayCommand(CanExecute = nameof(CanGenerateLogPath))]
        private async Task GenerateLogPath()
        {
            if (string.IsNullOrWhiteSpace(MsiFilePath)) return;
            string logPath = _settingsCtrl.GetLogLocation();
            Hashtable msiProps = MSITools.GetMSIProperties(MsiFilePath);
            LogPath = Path.Combine(logPath, $"{msiProps["Manufacturer"]}_{msiProps["ProductName"]}_{msiProps["ProductVersion"]}_Deploy.log");
        }
    }
}
