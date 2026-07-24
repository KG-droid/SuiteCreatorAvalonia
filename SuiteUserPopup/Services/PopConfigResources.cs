using Avalonia;
using Avalonia.Media;

namespace SuiteUserPopup.Services;

public static class PopConfigResources
{
    public const string CompanyLogoForegroundBrushKey = "CompanyLogoForegroundBrush";
    public const string CompanyLogoBackgroundBrushKey = "CompanyLogoBackgroundBrush";

    public static void EnsureDefaults(Application app)
    {
        if (!app.Resources.ContainsKey(CompanyLogoForegroundBrushKey))
            app.Resources[CompanyLogoForegroundBrushKey] = new SolidColorBrush(Colors.White);

        if (!app.Resources.ContainsKey(CompanyLogoBackgroundBrushKey))
            app.Resources[CompanyLogoBackgroundBrushKey] = new SolidColorBrush(Colors.Blue);
    }

    public static void Apply(Application app, string? background)
    {
        EnsureDefaults(app);

        if (TryParseColor(background, out var bg))
        {
            app.Resources[CompanyLogoBackgroundBrushKey] = new SolidColorBrush(bg);
            app.Resources[CompanyLogoForegroundBrushKey] = GetReadableForeground(bg);
        }
    }

    private static IBrush GetReadableForeground(Color background)
    {
        // Calculate the relative luminance (per W3C recommendations)
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;

        // If luminance is high, use black text; otherwise, use white text
        return luminance > 0.5
            ? new SolidColorBrush(Colors.Black)
            : new SolidColorBrush(Colors.White);
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            color = Color.Parse(value);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
