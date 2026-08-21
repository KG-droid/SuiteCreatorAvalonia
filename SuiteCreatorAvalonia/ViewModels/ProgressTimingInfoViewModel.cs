using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Services;

namespace SuiteCreatorAvalonia.ViewModels
{
    // Shown once when an admin flips on the Progress popup (Popups page), pointing them at the
    // per-package "Estimated Duration" fields that now control how the progress bar animates
    // during a package's install/uninstall - those fields are otherwise hidden until this is enabled.
    internal partial class ProgressTimingInfoViewModel : ViewModelBase
    {
        [RelayCommand]
        private void GoToPackages()
        {
            this.CloseDialog();
            AppNavigationService.Instance.NavigateToPackagesTab?.Invoke();
        }

        [RelayCommand]
        private void Close()
        {
            this.CloseDialog();
        }
    }
}
