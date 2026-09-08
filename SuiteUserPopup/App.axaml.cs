using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SuiteUserPopup.Models.Config;
using SuiteUserPopup.Services;
using SuiteUserPopup.ViewModels;
using SuiteUserPopup.Views;
using System;
using System.Diagnostics;
using System.IO;

namespace SuiteUserPopup;

public partial class App : Application
{
    // Must match the prefix ClosureFailsafe.KillLingeringBlockedNotices (SuiteOperations) searches for.
    // The window is borderless and hidden from the taskbar, so this title is never actually seen — it's
    // just a carrier for the suite's stable ID (UpgradeCode), so a later unblock can find and close this
    // notice by enumerating window titles instead of needing a separate marker file.
    internal const string BlockedNoticeTitlePrefix = "SuiteExecBlockedNotice|";

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        AppLogService.Info("Avalonia application initialized.", nameof(App));
        ApplyConfigFromJson();
    }

    // Held for the process lifetime so the tray icon and its poll timer aren't garbage collected out from
    // under their event handlers.
    private TrayReminderService? _trayReminderService;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupOptions options = StartupOptions.Current;

            if (options.IsTrayMode)
            {
                // No window at all — closing the tray icon (or the reminder task disappearing) is the only
                // way this process ends, so it must not tie its lifetime to a window that's never created.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _trayReminderService = new TrayReminderService(desktop, options.TrayReminderTaskName!, options.TraySuiteName, options.SuiteLogoPath);
                AppLogService.Info("Tray reminder icon created successfully.", nameof(App));
            }
            else
            {
                PopupWindowViewModel viewModel = new PopupWindowViewModel(
                    options.CompanyLogoPath,
                    options.SuiteLogoPath,
                    options.IsBlockedNotice,
                    options.BlockedProcessName,
                    options.BlockedExePath);
                PopupWindow popWindow = new PopupWindow
                {
                    DataContext = viewModel,
                    // Centre on screen for a blocked-process notice so it can't be missed; the usual
                    // warning/progress popups stay pinned to the corner like a toast.
                    CenterOnScreen = options.IsBlockedNotice
                };

                if (options.IsBlockedNotice && !string.IsNullOrWhiteSpace(options.BlockedSuiteId))
                    popWindow.Title = BlockedNoticeTitlePrefix + options.BlockedSuiteId;

                desktop.MainWindow = popWindow;
                AppLogService.Info("Main window created successfully.", nameof(App));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyConfigFromJson()
    {
        try
        {
            PopConfigResources.EnsureDefaults(this);

            string? configPath = GetPopConfigPath();
            if (configPath is null)
            {
                AppLogService.Warning("No popconfig.json found. Using defaults.", nameof(App));
                PopConfigProvider.Set(null);
                return;
            }

            PopConfig? cfg = popConfigLoader.LoadFromJsonFile(configPath);
            if (cfg is null)
            {
                AppLogService.Warning($"Failed to parse popconfig: {configPath}. Using defaults.", nameof(App));
                PopConfigProvider.Set(null);
                return;
            }

            PopConfigProvider.Set(cfg);

            PopConfigResources.Apply(this, cfg.CompanyLogoBackground);
            AppLogService.Info($"Applied branding from: {configPath}", nameof(App));
        }
        catch (Exception ex)
        {
            PopConfigProvider.Set(null);
            AppLogService.Error($"Error applying branding: {ex}", nameof(App));
        }
    }

    private static string? GetPopConfigPath()
    {
        string? fromOptions = StartupOptions.Current.BrandingConfigPath;
        if (!string.IsNullOrWhiteSpace(fromOptions) && File.Exists(fromOptions))
            return fromOptions;

        try
        {
            string exeDir = AppContext.BaseDirectory;
            string candidate = Path.Combine(exeDir, "popconfig.json");
            return File.Exists(candidate) ? candidate : null;
        }
        catch
        {
            return null;
        }
    }
}