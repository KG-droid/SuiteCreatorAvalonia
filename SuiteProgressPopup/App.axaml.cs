using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using SuiteProgressPopup.Services;
using SuiteProgressPopup.ViewModels;
using SuiteProgressPopup.Views;
using System;

namespace SuiteProgressPopup;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppLogService.Info("Avalonia application initialized.", nameof(App));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupOptions options = StartupOptions.Current;
            ProgressWindowViewModel viewModel = new ProgressWindowViewModel(
                options.SuiteLogoPath,
                options.ProgressFilePath,
                options.ProgressColourBrush,
                options.IsLockdown,
                options.CompanyLogoPath,
                options.LockdownMaxMinutes,
                options.LockdownMessage);

            if (options.IsLockdown)
            {
                LockdownWindow primaryWindow = new LockdownWindow { DataContext = viewModel };
                desktop.MainWindow = primaryWindow;

                // A locked-down machine that still shows a usable desktop on a second monitor isn't
                // actually locked down, so every other connected screen gets its own identical,
                // topmost blocker window sharing the same ViewModel - progress/status stay in sync
                // across all of them automatically via the shared bindings.
                primaryWindow.Opened += (_, _) => SpawnLockdownBlockersForOtherScreens(primaryWindow, viewModel);
            }
            else
            {
                desktop.MainWindow = new ProgressWindow { DataContext = viewModel };
            }

            AppLogService.Info("Main window created successfully.", nameof(App));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void SpawnLockdownBlockersForOtherScreens(LockdownWindow primaryWindow, ProgressWindowViewModel viewModel)
    {
        try
        {
            Screen? primaryScreen = primaryWindow.Screens.ScreenFromWindow(primaryWindow) ?? primaryWindow.Screens.Primary;
            int spawned = 0;

            foreach (Screen screen in primaryWindow.Screens.All)
            {
                if (primaryScreen is not null && screen.Bounds == primaryScreen.Bounds)
                    continue;

                LockdownWindow blockerWindow = new LockdownWindow { DataContext = viewModel };
                blockerWindow.SetTargetScreen(screen);
                blockerWindow.Show();
                spawned++;
            }

            AppLogService.Info($"Lockdown covering {spawned + 1} screen(s) total.", nameof(App));
        }
        catch (Exception ex)
        {
            AppLogService.Warning($"Failed to spawn lockdown windows for additional screens: {ex.Message}", nameof(App));
        }
    }
}
