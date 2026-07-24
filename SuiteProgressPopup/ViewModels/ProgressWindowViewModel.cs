using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteProgressPopup.Helpers;
using SuiteProgressPopup.Models;
using SuiteProgressPopup.Services;
using System;
using System.IO;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteProgressPopup.ViewModels
{
    internal partial class ProgressWindowViewModel : ViewModelBase, IDisposable
    {
        private const int PollIntervalMs = 500;
        private const int CompletionLingerMs = 1500;

        private readonly string? _progressFilePath;
        private readonly DispatcherTimer _pollTimer;
        private bool _hasSeenCompletion;

        [ObservableProperty]
        private Bitmap? _suiteLogo;

        [ObservableProperty]
        private int _percentage;

        [ObservableProperty]
        private bool _isIndeterminate = true;

        [ObservableProperty]
        private string _statusText = "Preparing installation...";

        [ObservableProperty]
        private bool _isError;

        [ObservableProperty]
        private SolidColorBrush _progressColourBrush;

        public ProgressWindowViewModel() : this(Path.Combine(AppContext.BaseDirectory, "SuiteLogo.png"), null)
        {
        }

        public ProgressWindowViewModel(string suiteLogoPath, string? progressFilePath, SolidColorBrush? progressColourBrush = null)
        {
            _progressFilePath = progressFilePath;
            LoadSuiteLogo(suiteLogoPath);
            ProgressColourBrush = progressColourBrush ?? new SolidColorBrush(Colors.DarkGreen);
            ApplyLogoOutlineIfNeeded(suiteLogoPath);
            LogInfo("Progress window view model initialized.");

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _pollTimer.Tick += OnPollTick;

            if (!Design.IsDesignMode)
            {
                _pollTimer.Start();
            }
        }

        private void LoadSuiteLogo(string filePath)
        {
            if (File.Exists(filePath))
            {
                using var fs = File.OpenRead(filePath);
                SuiteLogo = new Bitmap(fs);
            }
            else
            {
                LogWarning($"Suite logo file not found: {filePath}");
            }
        }

        private void ApplyLogoOutlineIfNeeded(string suiteLogoPath)
        {
            Color themeBackgroundColour = TryGetThemeBackgroundColour() ?? Colors.Black;

            if (!LogoContrastHelper.NeedsOutline(suiteLogoPath, themeBackgroundColour))
                return;

            // Use black/white based on the theme background's own luminance rather than the suite's
            // accent colour, since that accent is user-configurable and can be just as dark as the
            // background it would need to contrast against.
            Color outlineColour = LogoContrastHelper.GetReadableForeground(themeBackgroundColour);
            Bitmap? outlined = LogoContrastHelper.CreateLogoWithOutline(suiteLogoPath, outlineColour);
            if (outlined is not null)
                SuiteLogo = outlined;
        }

        private static Color? TryGetThemeBackgroundColour()
        {
            ThemeVariant themeVariant = Avalonia.Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
            if (Avalonia.Application.Current?.TryGetResource("SystemAltHighColor", themeVariant, out object? resource) == true
                && resource is Color colour)
            {
                return colour;
            }

            return null;
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            ProgressStatus? status = ProgressFileReader.TryRead(_progressFilePath);
            if (status is null)
                return;

            IsIndeterminate = false;
            Percentage = Math.Clamp(status.Percentage, 0, 100);
            IsError = status.IsError;

            if (!string.IsNullOrWhiteSpace(status.StatusText))
                StatusText = status.StatusText;
            else if (status.IsError)
                StatusText = "Installation failed";
            else if (status.IsComplete)
                StatusText = "Installation complete";

            if ((status.IsComplete || status.IsError) && !_hasSeenCompletion)
            {
                _hasSeenCompletion = true;
                ScheduleExit();
            }
        }

        private void ScheduleExit()
        {
            DispatcherTimer.RunOnce(() =>
            {
                LogInfo("Progress popup exiting after completion linger period.");
                ApplicationHelper.ExitApplication(0);
            }, TimeSpan.FromMilliseconds(CompletionLingerMs));
        }

        public void Dispose()
        {
            _pollTimer.Tick -= OnPollTick;
            _pollTimer.Stop();
        }
    }
}
