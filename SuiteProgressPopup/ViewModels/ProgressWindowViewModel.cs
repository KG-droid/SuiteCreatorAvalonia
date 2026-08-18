using Avalonia.Controls;
using Avalonia.Labs.Gif;
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
using DrawingImage = System.Drawing.Image;

namespace SuiteProgressPopup.ViewModels
{
    internal partial class ProgressWindowViewModel : ViewModelBase, IDisposable
    {
        private const int PollIntervalMs = 500;
        private const int CompletionLingerMs = 1500;

        private const int ElapsedIntervalMs = 1000;
        private const int CompanyLogoMaxDimension = 280;
        private const int CompanyLogoGlowPaddingPx = 600;
        private const byte CompanyLogoGlowMaxAlpha = 100;
        private const double CompanyLogoGlowSigma = 0.38;
        private const double GlowShadeAmount = 0.2;

        private readonly string? _progressFilePath;
        private readonly DispatcherTimer _pollTimer;
        private readonly DispatcherTimer? _lockdownDeadlineTimer;
        private readonly DispatcherTimer? _elapsedTimer;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private bool _hasSeenCompletion;

        // A lockdown can spawn one window per monitor, all sharing this ViewModel - the company
        // logo path is kept here (rather than a single pre-built Control on the ViewModel) so each
        // window can build its own independent Image/GifImage via CreateCompanyLogoControl(). A
        // Control is part of the visual tree and can only have one parent at a time; sharing one
        // instance across multiple windows crashes as soon as the second window tries to show it.
        private string? _companyLogoFilePath;

        [ObservableProperty]
        private Bitmap? _suiteLogo;

        // A soft radial gradient rendered behind the company logo control in LockdownWindow.axaml
        // when the logo is too close in colour to the background to read clearly on its own. Null
        // when no glow is needed.
        [ObservableProperty]
        private IBrush? _companyLogoGlowBrush;

        // Sized to match the logo's own aspect ratio (as rendered at CompanyLogoMaxDimension, plus
        // padding) rather than a fixed circle - a wide/elongated logo would otherwise stick out past
        // a circular glow at its extremities (e.g. the head/tail of a running horse silhouette).
        [ObservableProperty]
        private double _companyLogoGlowWidth;

        [ObservableProperty]
        private double _companyLogoGlowHeight;

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

            _companyLogoFilePath = filePath;
            ApplyCompanyLogoGlowIfNeeded(filePath);
        }

        // Builds a fresh Image or GifImage from the company logo file - called once per lockdown
        // window (see the comment on _companyLogoFilePath) so each monitor gets its own independent
        // control and, for an animated GIF, its own independent stream/frame position.
        public Control? CreateCompanyLogoControl()
        {
            if (string.IsNullOrWhiteSpace(_companyLogoFilePath) || !File.Exists(_companyLogoFilePath))
                return null;

            try
            {
                string ext = Path.GetExtension(_companyLogoFilePath).ToLowerInvariant();
                if (ext == ".gif")
                {
                    byte[] bytes = File.ReadAllBytes(_companyLogoFilePath);
                    MemoryStream ms = new MemoryStream(bytes);
                    return new GifImage
                    {
                        Source = GifStreamSource.FromStream(ms),
                        Stretch = Stretch.Uniform,
                        MaxWidth = CompanyLogoMaxDimension,
                        MaxHeight = CompanyLogoMaxDimension
                    };
                }

                using var fs = File.OpenRead(_companyLogoFilePath);
                return new Image
                {
                    Source = new Bitmap(fs),
                    Stretch = Stretch.Uniform,
                    MaxWidth = CompanyLogoMaxDimension,
                    MaxHeight = CompanyLogoMaxDimension
                };
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to load lockdown company logo: {ex.Message}");
                return null;
            }
        }

        // Sets a brush that LockdownWindow.axaml renders as a separate radial gradient behind the
        // company logo control - used for both static images and animated GIFs alike now, rather
        // than baking a silhouette-hugging glow into a static bitmap's own pixels.
        private void ApplyCompanyLogoGlowIfNeeded(string companyLogoPath)
        {
            Color themeBackgroundColour = TryGetThemeBackgroundColour() ?? Colors.Black;

            if (!LogoContrastHelper.NeedsGlow(companyLogoPath, themeBackgroundColour))
                return;

            Color glowColour = GetGlowColour();
            CompanyLogoGlowBrush = CreateRadialGlowBrush(glowColour);

            (double renderedWidth, double renderedHeight) = GetRenderedSizeWithinBounds(companyLogoPath, CompanyLogoMaxDimension);
            CompanyLogoGlowWidth = renderedWidth + CompanyLogoGlowPaddingPx;
            CompanyLogoGlowHeight = renderedHeight + CompanyLogoGlowPaddingPx;
        }

        // A soft ambient glow needs a fade so gradual the eye can't pin down where it ends, rather
        // than a ring with a perceptible edge. A Gaussian falloff (as opposed to the sharper 1-t^2
        // curve) naturally tapers to a long, near-invisible tail well before the last stop, and
        // CompanyLogoGlowSigma/CompanyLogoGlowPaddingPx are tuned so what little alpha remains at t=1 is negligible.
        // Many stops keep Avalonia's linear interpolation between them close to the true curve.
        private static RadialGradientBrush CreateRadialGlowBrush(Color glowColour)
        {
            RadialGradientBrush brush = new RadialGradientBrush();
            for (int i = 0; i <= 20; i++)
            {
                double t = i / 20.0;
                double falloff = Math.Exp(-(t * t) / (2 * CompanyLogoGlowSigma * CompanyLogoGlowSigma));
                byte alpha = (byte)Math.Clamp(CompanyLogoGlowMaxAlpha * falloff, 0, 255);
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, glowColour.R, glowColour.G, glowColour.B), t));
            }
            return brush;
        }

        // Mirrors the Stretch="Uniform" + MaxWidth/MaxHeight scaling GifImage applies itself, so the
        // glow shape's aspect ratio matches how the logo actually renders rather than assuming square.
        private static (double Width, double Height) GetRenderedSizeWithinBounds(string imagePath, int maxDimension)
        {
            try
            {
                using DrawingImage source = DrawingImage.FromFile(imagePath);
                double scale = Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height);
                return (source.Width * scale, source.Height * scale);
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Failed to read image dimensions for glow sizing: {ex.Message}", nameof(ProgressWindowViewModel));
                return (maxDimension, maxDimension);
            }
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

            if (!LogoContrastHelper.NeedsGlow(suiteLogoPath, themeBackgroundColour))
                return;

            Bitmap? withGlow = LogoContrastHelper.CreateLogoWithGlow(suiteLogoPath, GetGlowColour());
            if (withGlow is not null)
                SuiteLogo = withGlow;
        }

        // The glow is a shade of the suite's own accent colour (--ProgressColour) rather than a
        // generic black/white: darker for dark mode so it doesn't wash out against a dark
        // background, lighter for light mode so it doesn't turn muddy against a light one.
        private Color GetGlowColour()
        {
            Color accent = ProgressColourBrush.Color;
            ThemeVariant themeVariant = Avalonia.Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
            return themeVariant == ThemeVariant.Light
                ? Lighten(accent, GlowShadeAmount)
                : Darken(accent, GlowShadeAmount);
        }

        private static Color Darken(Color colour, double amount) => Color.FromRgb(
            (byte)(colour.R * (1 - amount)),
            (byte)(colour.G * (1 - amount)),
            (byte)(colour.B * (1 - amount)));

        private static Color Lighten(Color colour, double amount) => Color.FromRgb(
            (byte)(colour.R + (255 - colour.R) * amount),
            (byte)(colour.G + (255 - colour.G) * amount),
            (byte)(colour.B + (255 - colour.B) * amount));

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
