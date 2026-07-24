using Avalonia.Styling;
using System;
using Color = Avalonia.Media.Color;
using Newtonsoft.Json;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class UISettings
    {
        public ThemeVariant? Theme { get; set; }
        public bool IsManualPaneControl { get; set; } = false;
        public Color? LightAccentColour { get; set; }
        public Color? DarkAccentColour { get; set; }
        public Type? CompanyLogoType { get; set; }
        public byte[]? CompanyLogoBytes { get; set; }
        public Color? CompanyLogoBackgroundColor { get; set; }
        public string? LogLocation { get; set; }
        public string? GlobalPopupCondition { get; set; }

        [JsonConstructor]
        public UISettings()
        {
        }

        public UISettings Clone()
        {
            return new UISettings
            {
                Theme = Theme,
                IsManualPaneControl = IsManualPaneControl,
                LightAccentColour = LightAccentColour,
                DarkAccentColour = DarkAccentColour,
                CompanyLogoType = CompanyLogoType,
                CompanyLogoBytes = CompanyLogoBytes != null ? (byte[])CompanyLogoBytes.Clone() : null,
                CompanyLogoBackgroundColor = CompanyLogoBackgroundColor,
                LogLocation = LogLocation,
                GlobalPopupCondition = GlobalPopupCondition
            };
        }

        public void UpdateFrom(UISettings settings)
        {
            if (settings == null) return;
            Theme = settings.Theme;
            IsManualPaneControl = settings.IsManualPaneControl;
            LightAccentColour = settings.LightAccentColour;
            DarkAccentColour = settings.DarkAccentColour;
            CompanyLogoType = settings.CompanyLogoType;
            CompanyLogoBytes = settings.CompanyLogoBytes != null ? (byte[])settings.CompanyLogoBytes.Clone() : null;
            CompanyLogoBackgroundColor = settings.CompanyLogoBackgroundColor;
            LogLocation = settings.LogLocation;
            GlobalPopupCondition = settings.GlobalPopupCondition;
        }

        public void MergeUserSettingsOver(UISettings userSettings)
        {
            if (userSettings == null) return;
            if (userSettings.Theme != null) Theme = userSettings.Theme;
            if (userSettings.IsManualPaneControl) IsManualPaneControl = userSettings.IsManualPaneControl;
            if (userSettings.LightAccentColour != null) LightAccentColour = userSettings.LightAccentColour;
            if (userSettings.DarkAccentColour != null) DarkAccentColour = userSettings.DarkAccentColour;
            // CompanyLogoType/CompanyLogoBytes/CompanyLogoBackgroundColor, LogLocation, and GlobalPopupCondition
            // are intentionally excluded here - they are admin-controlled via the install directory's
            // AppSettings.json only and must not be overridable by the per-user settings file.
        }
    }
}