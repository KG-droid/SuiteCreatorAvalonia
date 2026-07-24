using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Services;

namespace SuiteCreatorAvalonia.ViewModels
{
    // Admin-controlled only - the logo and background colour are set via the install directory's
    // AppSettings.json (see the Ctrl+Shift+A admin export dialog on the Settings page), not editable here.
    internal partial class CompanyLogoPickerViewModel : ViewModelBase
    {
        private readonly AppSettingsControl _settingsCtrl;

        [ObservableProperty]
        private Control? _companyLogoCtrl;

        [ObservableProperty]
        private Color _companyLogoBackgroundColor = Colors.DarkBlue;

        // Parameterless constructor for design-time support
        public CompanyLogoPickerViewModel() : this(new AppSettingsControl())
        {
        }

        public CompanyLogoPickerViewModel(AppSettingsControl settingsCtrl)
        {
            _settingsCtrl = settingsCtrl;
            CompanyLogoBackgroundColor = _settingsCtrl.GetCompanyLogoBackgroundColor();
            CompanyLogoCtrl = _settingsCtrl.GetCompanyLogo();
        }
    }
}
