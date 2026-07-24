using Avalonia.Labs.Gif;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Tools;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PopupsViewModel : ViewModelBase
    {
        private readonly SuiteCoreManager _suiteCoreManager;
        private readonly AppSettingsControl _uISettingsControl;
        private bool _isLoading = false;

        [ObservableProperty]
        private ViewFactory _viewFact; // Factory for creating view model for the Popups Preview in the PopupsView code behind

        [ObservableProperty]
        private bool _showProgress = false;

        [ObservableProperty]
        private bool _showPopupWarning = false;

        [ObservableProperty]
        private bool _showPopupPreview = true;

        [ObservableProperty]
        private bool _linkToProcClosures = true;

        [ObservableProperty]
        private TextDocument _pSCondition = new();

        [ObservableProperty]
        private bool _hasPopupCondition = false;

        [ObservableProperty]
        private bool _hasInstallPopup = true;

        [ObservableProperty]
        private bool _hasUninstallPopup = true;

        [ObservableProperty]
        private TextDocument _installTxt = new();

        [ObservableProperty]
        private string _instAction = "Upgrade";

        [ObservableProperty]
        private TextDocument _uninstallTxt = new();

        [ObservableProperty]
        private string _uninstAction = "Rollback";

        [ObservableProperty]
        private List<MaterialIconKind> _applicableIconKinds = new List<MaterialIconKind>
        {
            MaterialIconKind.Update,
            MaterialIconKind.CloseCircle,
            MaterialIconKind.Undo,
            MaterialIconKind.PackageVariantRemove,
            MaterialIconKind.PackageVariantPlus,
            MaterialIconKind.PackageVariantMinus,
            MaterialIconKind.PackageDown,
            MaterialIconKind.PackageUp,
            MaterialIconKind.OpenInApp,
            MaterialIconKind.Application,
            MaterialIconKind.ApplicationEdit,
            MaterialIconKind.ApplicationImport,
            MaterialIconKind.ArrowLeft,
            MaterialIconKind.ArrowLeftCircle,
            MaterialIconKind.ArrowLeftThick,
            MaterialIconKind.ArrowULeftTopBold,
            MaterialIconKind.ArrowUpCircle,
            MaterialIconKind.ArrowUpThick,
            MaterialIconKind.ArrowUpCircleOutline,
            MaterialIconKind.MonitorArrowDown,
            MaterialIconKind.MonitorArrowDownVariant,
            MaterialIconKind.RotateLeft,
            MaterialIconKind.RotateRight,
            MaterialIconKind.Restore,
            MaterialIconKind.Reload,
        };

        [ObservableProperty]
        private MaterialIconKind _selectedDeployIconKind = MaterialIconKind.PackageUp;

        [ObservableProperty]
        private MaterialIconKind _selectedRemoveIconKind = MaterialIconKind.Restore;

        [ObservableProperty]
        private int? _timer = 40;

        [ObservableProperty]
        private PopupAction _timerExpireAction = PopupAction.Continue;

        [ObservableProperty]
        private int? _delayDays = 7;

        [ObservableProperty]
        private CompanyLogoPickerView _companyLogoPicker = new();

        [ObservableProperty]
        private Bitmap? _suiteLogo = null;

        [ObservableProperty]
        private string? _suiteLogoBase64 = null;

        [ObservableProperty]
        private string? _suiteLogoError = null;

        [ObservableProperty]
        private PopupAction _loggedOffAction = PopupAction.Continue;

        [ObservableProperty]
        private PopupAction _lockedAction = PopupAction.Continue;

        [ObservableProperty]
        private PopupAction _espAction = PopupAction.Continue;

        [ObservableProperty]
        private SuiteAction _showSampleFor = SuiteAction.Deployment;

        partial void OnHasInstallPopupChanged(bool value)
        {
            SetDefaultShowSampleFor();
        }

        partial void OnHasUninstallPopupChanged(bool value)
        {
            SetDefaultShowSampleFor();
        }

        public PopupsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new AppSettingsControl(), new SuiteCoreManager())
        {
        }

        public PopupsViewModel(ViewFactory viewFactory, AppSettingsControl uISettingsControl, SuiteCoreManager suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
            _uISettingsControl = uISettingsControl;
            ViewFact = viewFactory;
            CompanyLogoPicker.DataContext = viewFactory.GetVM(typeof(CompanyLogoPickerViewModel));
            SetupEvents();
            LoadPopupSettings();
            PropertyChanged += (sender, args) => SavePopupSettings();
        }

        private void SetDefaultShowSampleFor()
        {
            if (HasInstallPopup)
                ShowSampleFor = SuiteAction.Deployment;
            else
                ShowSampleFor = SuiteAction.Removal;
        }

        private void SetupEvents()
        {
            InstallTxt.TextChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(InstallTxt));
            };
            UninstallTxt.TextChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(InstallTxt));
            };
            PSCondition.TextChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(PSCondition));
            };
        }

        [RelayCommand]
        private void AddInstallPop()
        {
            HasInstallPopup = true;
        }

        [RelayCommand]
        private void RemoveInstallPop()
        {
            HasInstallPopup = false;
        }

        [RelayCommand]
        private void AddUninstallPop()
        {
            HasUninstallPopup = true;
        }

        [RelayCommand]
        private void RemoveUninstallPop()
        {
            HasUninstallPopup = false;
        }

        [RelayCommand]
        private async Task ChangeSuiteLogo()
        {
            IEnumerable<string>? result = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Select a Logo", FileTypeFilter = SysIOPickerTypes.ImageIncGif });
            if (result != null && result.Any())
            {
                string filePath = result.First();
                try
                {
                    byte[] bmpBytes = ImageLoader.GetBytes(new Uri(filePath));
                    byte[]? LogoBytes = bmpBytes;
                    MemoryStream ms = new(bmpBytes);
                    Bitmap bitmap = new(ms);
                    SuiteLogo = bitmap;
                    SuiteLogoBase64 = ImageLoader.GetBase64(SuiteLogo);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load suite logo image from: {filePath}", ex, "Popups");
                    Dispatcher.UIThread.Post(() =>
                    {
                        SuiteLogoError = $"Error loading the provided image: {ex.Message}";
                    });
                    return;
                }
            }
        }

        [RelayCommand]
        private void SetDefaultSuiteLogo()
        {
            SuiteLogo = ImageLoader.Get(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/SuiteLogo.png"));
            SuiteLogoBase64 = ImageLoader.GetBase64(SuiteLogo);
        }

        private void LoadPopupSettings()
        {
            _isLoading = true;
            Popup popSettings = _suiteCoreManager.GetPopupSettings();
            ShowProgress = popSettings.ShowProgress;
            ShowPopupWarning = popSettings.ShowPopupWarning;
            LinkToProcClosures = popSettings.LinkToProcClosures;
            // Decode PSCondition from Base64 if possible, else fallback to plain text
            string? psConditionRaw = popSettings.PSCondition;
            if (!string.IsNullOrEmpty(psConditionRaw))
            {
                try
                {
                    byte[] base64Bytes = Convert.FromBase64String(psConditionRaw);
                    string decoded = System.Text.Encoding.UTF8.GetString(base64Bytes);
                    PSCondition.Text = decoded;
                }
                catch (FormatException)
                {
                    // Not valid Base64, fallback to plain text
                    PSCondition.Text = string.Empty;
                }
            }
            else
            {
                PSCondition.Text = string.Empty;
            }
            InstallTxt.Text = popSettings.InstallTxt ?? "";
            UninstallTxt.Text = popSettings.UninstallTxt ?? "";
            Timer = popSettings.Timer > 0 ? popSettings.Timer : 40;
            TimerExpireAction = popSettings.TimerExpireAction ?? PopupAction.Continue;
            DelayDays = popSettings.DelayDays ?? 7;
            LoggedOffAction = popSettings.LoggedOffAction ?? PopupAction.Continue;
            LockedAction = popSettings.LockedAction ?? PopupAction.Continue;
            EspAction = popSettings.ESPAction ?? PopupAction.Continue;
            HasInstallPopup = popSettings.HasInstallTxt;
            HasUninstallPopup = popSettings.HasUninstallTxt;
            SelectedDeployIconKind = popSettings.DeployIconKind;
            SelectedRemoveIconKind = popSettings.RemoveIconKind;
            InstAction = popSettings.InstAction ?? "Upgrade";
            UninstAction = popSettings.UninstAction ?? "Rollback";
            HasPopupCondition = popSettings.HasPSCondition;
            SuiteLogoBase64 = popSettings.SuiteLogoBase64;
            if (string.IsNullOrWhiteSpace(popSettings.SuiteLogoBase64))
                SetDefaultSuiteLogo();
            else
                SuiteLogo = ImageLoader.GetFromBase64(popSettings.SuiteLogoBase64);
            _isLoading = false;
        }

        private void SavePopupSettings()
        {
            if (_isLoading) return; // Prevent saving while loading settings
            // Encode PSCondition.Text as Base64
            string psConditionBase64 = string.Empty;
            if (!string.IsNullOrEmpty(PSCondition.Text))
            {
                byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(PSCondition.Text);
                psConditionBase64 = Convert.ToBase64String(textBytes);
            }
            Popup popSettings = new Popup
            {
                ShowProgress = ShowProgress,
                ShowPopupWarning = ShowPopupWarning,
                LinkToProcClosures = LinkToProcClosures,
                PSCondition = psConditionBase64,
                InstallTxt = InstallTxt.Text,
                UninstallTxt = UninstallTxt.Text,
                Timer = Timer != null ? (int)Timer : 40,
                TimerExpireAction = TimerExpireAction,
                DelayDays = DelayDays != null ? (int)DelayDays : 7,
                LoggedOffAction = LoggedOffAction,
                LockedAction = LockedAction,
                ESPAction = EspAction,
                DeployIconKind = SelectedDeployIconKind,
                HasInstallTxt = HasInstallPopup,
                HasUninstallTxt = HasUninstallPopup,
                RemoveIconKind = SelectedRemoveIconKind,
                InstAction = InstAction,
                UninstAction = UninstAction,
                HasPSCondition = HasPopupCondition,
                SuiteLogoBase64 = SuiteLogoBase64, // Saving takes too long if we dont cache this base64
            };
            _suiteCoreManager.UpdatePopupSettings(popSettings);
        }
    }
}
