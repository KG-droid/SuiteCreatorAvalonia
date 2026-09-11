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

    // "Run now" tray icon mode: shown for the lifetime of a pending deferral so the user isn't stuck
    // waiting for the scheduled reminder time if they change their mind. See TrayReminderService.
    public bool IsTrayMode { get; private init; }
    public string? TrayReminderTaskName { get; private init; }
    public string TraySuiteName { get; private init; } = "Suite";

    public static void Set(
        string? brandingConfigPath,
        string companyLogoPath,
        string suiteLogoPath,
        bool isBlockedNotice = false,
        string? blockedProcessName = null,
        string? blockedExePath = null,
        string? blockedSuiteId = null,
        bool isTrayMode = false,
        string? trayReminderTaskName = null,
        string traySuiteName = "Suite")
    {
        Current = new StartupOptions
        {
            BrandingConfigPath = brandingConfigPath,
            CompanyLogoPath = companyLogoPath,
            SuiteLogoPath = suiteLogoPath,
            IsBlockedNotice = isBlockedNotice,
            BlockedProcessName = blockedProcessName,
            BlockedExePath = blockedExePath,
            BlockedSuiteId = blockedSuiteId,
            IsTrayMode = isTrayMode,
            TrayReminderTaskName = trayReminderTaskName,
            TraySuiteName = traySuiteName
        };
    }
}
