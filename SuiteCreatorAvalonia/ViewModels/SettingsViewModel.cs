using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class SettingsViewModel : ViewModelBase
    {
        private bool _isLoading = false;
        private AppSettingsControl _settingsCtrl;
        private ThemeManager _themeCtrl;

        [ObservableProperty]
        private bool _manualPaneControl;

        [ObservableProperty]
        private ThemeVariant _theme;

        [ObservableProperty]
        private List<ThemeVariant> _themes;

        [ObservableProperty]
        private Color _darkAccentColour;

        [ObservableProperty]
        private Color _lightAccentColour;

        [ObservableProperty]
        private CompanyLogoPickerView _companyLogoPicker = new();

        [ObservableProperty]
        private string? _logLocation;

        [ObservableProperty]
        private TextDocument _globalPopupCondition = new();

        // Parameterless constructor for design-time support
        public SettingsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new AppSettingsControl(), new ThemeManager())
        {
        }

        public SettingsViewModel(ViewFactory viewFactory, AppSettingsControl settingsCtrl, ThemeManager themeManager)
        {
            Themes = new List<ThemeVariant> { ThemeVariant.Default, ThemeVariant.Light, ThemeVariant.Dark };
            Theme = Themes.First();
            _settingsCtrl = settingsCtrl;
            _themeCtrl = themeManager;
            CompanyLogoPicker.DataContext = viewFactory.GetVM(typeof(CompanyLogoPickerViewModel));
            LoadSettings();
            PropertyChanged += async (sender, e) =>
            {
                await SaveSettings();
            };
        }

        private void LoadSettings()
        {
            _isLoading = true;
            ManualPaneControl = _settingsCtrl.GetIsManualPaneControl();
            Theme = _settingsCtrl.GetTheme();
            LightAccentColour = _settingsCtrl.GetLightAccentColor();
            DarkAccentColour = _settingsCtrl.GetDarkAccentColor();
            LogLocation = _settingsCtrl.GetLogLocation();
            string? globalConditionRaw = _settingsCtrl.GetGlobalPopupCondition();
            if (!string.IsNullOrEmpty(globalConditionRaw))
            {
                try
                {
                    byte[] base64Bytes = Convert.FromBase64String(globalConditionRaw);
                    GlobalPopupCondition.Text = System.Text.Encoding.UTF8.GetString(base64Bytes);
                }
                catch (FormatException)
                {
                    GlobalPopupCondition.Text = string.Empty;
                }
            }
            else
            {
                GlobalPopupCondition.Text = string.Empty;
            }
            _isLoading = false;
        }

        private async Task SaveSettings()
        {
            if (_isLoading) { return; }
            _themeCtrl.SetTheme(Theme);
            _settingsCtrl.UpdateTheme(Theme);
            _settingsCtrl.UpdateIsManualPaneControl(ManualPaneControl);
            _settingsCtrl.UpdateLightAccentColor(LightAccentColour);
            _settingsCtrl.UpdateDarkAccentColor(DarkAccentColour);
            await _settingsCtrl.SaveAsync();
        }

        // Hidden admin entry point (Ctrl+Shift+A on this page, see SettingsView.axaml) for building the
        // install-directory AppSettings.json that makes settings like the Global Popup Condition admin-controlled.
        [RelayCommand]
        private async Task OpenAdminExport()
        {
            await this.ShowDialogAsync(new AdminExportViewModel());
        }
    }
}
