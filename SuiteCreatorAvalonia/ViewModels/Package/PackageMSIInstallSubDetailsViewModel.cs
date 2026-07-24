using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Services;
using SuiteTools;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RestartBehaviorEnum = SuiteCreatorAvalonia.Enums.RestartBehavior;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PackageMSIInstallSubDetailsViewModel : ViewModelBase
    {
        private AppSettingsControl _settingsCtrl;
        private readonly Dictionary<MSIProperty, PropertyChangedEventHandler> _propertyHandlers = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(GenerateLogPathCommand))]
        private string? _msiFilePath;

        [ObservableProperty]
        private bool _isUserContext = false;

        [ObservableProperty]
        private bool _IncludeAdjacentFilesFolders = false;

        [ObservableProperty]
        private bool _isRemoveFamily = true;

        [ObservableProperty]
        private bool _isCreateLog = true;

        [ObservableProperty]
        private string? _logPath;

        [ObservableProperty]
        private ObservableCollection<MSIProperty> _properties = new();

        [ObservableProperty]
        private string? _newPropertyName;

        [ObservableProperty]
        private string? _newPropertyValue;

        [ObservableProperty]
        private MSIProperty? _selectedProperty;

        [ObservableProperty]
        private string? _transformFile;

        [ObservableProperty]
        private string? _patchFile;

        [ObservableProperty]
        private bool _secureTransforms;

        [ObservableProperty]
        private string? _cmdPreview;

        [ObservableProperty]
        private bool _legacyLongFilePath;

        [ObservableProperty]
        private RestartBehaviorEnum _restartBehavior = RestartBehaviorEnum.Ignore;

        [ObservableProperty]
        private string? _productCode;

        [ObservableProperty]
        private string? _upgradeCode;

        [ObservableProperty]
        private string? _detectionPreview;

        public PackageMSIInstallSubDetailsViewModel()
        {
        }

        public PackageMSIInstallSubDetailsViewModel(AppSettingsControl settingsCtrl)
        {
            _settingsCtrl = settingsCtrl;
            PropertyChanged += (sender, args) =>
            {
                if (!args.PropertyName.Equals("NewPropertyName", System.StringComparison.OrdinalIgnoreCase) &&
                !args.PropertyName.Equals("NewPropertyValue", System.StringComparison.OrdinalIgnoreCase))
                    ContructPreviewCMD(args);

                if (args.PropertyName == nameof(MsiFilePath))
                    RefreshMsiCodes();

                if (args.PropertyName == nameof(MsiFilePath) || args.PropertyName == nameof(IsRemoveFamily))
                    RefreshDetectionPreview();
            };
            Properties.CollectionChanged += (sender, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset)
                {
                    foreach (var kv in _propertyHandlers.ToList())
                        kv.Key.PropertyChanged -= kv.Value;
                    _propertyHandlers.Clear();
                }
                else
                {
                    if (args.OldItems != null)
                        foreach (MSIProperty removed in args.OldItems)
                            DetachPropertyHandler(removed);
                    if (args.NewItems != null)
                        foreach (MSIProperty added in args.NewItems)
                            AttachPropertyHandler(added);
                }
                ContructPreviewCMD(new PropertyChangedEventArgs("Properties"));
            };
        }

        private void AttachPropertyHandler(MSIProperty property)
        {
            PropertyChangedEventHandler handler = (s, e) => OnPropertyChanged(nameof(Properties));
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

        private void RefreshMsiCodes()
        {
            if (string.IsNullOrWhiteSpace(MsiFilePath) || !Path.Exists(MsiFilePath))
            {
                ProductCode = null;
                UpgradeCode = null;
                return;
            }
            try
            {
                Hashtable msiProps = MSITools.GetMSIProperties(MsiFilePath);
                ProductCode = msiProps["ProductCode"]?.ToString();
                UpgradeCode = msiProps["UpgradeCode"]?.ToString();
            }
            catch
            {
                ProductCode = null;
                UpgradeCode = null;
            }
        }

        private void RefreshDetectionPreview()
        {
            if (string.IsNullOrWhiteSpace(MsiFilePath))
            {
                DetectionPreview = null;
                return;
            }
            DetectionPreview = IsRemoveFamily
                ? $"Detects whether any product in the same upgrade family is installed, based on UpgradeCode {UpgradeCode ?? "(unknown)"}."
                : $"Detects whether this specific product is installed, based on ProductCode {ProductCode ?? "(unknown)"}.";
        }

        private void ContructPreviewCMD(PropertyChangedEventArgs? args)
        {
            if (args != null &&
                args.PropertyName != null &&
                !string.IsNullOrWhiteSpace(MsiFilePath) &&
                (
                    args.PropertyName == nameof(MsiFilePath) ||
                    args.PropertyName == nameof(LogPath) ||
                    args.PropertyName == nameof(TransformFile) ||
                    args.PropertyName == nameof(PatchFile) ||
                    args.PropertyName == nameof(Properties)
                )
            )
            {
                CmdPreview = $"msiexec.exe /i \"{MsiFilePath}\" /q" +
                    $"{(!string.IsNullOrWhiteSpace(TransformFile) ? $" TRANSFORMS=\"{TransformFile}\"" : "")}" +
                    $"{(!string.IsNullOrWhiteSpace(PatchFile) ? $" /update \"{PatchFile}\"" : "")}" +
                    $"{(Properties.Count > 0 ? string.Join(" ", Properties.Select(p => $" {p.Name}=\"{p.Value}\"")) : "")}" +
                    $"{(!string.IsNullOrWhiteSpace(LogPath) ? $" /l*v \"{LogPath}\"" : "")}";
            }
        }

        [RelayCommand]
        private void AddProperty()
        {
            if (string.IsNullOrWhiteSpace(NewPropertyName))
                return;
            if (!Properties.Any(p => p.Name.Equals(NewPropertyName, System.StringComparison.OrdinalIgnoreCase)))
            {
                Properties.Add(new MSIProperty { Name = NewPropertyName, Value = NewPropertyValue });
            }
            else
            {
                MSIProperty existingProperty = Properties.First(p => p.Name.Equals(NewPropertyName, System.StringComparison.OrdinalIgnoreCase));
                existingProperty.Value = NewPropertyValue;
                int index = Properties.IndexOf(existingProperty);
                Properties.RemoveAt(index);
                Properties.Insert(index, new MSIProperty { Name = NewPropertyName, Value = NewPropertyValue });
            }
            ContructPreviewCMD(new PropertyChangedEventArgs("Properties"));
        }

        [RelayCommand]
        private void RemoveProperty()
        {
            if (SelectedProperty != null)
                Properties.Remove(SelectedProperty);
        }

        [RelayCommand]
        private async Task BrowseForMSI()
        {
            IEnumerable<string>? filePath = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for an MSI", FileTypeFilter = SysIOPickerTypes.MSI });
            if (filePath != null && filePath.Count() > 0)
                MsiFilePath = filePath.First();
        }

        [RelayCommand]
        private async Task BrowseForPatch()
        {
            IEnumerable<string>? filePath = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for a patch", FileTypeFilter = SysIOPickerTypes.MSP });
            if (filePath != null && filePath.Count() > 0)
                PatchFile = filePath.First();
        }

        [RelayCommand]
        private async Task BrowseForTransform()
        {
            IEnumerable<string>? filePath = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for a transform", FileTypeFilter = SysIOPickerTypes.MST });
            if (filePath != null && filePath.Count() > 0)
                TransformFile = filePath.First();
        }

        private bool CanGenerateLogPath()
        {
            return !string.IsNullOrEmpty(MsiFilePath) && MsiFilePath.EndsWith(".msi", System.StringComparison.OrdinalIgnoreCase) && Path.Exists(MsiFilePath);
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
