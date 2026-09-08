using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;

namespace SuiteUserPopup.Services
{
    // Backs the "Run now" tray icon shown for the lifetime of a pending deferral (see
    // Suite.TrayReminder.cs on the SuiteExecutor side). Runs unelevated as the interactive user; it never
    // runs the suite itself, it only triggers the already-scheduled SuiteReminder_* task early — that
    // task is what actually re-runs SuiteExecutor. The task's security descriptor is widened at creation
    // time (Suite.TrayReminder.cs) to let a standard user start it on demand without a UAC prompt.
    internal sealed class TrayReminderService : IDisposable
    {
        private static readonly string _schTasksPath = Path.Combine(Environment.SystemDirectory, "schtasks.exe");

        // How often to check whether the reminder task we're watching is still pending. It disappears once
        // the reminder actually runs and the suite completes (CleanupDeferral) or the deferral is otherwise
        // cancelled — at that point this tray icon has nothing left to do and should close itself rather
        // than linger in the notification area indefinitely.
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(60);

        private readonly IClassicDesktopStyleApplicationLifetime _desktop;
        private readonly string _reminderTaskName;
        private readonly string _suiteName;
        private readonly DispatcherTimer _pollTimer;
        private TrayIcon? _trayIcon;
        private NativeMenuItem? _runNowItem;
        private bool _runNowClicked;

        public TrayReminderService(IClassicDesktopStyleApplicationLifetime desktop, string reminderTaskName, string suiteName, string? suiteLogoPath)
        {
            _desktop = desktop;
            _reminderTaskName = reminderTaskName;
            _suiteName = string.IsNullOrWhiteSpace(suiteName) ? "Suite" : suiteName;

            BuildTrayIcon(suiteLogoPath);

            _pollTimer = new DispatcherTimer { Interval = _pollInterval };
            _pollTimer.Tick += (_, _) => CheckReminderStillPending();
            _pollTimer.Start();

            // Also check right away — if the deferral was already handled by the time this instance of the
            // tray started (e.g. it was relaunched at logon just as the reminder happened to fire), there's
            // no reason to sit in the tray at all.
            CheckReminderStillPending();
        }

        private void BuildTrayIcon(string? suiteLogoPath)
        {
            WindowIcon? icon = TryLoadIcon(suiteLogoPath);

            _runNowItem = new NativeMenuItem($"Run {_suiteName} now");
            _runNowItem.Click += OnRunNowClicked;

            NativeMenu menu = new NativeMenu();
            menu.Items.Add(_runNowItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            // Closes this tray process entirely (not just the icon) — it comes back at the user's next
            // logon via the logon-triggered SuiteTrayReminder_* task while the deferral is still pending.
            NativeMenuItem closeItem = new NativeMenuItem("Close");
            closeItem.Click += (_, _) => _desktop.Shutdown();
            menu.Items.Add(closeItem);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = $"{_suiteName} — an update is waiting to be deferred or run",
                Menu = menu,
                IsVisible = true
            };

            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _trayIcon });
        }

        private static WindowIcon? TryLoadIcon(string? suiteLogoPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(suiteLogoPath) && File.Exists(suiteLogoPath))
                    return new WindowIcon(suiteLogoPath);
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Failed to load tray icon from '{suiteLogoPath}': {ex.Message}", "TrayReminder");
            }

            return null;
        }

        private void OnRunNowClicked(object? sender, EventArgs e)
        {
            if (_runNowClicked)
                return; // Debounce: schtasks /Run is not instantaneous, ignore repeat clicks while it's in flight.
            _runNowClicked = true;

            if (_runNowItem != null)
                _runNowItem.Header = "Starting…";

            AppLogService.Info($"'Run now' clicked — triggering scheduled task '{_reminderTaskName}'", "TrayReminder");

            bool started = TryRunReminderTaskNow();
            if (!started)
            {
                AppLogService.Warning($"Failed to trigger '{_reminderTaskName}' on demand.", "TrayReminder");
                if (_trayIcon != null)
                    _trayIcon.ToolTipText = $"{_suiteName} — couldn't start now, it will still run at the scheduled time";
                _runNowClicked = false;
                if (_runNowItem != null)
                    _runNowItem.Header = $"Run {_suiteName} now";
                return;
            }

            // The triggered task now owns showing the actual popup/progress UI. Give it a moment to launch
            // before this tray icon closes itself, rather than disappearing the instant the click happens.
            DispatcherTimer.RunOnce(() => _desktop.Shutdown(), TimeSpan.FromSeconds(3));
        }

        private bool TryRunReminderTaskNow()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _schTasksPath,
                    Arguments = $"/Run /TN \"{_reminderTaskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null)
                    return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Error triggering scheduled task '{_reminderTaskName}': {ex.Message}", "TrayReminder");
                return false;
            }
        }

        private void CheckReminderStillPending()
        {
            if (_runNowClicked)
                return; // Already shutting down on our own terms.

            if (DoesTaskExist(_reminderTaskName))
                return;

            AppLogService.Info($"Reminder task '{_reminderTaskName}' no longer exists — closing tray icon.", "TrayReminder");
            _desktop.Shutdown();
        }

        private static bool DoesTaskExist(string taskName)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = _schTasksPath,
                    Arguments = $"/Query /TN \"{taskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                // Fail safe towards keeping the tray icon visible rather than disappearing on a transient error.
                return true;
            }
        }

        public void Dispose()
        {
            _pollTimer.Stop();
            _trayIcon?.Dispose();
        }
    }
}
