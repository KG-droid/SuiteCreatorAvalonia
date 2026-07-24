using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Application = Avalonia.Application;

namespace SuiteCreatorAvalonia.Services
{
    public class ThemeManager
    {
        public ThemeManager()
        {

        }

        public ThemeVariant GetCurrentTheme()
        {
            return App.Current.ActualThemeVariant;
        }

        public Color GetResourceColor(string resourceName)
        {
            if (App.Current is Application application)
            {
                ThemeVariant themeVariant = application.ActualThemeVariant ?? ThemeVariant.Dark;
                return (Color)App.Current.FindResource(themeVariant, resourceName);
            }
            else
            {
                return Colors.Blue;
            }
        }

        public void SetTheme(ThemeVariant theme)
        {
            if (App.Current is Application application)
            {
                application.RequestedThemeVariant = theme;
            }
        }

        public void SetAccentColor(Color color, ThemeVariant themeVariant)
        {
            if (App.Current is Application application)
            {
                foreach (var style in application.Styles)
                {
                    if (style is FluentTheme fluentTheme)
                    {
                        fluentTheme.Palettes[themeVariant].Accent = color;
                        break;
                    }
                }
                // Change custom accent variants for the theme
                ResourceDictionary dictionary = (ResourceDictionary)application.Resources.ThemeDictionaries[themeVariant];
                if (dictionary.ContainsKey("AccentVariant1"))
                {
                    dictionary["AccentVariant1"] = AdjustColor(color, themeVariant, 10);
                }
                if (dictionary.ContainsKey("AccentVariant2"))
                {
                    dictionary["AccentVariant2"] = AdjustColor(color, themeVariant, 17);
                }
                if (dictionary.ContainsKey("AccentVariant3"))
                {
                    dictionary["AccentVariant3"] = AdjustColor(color, themeVariant, 60);
                }
            }
        }

        private Color AdjustColor(Color color, ThemeVariant themeVariant, int percentageChange)
        {
            if (themeVariant == ThemeVariant.Light)
            {
                // Decrease the lightness of the color for Light mode
                HslColor hsl = color.ToHsl();
                hsl = new HslColor(hsl.A, hsl.H, hsl.S, hsl.L - (percentageChange / 100.0));
                return hsl.ToRgb();
            }
            else
            {
                // Increase the lightness of the color for Dark mode
                HslColor hsl = color.ToHsl();
                hsl = new HslColor(hsl.A, hsl.H, hsl.S, hsl.L + (percentageChange / 100.0));
                return hsl.ToRgb();
            }
        }
    }
}
