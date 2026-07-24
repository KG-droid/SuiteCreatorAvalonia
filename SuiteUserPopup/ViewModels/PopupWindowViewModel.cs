using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Labs.Gif;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteUserPopup.Helpers;
using SuiteUserPopup.Services;
using System;
using System.IO;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteUserPopup.ViewModels
{
    internal partial class PopupWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ViewModelBase _mainView;

        [ObservableProperty]
        private Control? _companyLogo;

        [ObservableProperty]
        private bool _isSessionLocked;

        [ObservableProperty]
        private bool _isBlockedNotice;

        private readonly WindowsSessionMonitor? _sessionMonitor;

        public PopupWindowViewModel() : this(Path.Combine(AppContext.BaseDirectory, "CompanyLogo.png"), Path.Combine(AppContext.BaseDirectory, "SuiteLogo.png"))
        {
        }

        public PopupWindowViewModel(
            string companyLogoPath,
            string suiteLogoPath,
            bool isBlockedNotice = false,
            string? blockedProcessName = null,
            string? blockedExePath = null)
        {
            IsBlockedNotice = isBlockedNotice;
            MainView = isBlockedNotice
                ? new PopupBlockedViewModel(blockedProcessName, blockedExePath)
                : new PopupMainViewModel(suiteLogoPath);
            LogInfo("Popup window view model initialized.");

            if (!Design.IsDesignMode)
            {
                // A blocked-process notice shows the blocked app's own icon instead of the company logo
                // tab (hidden in PopupWindow.axaml via IsBlockedNotice), so there's nothing to load here.
                if (!isBlockedNotice)
                    LoadCompanyLogo(companyLogoPath);
                _sessionMonitor = new WindowsSessionMonitor();
                IsSessionLocked = _sessionMonitor.IsSessionLocked;
                _sessionMonitor.SessionLockedChanged += OnSessionLockedChanged;
            }
            else
            {
                CompanyLogo = new Image { Source = new Bitmap(companyLogoPath) };
            }
        }

        private void OnSessionLockedChanged(object? sender, bool locked)
        {
            IsSessionLocked = locked;

            if (locked)
            {
                LogWarning("Windows session locked. Application will exit.");
                ExitApplication(1);
            }
        }

        private void LoadCompanyLogo(string logoPath)
        {
            if (File.Exists(logoPath))
            {
                string ext = Path.GetExtension(logoPath).ToLowerInvariant();
                if (ext == ".gif")
                {
                    byte[] bytes = File.ReadAllBytes(logoPath);
                    MemoryStream ms = new MemoryStream(bytes);
                    CompanyLogo = new GifImage { Source = GifStreamSource.FromStream(ms) };
                }
                else
                {
                    CompanyLogo = new Image { Source = LoadCompanyLogoBitmapWithOutlineIfNeeded(logoPath) };
                }
                return;
            }

            LogError($"Company logo file not found: {logoPath}");
            Console.WriteLine($"Company logo file not found: {logoPath}");
            ExitApplication(2);
        }

        private static Bitmap LoadCompanyLogoBitmapWithOutlineIfNeeded(string logoPath)
        {
            // The company logo always sits on the accent-coloured tab shape (CompanyLogoBackgroundBrush),
            // so outline against that fixed colour rather than the theme background. Use the same
            // readable-foreground colour already computed for that tab (CompanyLogoForegroundBrush) so the
            // outline reliably contrasts against it.
            Color backgroundColour = ThemeResourceHelper.ResolveBrushColour(PopConfigResources.CompanyLogoBackgroundBrushKey) ?? Colors.Blue;
            Color outlineColour = ThemeResourceHelper.ResolveBrushColour(PopConfigResources.CompanyLogoForegroundBrushKey) ?? Colors.White;

            if (LogoContrastHelper.NeedsOutline(logoPath, backgroundColour))
            {
                Bitmap? outlined = LogoContrastHelper.CreateLogoWithOutline(logoPath, outlineColour);
                if (outlined is not null)
                    return outlined;
            }

            using var fs = File.OpenRead(logoPath);
            return new Bitmap(fs);
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