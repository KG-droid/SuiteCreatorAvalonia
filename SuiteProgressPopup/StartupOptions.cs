using Avalonia.Media;

namespace SuiteProgressPopup;

internal sealed class StartupOptions
{
    public static StartupOptions Current { get; private set; } = new StartupOptions();

    public string SuiteLogoPath { get; private init; } = string.Empty;
    public string? ProgressFilePath { get; private init; }
    public SolidColorBrush? ProgressColourBrush { get; private init; }

    public static void Set(string suiteLogoPath, string? progressFilePath, SolidColorBrush progressColourBrush)
    {
        Current = new StartupOptions
        {
            SuiteLogoPath = suiteLogoPath,
            ProgressFilePath = progressFilePath,
            ProgressColourBrush = progressColourBrush
        };
    }
}
