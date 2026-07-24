using Material.Icons;
using System.Collections.Generic;

namespace SuiteUserPopup.Models.Config;

public sealed class PopConfig
{
    public string? LogFilePath { get; set; }
    public string? CompanyLogoBackground { get; set; }
    public string? Manufacturer { get; set; }
    public string? ProductName { get; set; }
    public string? ProductVersion { get; set; }
    public string? MainText { get; set; }
    public bool? IsClosuresAppsVisible { get; set; }
    public List<string> ClosureApps { get; set; } = new List<string>();
    public int? Timer { get; set; }
    public int? MaxDelayDays { get; set; }
    public string? Action { get; set; }
    public MaterialIconKind? ActionIconKind { get; set; }
    public PopupAction? TimerExpireAction { get; set; }
}
