using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Helpers;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Tools;
using System;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using System.IO;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PopupSampleWindowViewModel : ViewModelBase
    {
        private AppSettingsControl _settingsCtrl;

        [ObservableProperty]
        private TextDocument _mainText = new();

        [ObservableProperty]
        private Bitmap _icon;

        [ObservableProperty]
        private Bitmap? _iconDisplay;

        [ObservableProperty]
        private bool _isClosuresAppsVisible = true;

        [ObservableProperty]
        private bool _hasOtherPopupTypes = false;

        [ObservableProperty]
        private string _Name = "ExampleApp";

        [ObservableProperty]
        private string _version = "9.9.9";

        [ObservableProperty]
        private string _action = "Upgrade";

        [ObservableProperty]
        private int _timer = 40;

        [ObservableProperty]
        private int _maxDelayDays = 7;

        [ObservableProperty]
        private Control _companyLogo;

        [ObservableProperty]
        private IBrush _companyLogoBackgroundColor = new SolidColorBrush(Colors.Blue);

        public IBrush CompanyLogoBackgroundColorShade
        {
            get
            {
                if (CompanyLogoBackgroundColor is SolidColorBrush solidColorBrush)
                {
                    var originalColor = solidColorBrush.Color;

                    // Calculate the relative luminance (per W3C recommendations)
                    double luminance = (0.299 * originalColor.R + 0.587 * originalColor.G + 0.114 * originalColor.B) / 255;

                    // If luminance is high, use dark shader; otherwise, use light shader
                    if (luminance > 0.5)
                    {
                        // Lighten by interpolating towards white
                        byte Darken(byte channel) =>
                            (byte)(channel * (1 - 0.3)); // 30% darker


                        var darkerColor = Color.FromArgb(
                            originalColor.A,
                            Darken(originalColor.R),
                            Darken(originalColor.G),
                            Darken(originalColor.B));
                        return new SolidColorBrush(darkerColor);
                    }
                    else
                    {
                        // Lighten by interpolating towards white
                        byte Lighten(byte channel) =>
                            (byte)(channel + (255 - channel) * 0.3); // 30% lighter

                        var lighterColor = Color.FromArgb(
                            originalColor.A,
                            Lighten(originalColor.R),
                            Lighten(originalColor.G),
                            Lighten(originalColor.B));
                        return new SolidColorBrush(lighterColor);
                    }
                }
                else
                {
                    return new SolidColorBrush(Colors.Blue);
                }
            }
        }

        public IBrush CompanyLogoForegroundColor
        {
            get
            {
                if (CompanyLogoBackgroundColor is SolidColorBrush solidColorBrush)
                {
                    var color = solidColorBrush.Color;

                    // Calculate the relative luminance (per W3C recommendations)
                    double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;

                    // If luminance is high, use black text; otherwise, use white text
                    return luminance > 0.5
                        ? new SolidColorBrush(Colors.Black)
                        : new SolidColorBrush(Colors.White);
                }

                // Fallback: default to black
                return new SolidColorBrush(Colors.Black);
            }
        }

        partial void OnCompanyLogoBackgroundColorChanged(IBrush value)
        {
            OnPropertyChanged(nameof(CompanyLogoForegroundColor));
            OnPropertyChanged(nameof(CompanyLogoBackgroundColorShade));
        }

        [ObservableProperty]
        private PopupAction _timerExpireAction = PopupAction.Continue;

        [ObservableProperty]
        private SuiteAction _popupForAction = SuiteAction.Deployment;

        [ObservableProperty]
        private MaterialIconKind _actionIconKind = MaterialIconKind.Update;

        partial void OnIconChanging(Bitmap? oldValue, Bitmap newValue)
        {
            if (newValue == null)
            {
                Icon = ImageLoader.Get(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/SuiteLogo.png"));
            }
        }

        partial void OnIconChanged(Bitmap value)
        {
            UpdateIconDisplay();
        }

        private void UpdateIconDisplay()
        {
            if (Icon is null)
            {
                IconDisplay = null;
                return;
            }

            // This icon sits directly on the plain card background (SystemAltHighColor), so
            // outline against the theme background when the icon is too close in colour to it.
            Color themeBackgroundColour = new ThemeManager().GetResourceColor("SystemAltHighColor");

            if (!LogoContrastHelper.NeedsOutline(Icon, themeBackgroundColour))
            {
                IconDisplay = Icon;
                return;
            }

            Color outlineColour = LogoContrastHelper.GetReadableForeground(themeBackgroundColour);
            IconDisplay = LogoContrastHelper.CreateLogoWithOutline(Icon, outlineColour) ?? Icon;
        }

        public PopupSampleWindowViewModel() : this(new AppSettingsControl())
        {
        }

        public PopupSampleWindowViewModel(AppSettingsControl settingsCtrl)
        {
            _settingsCtrl = settingsCtrl;
            LoadCompanyLogoFromSettings();
            MainText.Text =
                    "The following apps will be closed before the upgrade.\n" +
                    "Please save any work required before continuing.";
            if (Icon == null)
                Icon = ImageLoader.Get(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/SuiteLogo.png"));
        }

        private void LoadCompanyLogoFromSettings()
        {
            CompanyLogo = _settingsCtrl.GetCompanyLogo();
            CompanyLogoBackgroundColor = new SolidColorBrush((_settingsCtrl.GetCompanyLogoBackgroundColor()));
            ApplyCompanyLogoOutlineIfNeeded();
        }

        private void ApplyCompanyLogoOutlineIfNeeded()
        {
            // Animated (gif) company logos aren't covered - baking a static outline into an
            // animated source isn't practical, so only the static image case is handled here.
            if (CompanyLogo is not Image image || image.Source is not Bitmap bitmap)
                return;

            if (CompanyLogoBackgroundColor is not SolidColorBrush backgroundBrush)
                return;

            if (!LogoContrastHelper.NeedsOutline(bitmap, backgroundBrush.Color))
                return;

            if (CompanyLogoForegroundColor is not SolidColorBrush foregroundBrush)
                return;

            Bitmap? outlined = LogoContrastHelper.CreateLogoWithOutline(bitmap, foregroundBrush.Color);
            if (outlined is not null)
                image.Source = outlined;
        }
    }
}
