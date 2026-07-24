using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace SuiteUserPopup.Helpers
{
    internal static class ThemeResourceHelper
    {
        public static Color? ResolveBrushColour(string resourceKey)
        {
            if (Application.Current?.TryGetResource(resourceKey, CurrentThemeVariant, out object? resource) == true
                && resource is ISolidColorBrush brush)
            {
                return brush.Color;
            }

            return null;
        }

        public static Color? ResolveColour(string resourceKey)
        {
            if (Application.Current?.TryGetResource(resourceKey, CurrentThemeVariant, out object? resource) == true
                && resource is Color colour)
            {
                return colour;
            }

            return null;
        }

        // Picks black or white, whichever reads clearly against the given background.
        public static Color GetReadableForeground(Color background)
        {
            double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            return luminance > 0.5 ? Colors.Black : Colors.White;
        }

        private static ThemeVariant CurrentThemeVariant
            => Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
    }
}
