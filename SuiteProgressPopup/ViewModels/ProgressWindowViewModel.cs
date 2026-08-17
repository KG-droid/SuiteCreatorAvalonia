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

        private const int ElapsedIntervalMs = 1000;

        private readonly string? _progressFilePath;
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer? _lockdownDeadlineTimer;
        private readonly DispatcherTimer? _elapsedTimer;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private bool _hasSeenCompletion;

        [ObservableProperty]
        private Bitmap? _suiteLogo;

        [ObservableProperty]
        private Bitmap? _companyLogo;

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

        [ObservableProperty]
        private bool _isLockdown;

        [ObservableProperty]
        private string? _lockdownMessage;

        [ObservableProperty]
        private string _elapsedTimeText = "00:00";

        public ProgressWindowViewModel() : this(Path.Combine(AppContext.BaseDirectory, "SuiteLogo.png"), null)
        {
        }

        public ProgressWindowViewModel(
            string suiteLogoPath,
            string? progressFilePath,
            SolidColorBrush? progressColourBrush = null,
            bool isLockdown = false,
            string? companyLogoPath = null,
            int lockdownMaxMinutes = 30,
            string? lockdownMessage = null)
        {
            _progressFilePath = progressFilePath;
            LoadSuiteLogo(suiteLogoPath);
            ProgressColourBrush = progressColourBrush ?? new SolidColorBrush(Colors.DarkGreen);
            ApplyLogoOutlineIfNeeded(suiteLogoPath);
            IsLockdown = isLockdown;
            LockdownMessage = string.IsNullOrWhiteSpace(lockdownMessage) ? "Please do not turn off your computer." : lockdownMessage;
            LogInfo($"Progress window view model initialized. Lockdown={isLockdown}");

            if (isLockdown)
            {
                LoadCompanyLogo(companyLogoPath);
            }

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PollIntervalMs)
            };
            _pollTimer.Tick += OnPollTick;

            if (!Design.IsDesignMode)
            {
                _pollTimer.Start();

                if (isLockdown)
                {
                    _lockdownDeadlineTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMinutes(Math.Max(1, lockdownMaxMinutes))
                    };
                    _lockdownDeadlineTimer.Tick += OnLockdownDeadlineElapsed;
                    _lockdownDeadlineTimer.Start();

                    _elapsedTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(ElapsedIntervalMs)
                    };
                    _elapsedTimer.Tick += OnElapsedTick;
                    _elapsedTimer.Start();
                }
            }
        }

        private void LoadCompanyLogo(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                LogWarning($"Lockdown company logo not found: {filePath}");
                return;
            }

            try
            {
                using var fs = File.OpenRead(filePath);
                CompanyLogo = new Bitmap(fs);
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to load lockdown company logo: {ex.Message}");
                return;
            }

            ApplyCompanyLogoOutlineIfNeeded(filePath);
        }

        // Mirrors ApplyLogoOutlineIfNeeded for the suite logo: the company logo is shown against
        // the same lockdown window background, so it needs the same halo treatment when it's too
        // close in colour to read clearly on its own.
        private void ApplyCompanyLogoOutlineIfNeeded(string companyLogoPath)
        {
            Color themeBackgroundColour = TryGetThemeBackgroundColour() ?? Colors.Black;

            if (!LogoContrastHelper.NeedsOutline(companyLogoPath, themeBackgroundColour))
                return;

            Color outlineColour = LogoContrastHelper.GetReadableForeground(themeBackgroundColour);
            Bitmap? outlined = LogoContrastHelper.CreateLogoWithOutline(companyLogoPath, outlineColour);
            if (outlined is not null)
                CompanyLogo = outlined;
        }

        private void OnElapsedTick(object? sender, EventArgs e)
        {
            TimeSpan elapsed = DateTime.UtcNow - _startTime;
            ElapsedTimeText = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"hh\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
        }

        private void OnLockdownDeadlineElapsed(object? sender, EventArgs e)
        {
            LogWarning("Lockdown max time limit reached before the suite completed; self-terminating.");
            _lockdownDeadlineTimer?.Stop();
            ApplicationHelper.ExitApplication(1);
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
            _lockdownDeadlineTimer?.Stop();
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

            if (_lockdownDeadlineTimer is not null)
            {
                _lockdownDeadlineTimer.Tick -= OnLockdownDeadlineElapsed;
                _lockdownDeadlineTimer.Stop();
            }

            if (_elapsedTimer is not null)
            {
                _elapsedTimer.Tick -= OnElapsedTick;
                _elapsedTimer.Stop();
            }
        }
    }
}
