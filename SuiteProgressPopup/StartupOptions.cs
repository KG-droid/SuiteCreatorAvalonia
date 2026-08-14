using Avalonia.Media;

namespace SuiteProgressPopup;

internal sealed class StartupOptions
{
    public static StartupOptions Current { get; private set; } = new StartupOptions();

    public string SuiteLogoPath { get; private init; } = string.Empty;
    public string? ProgressFilePath { get; private init; }
    public SolidColorBrush? ProgressColourBrush { get; private init; }
    public bool IsLockdown { get; private init; }
    public string? CompanyLogoPath { get; private init; }
    public int LockdownMaxMinutes { get; private init; } = 30;
    public string? LockdownMessage { get; private init; }

    public static void Set(
        string suiteLogoPath,
        string? progressFilePath,
        SolidColorBrush progressColourBrush,
        bool isLockdown = false,
        string? companyLogoPath = null,
        int lockdownMaxMinutes = 30,
        string? lockdownMessage = null)
    {
        Current = new StartupOptions
        {
            SuiteLogoPath = suiteLogoPath,
            ProgressFilePath = progressFilePath,
            ProgressColourBrush = progressColourBrush,
            IsLockdown = isLockdown,
            CompanyLogoPath = companyLogoPath,
            LockdownMaxMinutes = lockdownMaxMinutes,
            LockdownMessage = lockdownMessage
        };
    }
}
