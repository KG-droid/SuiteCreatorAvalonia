using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteUserPopup.Helpers;
using SuiteUserPopup.Services;
using System.Drawing;
using System.IO;
using System.Linq;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteUserPopup.ViewModels
{
    internal partial class PopupBlockedViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _processName;

        [ObservableProperty]
        private string _suiteName = "This suite";

        [ObservableProperty]
        private string _mainText;

        [ObservableProperty]
        private Bitmap? _appIcon;

        public PopupBlockedViewModel() : this("ExampleApp.exe", null)
        {
        }

        public PopupBlockedViewModel(string? processName, string? blockedExePath)
        {
            ProcessName = string.IsNullOrWhiteSpace(processName) ? "This application" : processName;
            ApplyConfig();
            MainText = $"{ProcessName} is blocked while {SuiteName} is running.";
            LoadAppIcon(blockedExePath);
            LogInfo($"Blocked notice shown for process: {ProcessName}");
        }

        private void ApplyConfig()
        {
            var cfg = PopConfigProvider.Current;
            if (cfg is null)
                return;

            // Vendor and app name from the suite's own build settings, e.g. "Acme Corp MySuite" —
            // falls back to whichever half is actually present, or the generic default if neither is.
            string combined = string.Join(" ", new[] { cfg.Manufacturer, cfg.ProductName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

            if (!string.IsNullOrWhiteSpace(combined))
                SuiteName = combined;
        }

        // Best-effort only — if the path is missing or icon extraction fails, the view falls back to a
        // generic "blocked" glyph instead of the real app icon.
        private void LoadAppIcon(string? blockedExePath)
        {
            if (string.IsNullOrWhiteSpace(blockedExePath) || !File.Exists(blockedExePath))
                return;

            try
            {
                using Icon? icon = Icon.ExtractAssociatedIcon(blockedExePath);
                if (icon is null)
                    return;

                using System.Drawing.Bitmap gdiBitmap = icon.ToBitmap();
                using MemoryStream memoryStream = new MemoryStream();
                gdiBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                memoryStream.Seek(0, SeekOrigin.Begin);
                AppIcon = new Bitmap(memoryStream);
            }
            catch
            {
                LogWarning($"Failed to extract icon from: {blockedExePath}");
            }
        }

        [RelayCommand]
        private void Dismiss()
        {
            ApplicationHelper.ExitApplication(0);
        }
    }
}
