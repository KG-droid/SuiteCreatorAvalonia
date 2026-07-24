using Avalonia.Controls;
using Avalonia.Labs.Gif;
using Avalonia.Styling;
using Newtonsoft.Json;
using SuiteCreatorAvalonia.Converters;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Tools;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bitmap = Avalonia.Media.Imaging.Bitmap;
using Color = Avalonia.Media.Color;

namespace SuiteCreatorAvalonia.Services
{
    public partial class AppSettingsControl
    {
        private bool _isLoading = false;
        private string _defaultLogoPath = "avares://SuiteCreatorAvalonia/Assets/Images/SuiteCreatorLogoImage.png";
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1); // Used to prevent multiple threads from accessing the file at the same time
        internal static readonly string AppLocalAppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuiteCreator");
        private readonly string AdminSettingsFilePath = Path.Combine(AppContext.BaseDirectory, "AppSettings.json");
        private readonly string SettingsFilePath = Path.Combine(AppLocalAppDataPath, "AppSettings.json");
        private UISettings Settings;
        private ThemeManager _themeManager;

        public AppSettingsControl() : this(new ThemeManager())
        {
        }

        public AppSettingsControl(ThemeManager themeManager)
        {
            _themeManager = themeManager;
            LoadSettings();
        }


        #region CompanyLogoBackgroundColor
        // Admin-controlled only - set via the install directory's AppSettings.json, not editable in the UI.
        internal Color GetCompanyLogoBackgroundColor()
        {
            if (Settings.CompanyLogoBackgroundColor == null)
                return Color.Parse("#497cab");
            else
                return (Color)Settings.CompanyLogoBackgroundColor;
        }
        #endregion

        #region CompanyLogo
        // Admin-controlled only - set via the install directory's AppSettings.json, not editable in the UI.
        internal Control GetCompanyLogo()
        {
            MemoryStream ms;
            Type? logoType = Settings.CompanyLogoType;
            if (Settings.CompanyLogoBytes == null)
            {
                logoType = typeof(Image);

                Bitmap? defaultBitmap = null;
                var companyLogoPath = Path.Combine(AppContext.BaseDirectory, "CompanyLogo.png");
                if (File.Exists(companyLogoPath))
                {
                    using var fs = File.OpenRead(companyLogoPath);
                    defaultBitmap = new Bitmap(fs);
                }

                defaultBitmap ??= ImageLoader.Get(new Uri(_defaultLogoPath));
                ms = new(GetBmpBytes(defaultBitmap!));
            }
            else
            {
                ms = new(Settings.CompanyLogoBytes);
            }
            if (logoType == typeof(Image))
            {
                var bitmap = new Bitmap(ms);
                Image img = new Image
                {
                    Source = bitmap
                };
                return img;
            }
            else
            {
                return new GifImage { Source = GifStreamSource.FromStream(ms) };
            }
        }

        internal string? GetCompanyLogoBase64()
        {
            if (Settings.CompanyLogoBytes == null)
            {
                Bitmap? defaultBitmap = null;
                var companyLogoPath = Path.Combine(AppContext.BaseDirectory, "CompanyLogo.png");
                if (File.Exists(companyLogoPath))
                {
                    using var fs = File.OpenRead(companyLogoPath);
                    defaultBitmap = new Bitmap(fs);
                }

                defaultBitmap ??= ImageLoader.Get(new Uri(_defaultLogoPath));
                return Convert.ToBase64String(GetBmpBytes(defaultBitmap!));
            }
            else
                return Convert.ToBase64String(Settings.CompanyLogoBytes);
        }

        internal Type? GetCompanyLogoType()
        {
            return Settings.CompanyLogoType;
        }
        #endregion

        #region DarkAccentColour
        internal Color GetDarkAccentColor()
        {
            if (Settings.DarkAccentColour == null)
                return Color.Parse("#000a22");
            else
                return (Color)Settings.DarkAccentColour;
        }
        internal void UpdateDarkAccentColor(Color? color)
        {
            if (color == null)
                Settings.DarkAccentColour = Color.Parse("#000a22");
            else
                Settings.DarkAccentColour = color;
            _themeManager.SetAccentColor((Color)Settings.DarkAccentColour, ThemeVariant.Dark);
        }
        #endregion

        #region IsManualPaneControl
        internal bool GetIsManualPaneControl()
        {
            return Settings.IsManualPaneControl;
        }
        internal void UpdateIsManualPaneControl(bool isManualCtrl)
        {
            Settings.IsManualPaneControl = isManualCtrl;
        }
        #endregion

        #region LightAccentColour
        internal Color GetLightAccentColor()
        {
            if (Settings.LightAccentColour == null)
                return Color.Parse("#e7ebff");
            else
                return (Color)Settings.LightAccentColour;
        }
        internal void UpdateLightAccentColor(Color? color)
        {
            if (color == null)
                Settings.LightAccentColour = Color.Parse("#e7ebff");
            else
                Settings.LightAccentColour = color;
            _themeManager.SetAccentColor((Color)Settings.LightAccentColour, ThemeVariant.Light);
        }
        #endregion

        #region Theme
        internal ThemeVariant GetTheme()
        {
            if (Settings.Theme == null)
                return ThemeVariant.Default;
            else
                return Settings.Theme;
        }
        internal void UpdateTheme(ThemeVariant? theme)
        {
            if (theme == null)
                Settings.Theme = ThemeVariant.Default;
            else
                Settings.Theme = theme;
            _themeManager.SetTheme(Settings.Theme);
        }
        #endregion

        #region LogLocation
        // Admin-controlled only - set via the install directory's AppSettings.json, not editable in the UI.
        internal string GetLogLocation()
        {
            if (Settings.LogLocation == null)
                return @"C:\Modern-Workplace-Logs";
            else
                return Settings.LogLocation;
        }
        #endregion

        #region GlobalPopupCondition
        // Admin-controlled only - set via the install directory's AppSettings.json, not editable in the UI.
        internal string? GetGlobalPopupCondition()
        {
            return Settings.GlobalPopupCondition;
        }
        #endregion

        public UISettings GetAllSettings()
        {
            if (null == Settings)
            {
                LoadSettings();
            }
            return Settings;
        }

        public void LoadSettings()
        {
            try
            {
                _isLoading = true;

                JsonSerializerSettings serializerSettings = new()
                {
                    Converters =
                    {
                        new ColorToJson(),
                        new BitmapToJson(),
                    }
                };

                // Load admin-provided defaults from the install directory first
                UISettings adminSettings = new UISettings();
                if (File.Exists(AdminSettingsFilePath))
                {
                    try
                    {
                        UISettings? adminResult = JsonConvert.DeserializeObject<UISettings>(File.ReadAllText(AdminSettingsFilePath), serializerSettings);
                        if (adminResult != null)
                        {
                            adminSettings = adminResult;
                            AppLog.Info("Loaded admin app settings from install directory", "Settings");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Loading admin settings failed", ex, "Settings");
                    }
                }

                // Apply user settings from LocalAppData on top, overriding admin defaults where set
                if (!File.Exists(SettingsFilePath))
                {
                    Settings = adminSettings;
                    return;
                }

                AppLog.Info("Loading app settings", "Settings");

                try
                {
                    UISettings? results = JsonConvert.DeserializeObject<UISettings>(File.ReadAllText(SettingsFilePath), serializerSettings);
                    if (results != null)
                    {
                        adminSettings.MergeUserSettingsOver(results);
                        Settings = adminSettings;
                    }
                    else
                    {
                        throw new Exception("Settings file is empty or invalid, reverting to admin/default settings");
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error("Loading settings failed", ex, "Settings");
                    AppLog.Info("Will revert to admin/default settings", "Settings");
                    Settings = adminSettings;
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        public async Task SaveAsync()
        {
            if (_isLoading) { return; }
            await _semaphore.WaitAsync();
            try
            {
                AppLog.Info("Saving app settings", "Settings");
                // Only user-ownable settings are persisted to the per-user file. The admin-controlled
                // settings (company logo/background, log location, global popup condition) come solely from
                // the install directory's AppSettings.json and must not be written back per-user, so their
                // fields are left null here and omitted from the file entirely via NullValueHandling.
                UISettings userSettings = new UISettings
                {
                    Theme = Settings.Theme,
                    IsManualPaneControl = Settings.IsManualPaneControl,
                    LightAccentColour = Settings.LightAccentColour,
                    DarkAccentColour = Settings.DarkAccentColour,
                };
                string json = JsonConvert.SerializeObject(userSettings, Formatting.Indented, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    Converters =
                    {
                        new ColorToJson(),
                        new BitmapToJson(),
                    }
                });
                if (!Directory.Exists(AppLocalAppDataPath))
                {
                    Directory.CreateDirectory(AppLocalAppDataPath);
                }
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to save app settings to: {SettingsFilePath}", ex, "Settings");
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private byte[] GetBmpBytes(Bitmap bitmap)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bitmap.Save(ms);
                return ms.ToArray();
            }
        }
    }
}
