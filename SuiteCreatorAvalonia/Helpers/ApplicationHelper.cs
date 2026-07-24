using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;

namespace SuiteUserPopup.Helpers
{
    public static class ApplicationHelper
    {
        public static void ExitApplication(int exitCode)
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(exitCode);
                return;
            }
            Environment.Exit(exitCode);
        }
    }
}
