namespace SuiteProgressPopup.Models;

public sealed class ProgressStatus
{
    public int Percentage { get; set; }
    public string? StatusText { get; set; }
    public string? ProductName { get; set; }
    public bool IsComplete { get; set; }
    public bool IsError { get; set; }
}
