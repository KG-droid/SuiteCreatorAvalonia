namespace SuiteUserPopup;

internal sealed class StartupOptions
{
    public static StartupOptions Current { get; private set; } = new StartupOptions();

    public string? BrandingConfigPath { get; private init; }
    public string CompanyLogoPath { get; private init; } = string.Empty;
    public string SuiteLogoPath { get; private init; } = string.Empty;
    public bool IsBlockedNotice { get; private init; }
    public string? BlockedProcessName { get; private init; }
    public string? BlockedExePath { get; private init; }
    public string? BlockedSuiteId { get; private init; }

    public static void Set(
        string? brandingConfigPath,
        string companyLogoPath,
        string suiteLogoPath,
        bool isBlockedNotice = false,
        string? blockedProcessName = null,
        string? blockedExePath = null,
        string? blockedSuiteId = null)
    {
        Current = new StartupOptions
        {
            BrandingConfigPath = brandingConfigPath,
            CompanyLogoPath = companyLogoPath,
            SuiteLogoPath = suiteLogoPath,
            IsBlockedNotice = isBlockedNotice,
            BlockedProcessName = blockedProcessName,
            BlockedExePath = blockedExePath,
            BlockedSuiteId = blockedSuiteId
        };
    }
}
