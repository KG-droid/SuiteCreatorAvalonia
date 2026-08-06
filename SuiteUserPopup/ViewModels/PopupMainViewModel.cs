using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SuiteUserPopup.Helpers;
using SuiteUserPopup.Models;
using SuiteUserPopup.Services;
using System;
using System.IO;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteUserPopup.ViewModels
{
    internal partial class PopupMainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ViewModelBase _transitioningView;

        [ObservableProperty]
        private Bitmap? _icon;

        [ObservableProperty]
        private bool _hasOtherPopupTypes = false;

        [ObservableProperty]
        private string _Name = "ExampleApp";

        [ObservableProperty]
        private string _version = "9.9.9";

        [ObservableProperty]
        private string _action = "Upgrade";

        [ObservableProperty]
        private MaterialIconKind _actionIconKind = MaterialIconKind.Update;

        public PopupMainViewModel() : this(Path.Combine(AppContext.BaseDirectory, "CompanyLogo.png"))
        {
        }

        public PopupMainViewModel(string suiteLogoPath)
        {
            TransitioningView = new PopupWarningViewModel();
            LoadAppLogo(suiteLogoPath);
            ApplyConfig();
            LogInfo("Popup main view model initialized.");
        }

        private void LoadAppLogo(string filePath)
        {
            if (File.Exists(filePath))
            {
                using var fs = File.OpenRead(filePath);
                Icon = new Bitmap(fs);
                ApplyIconOutlineIfNeeded(filePath);
                return;
            }
            else
            {
                LogError($"Suite logo file not found: {filePath}");
                Console.WriteLine($"Suite logo file not found: {filePath}");
                ExitApplication(5);
            }
        }

        private void ApplyIconOutlineIfNeeded(string suiteLogoPath)
        {
            // This icon sits directly on the plain card background (SystemAltHighColor), unlike the
            // company logo tab which always has its own accent-coloured backdrop, so outline against
            // the theme background here instead. The suite's own accent colour isn't a safe choice for
            // the stroke itself, since it can just as easily be as dark as the background; pick black
            // or white based on the background's own luminance so it always reads.
            Color themeBackgroundColour = ThemeResourceHelper.ResolveColour("SystemAltHighColor") ?? Colors.Black;

            if (!LogoContrastHelper.NeedsOutline(suiteLogoPath, themeBackgroundColour))
                return;

            Color outlineColour = ThemeResourceHelper.GetReadableForeground(themeBackgroundColour);
            Bitmap? outlined = LogoContrastHelper.CreateLogoWithOutline(suiteLogoPath, outlineColour);
            if (outlined is not null)
                Icon = outlined;
        }

        [RelayCommand]
        private void ShowPickReminder(int maxDelayDays)
        {
            // On the final day a delay is still allowed, force an explicit acknowledgement before letting
            // the user pick a reminder time, since the button itself no longer shows the days remaining.
            if (maxDelayDays == 1)
            {
                TransitioningView = new PopupFinalSkipWarningViewModel();
                return;
            }

            TransitioningView = new PopupPickReminderViewModel();
        }

        [RelayCommand]
        private void ShowPickReminderConfirmed()
        {
            TransitioningView = new PopupPickReminderViewModel();
        }

        [RelayCommand]
        private void ShowWarning()
        {
            TransitioningView = new PopupWarningViewModel();
        }

        private void ApplyConfig()
        {
            var cfg = PopConfigProvider.Current;
            if (cfg is null)
                return;

            if (!string.IsNullOrWhiteSpace(cfg.Action))
                Action = cfg.Action;

            if (cfg.ActionIconKind != null)
                ActionIconKind = cfg.ActionIconKind.Value;

            if (!string.IsNullOrWhiteSpace(cfg.ProductName))
                Name = cfg.ProductName;

            if (!string.IsNullOrWhiteSpace(cfg.ProductVersion))
                Version = cfg.ProductVersion;

            LogInfo("Popup main view model config applied.");
        }

        private static void ExitApplication(int exitCode)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(exitCode);
                return;
            }
            Environment.Exit(exitCode);
        }
    }
}