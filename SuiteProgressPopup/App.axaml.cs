using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SuiteProgressPopup.Services;
using SuiteProgressPopup.ViewModels;
using SuiteProgressPopup.Views;

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

            Window mainWindow = options.IsLockdown
                ? new LockdownWindow { DataContext = viewModel }
                : new ProgressWindow { DataContext = viewModel };

            desktop.MainWindow = mainWindow;
            AppLogService.Info("Main window created successfully.", nameof(App));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
