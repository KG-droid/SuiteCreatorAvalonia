using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Material.Icons.Avalonia;
using SuiteUserPopup.Helpers;
using SuiteUserPopup.Models;
using SuiteUserPopup.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace SuiteUserPopup.ViewModels
{
    internal partial class PopupWarningViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _mainText;

        [ObservableProperty]
        private bool _isClosuresAppsVisible = false;

        [ObservableProperty]
        private List<string> _closureApps = new List<string>();

        [ObservableProperty]
        private int _timer = 40;

        [ObservableProperty]
        private int _maxDelayDays = 7;

        [ObservableProperty]
        private string _action = "Upgrade";

        [ObservableProperty]
        private PopupAction _timerExpireAction = PopupAction.Continue;

        [ObservableProperty]
        private MaterialIconKind _actionIconKind = MaterialIconKind.Download;

        [ObservableProperty]
        private double _progressMax;

        [ObservableProperty]
        private double _progressValue;

        /// <summary>
        /// Time remaining as a 0-100 percentage (100 = timer just started, 0 = expired), for ShimmerProgress's
        /// fixed percentage scale. Inverted from elapsed time so the bar reads as a countdown — starting full
        /// and draining away — rather than filling up as if towards a completed task.
        /// </summary>
        [ObservableProperty]
        private double _timeRemainingPercent;

        [ObservableProperty]
        private ObservableCollection<Control> _appsToCloseControls = new();

        private CancellationTokenSource? _progressCts;

        private bool _isInitializing;

        private sealed class AppClosureDisplayItem
        {
            internal AppClosureDisplayItem(string tooltipText, Avalonia.Media.Imaging.Bitmap? bitmap, MaterialIconKind fallbackIconKind)
            {
                TooltipText = tooltipText;
                Bitmap = bitmap;
                FallbackIconKind = fallbackIconKind;
            }

            internal string TooltipText { get; }

            internal Avalonia.Media.Imaging.Bitmap? Bitmap { get; }

            internal MaterialIconKind FallbackIconKind { get; }
        }

        private static class NativeMethods
        {
            [DllImport("shell32.dll", CharSet = CharSet.Auto)]
            internal static extern uint ExtractIconEx(
                string lpszFile, int nIconIndex,
                IntPtr[]? phiconLarge, IntPtr[]? phiconSmall,
                uint nIcons);

            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            internal static extern int SHLoadIndirectString(
                string pszSource,
                StringBuilder pszOutBuf,
                uint cchOutBuf,
                IntPtr ppvReserved);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool DestroyIcon(IntPtr hIcon);
        }

        public PopupWarningViewModel()
        {
            _isInitializing = true;
            MainText = "The following apps will be closed before the upgrade. Please save any work required before you continue.";
            ApplyConfig();
            _isInitializing = false;
            StartProgress();
            if (IsClosuresAppsVisible)
            {
                if (!Design.IsDesignMode)
                    _ = LoadAppClosuresIconsAsync();
                else
                    AddExampleAppClosuresIcons();
            }
        }

        partial void OnTimerChanged(int value)
        {
            if (_isInitializing)
                return;

            StartProgress();
        }

        private void StartProgress()
        {
            _progressCts?.Cancel();
            _progressCts?.Dispose();
            _progressCts = new CancellationTokenSource();

            var totalSeconds = Math.Max(0, Timer) * 60;
            ProgressMax = totalSeconds;
            ProgressValue = 0;
            TimeRemainingPercent = 100;

            if (totalSeconds <= 0)
                return;

            _ = RunProgressAsync(totalSeconds, _progressCts.Token);
        }

        private async Task RunProgressAsync(int totalSeconds, CancellationToken token)
        {
            try
            {
                for (var elapsed = 0; elapsed < totalSeconds; elapsed++)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                    ProgressValue = elapsed + 1;
                    TimeRemainingPercent = 100 - (elapsed + 1) * 100.0 / totalSeconds;
                }
                ApplicationHelper.ExitApplication(4);
            }
            catch (OperationCanceledException)
            {
            }
        }

        [RelayCommand]
        private void Continue()
        {
            ApplicationHelper.ExitApplication(0);
        }

        private void ApplyConfig()
        {
            var cfg = PopConfigProvider.Current;
            if (cfg is null)
                return;

            if (!string.IsNullOrWhiteSpace(cfg.MainText))
                MainText = cfg.MainText;

            if (cfg.IsClosuresAppsVisible is not null)
                IsClosuresAppsVisible = cfg.IsClosuresAppsVisible.Value;

            if (cfg.Timer is not null)
                Timer = cfg.Timer.Value;

            if (cfg.MaxDelayDays is not null)
                MaxDelayDays = cfg.MaxDelayDays.Value;

            if (!string.IsNullOrWhiteSpace(cfg.Action))
                Action = cfg.Action;

            if (cfg.TimerExpireAction is not null)
                TimerExpireAction = cfg.TimerExpireAction.Value;

            if (cfg.ClosureApps is not null)
                ClosureApps = cfg.ClosureApps;
        }

        private async Task LoadAppClosuresIconsAsync()
        {
            try
            {
                List<AppClosureDisplayItem> appClosureDisplayItems = await Task.Run(GetAppClosureDisplayItems);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AppsToCloseControls.Clear();
                    foreach (AppClosureDisplayItem appClosureDisplayItem in appClosureDisplayItems)
                    {
                        AppsToCloseControls.Add(CreateAppClosureControl(appClosureDisplayItem));
                    }
                });
            }
            catch
            {
            }
        }

        private List<AppClosureDisplayItem> GetAppClosureDisplayItems()
        {
            HashSet<string> closureNames = new HashSet<string>(
                ClosureApps.Select(Path.GetFileNameWithoutExtension),
                StringComparer.OrdinalIgnoreCase);

            IEnumerable<Process> matchingProcesses = Process.GetProcesses()
                .Where(p => closureNames.Contains(p.ProcessName))
                .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            List<AppClosureDisplayItem> appClosureDisplayItems = new List<AppClosureDisplayItem>();

            foreach (Process process in matchingProcesses)
            {
                Avalonia.Media.Imaging.Bitmap? bitmap = null;
                string tooltipText = process.ProcessName;
                try
                {
                    string? exePath = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        bool isAppx = exePath.StartsWith(
                            @"C:\Program Files\WindowsApps\",
                            StringComparison.OrdinalIgnoreCase);

                        if (isAppx)
                        {
                            try
                            {
                                using Icon? exeIcon = ExtractEmbeddedIcon(exePath);
                                bitmap = CreateAvaloniaBitmap(exeIcon);
                            }
                            catch { }

                            string packageDir = Path.GetDirectoryName(exePath)!;
                            string manifestPath = Path.Combine(packageDir, "AppxManifest.xml");
                            if (File.Exists(manifestPath))
                            {
                                try
                                {
                                    XDocument manifest = XDocument.Load(manifestPath);
                                    XNamespace appxNs = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
                                    XNamespace uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
                                    string exeName = Path.GetFileName(exePath);

                                    XElement? appNode = manifest
                                        .Descendants(appxNs + "Application")
                                        .FirstOrDefault(a =>
                                            string.Equals(
                                                a.Attribute("Executable")?.Value,
                                                exeName,
                                                StringComparison.OrdinalIgnoreCase));

                                    if (appNode is not null)
                                    {
                                        string? displayName = appNode
                                            .Descendants(uap + "VisualElements")
                                            .FirstOrDefault()
                                            ?.Attribute("DisplayName")?.Value;
                                        if (!string.IsNullOrWhiteSpace(displayName))
                                        {
                                            string? packageName = manifest
                                                .Root?
                                                .Element(appxNs + "Identity")?
                                                .Attribute("Name")?
                                                .Value;
                                            tooltipText = ResolveAppxDisplayName(packageDir, displayName, packageName);
                                        }

                                        if (bitmap is null)
                                        {
                                            string? logoRelPath = appNode
                                                .Descendants(uap + "VisualElements")
                                                .FirstOrDefault()
                                                ?.Attribute("Square44x44Logo")?.Value;

                                            if (!string.IsNullOrWhiteSpace(logoRelPath))
                                            {
                                                string logoPath = Path.Combine(packageDir, logoRelPath);
                                                if (!File.Exists(logoPath))
                                                {
                                                    string logoDir = Path.GetDirectoryName(logoPath)!;
                                                    string logoStem = Path.GetFileNameWithoutExtension(logoPath);
                                                    string logoExt = Path.GetExtension(logoPath);
                                                    IEnumerable<string> candidates = Directory
                                                        .EnumerateFiles(logoDir, $"{logoStem}*{logoExt}");
                                                    string? targetSize36 = candidates
                                                        .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                                                            .EndsWith("targetsize-36", StringComparison.OrdinalIgnoreCase));
                                                    logoPath = targetSize36
                                                        ?? candidates.OrderBy(f => f).FirstOrDefault()
                                                        ?? logoPath;
                                                }

                                                if (File.Exists(logoPath))
                                                {
                                                    bitmap = new Avalonia.Media.Imaging.Bitmap(logoPath);
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            // Try to get the file description from the exe's version info
                            try
                            {
                                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                                if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
                                    tooltipText = versionInfo.ProductName;
                            }
                            catch { }

                            using Icon? icon = Icon.ExtractAssociatedIcon(exePath);
                            bitmap = CreateAvaloniaBitmap(icon);
                        }
                    }
                }
                catch
                {
                }

                AppClosureDisplayItem appClosureDisplayItem = new AppClosureDisplayItem(
                    tooltipText,
                    bitmap,
                    MaterialIconKind.WindowMaximize);

                appClosureDisplayItems.Add(appClosureDisplayItem);
            }

            return appClosureDisplayItems;
        }

        private static Control CreateAppClosureControl(AppClosureDisplayItem appClosureDisplayItem)
        {
            Control control;
            if (appClosureDisplayItem.Bitmap is not null)
            {
                control = new Avalonia.Controls.Image
                {
                    Source = appClosureDisplayItem.Bitmap,
                };
            }
            else
            {
                control = new MaterialIcon
                {
                    Kind = appClosureDisplayItem.FallbackIconKind,
                };
            }

            ToolTip.SetTip(control, appClosureDisplayItem.TooltipText);
            control.Height = 35;

            StackPanel stack = new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Spacing = 2 };
            stack.Children.Add(control);

            TextBlock text = new TextBlock
            {
                Text = appClosureDisplayItem.TooltipText,
                FontSize = 10,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            stack.Children.Add(text);
            return stack;
        }

        private static Avalonia.Media.Imaging.Bitmap? CreateAvaloniaBitmap(Icon? icon)
        {
            if (icon is null)
                return null;

            using System.Drawing.Bitmap gdiBitmap = icon.ToBitmap();
            using MemoryStream memoryStream = new MemoryStream();
            gdiBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
            memoryStream.Seek(0, SeekOrigin.Begin);

            return new Avalonia.Media.Imaging.Bitmap(memoryStream);
        }

        private void AddExampleAppClosuresIcons()
        {
            (MaterialIconKind Kind, string Name)[] examples = new[]
            {
                (MaterialIconKind.MicrosoftEdge, "Microsoft Edge"),
                (MaterialIconKind.MicrosoftOutlook, "Microsoft Outlook"),
                (MaterialIconKind.MicrosoftTeams, "Microsoft Teams"),
                (MaterialIconKind.MicrosoftVisualStudio, "Visual Studio"),
            };

            foreach ((MaterialIconKind kind, string name) in examples)
            {
                AppClosureDisplayItem appClosureDisplayItem = new AppClosureDisplayItem(name, null, kind);
                AppsToCloseControls.Add(CreateAppClosureControl(appClosureDisplayItem));
            }
        }

        private static Icon? ExtractEmbeddedIcon(string exePath)
        {
            IntPtr[] large = new IntPtr[1];
            IntPtr[] small = new IntPtr[1];
            uint count = NativeMethods.ExtractIconEx(exePath, 0, large, small, 1);
            try
            {
                if (count == 0 || count == uint.MaxValue)
                    return null;
                IntPtr handle = large[0] != IntPtr.Zero ? large[0] : small[0];
                if (handle == IntPtr.Zero)
                    return null;
                // Clone into a new Icon that owns its own handle before we destroy the originals
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                foreach (IntPtr h in large) if (h != IntPtr.Zero) NativeMethods.DestroyIcon(h);
                foreach (IntPtr h in small) if (h != IntPtr.Zero) NativeMethods.DestroyIcon(h);
            }
        }

        private static string ResolveAppxDisplayName(string packageDir, string displayName, string? packageName)
        {
            if (!displayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                return displayName;

            string packageFullName = new DirectoryInfo(packageDir).Name;
            HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                displayName,
            };

            string resourceKey = displayName.Substring("ms-resource:".Length).Trim();
            resourceKey = resourceKey.TrimStart('/');

            if (!string.IsNullOrWhiteSpace(resourceKey))
            {
                candidates.Add($"ms-resource:///{resourceKey}");

                if (!resourceKey.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
                    candidates.Add($"ms-resource:///resources/{resourceKey}");

                if (!string.IsNullOrWhiteSpace(packageName))
                {
                    candidates.Add($"ms-resource://{packageName}/{resourceKey}");

                    if (!resourceKey.StartsWith("resources/", StringComparison.OrdinalIgnoreCase))
                        candidates.Add($"ms-resource://{packageName}/resources/{resourceKey}");
                }
            }

            foreach (string candidate in candidates)
            {
                string indirect = $"@{{{packageFullName}?{candidate}}}";
                string? resolved = TryLoadIndirectString(indirect);
                if (!string.IsNullOrWhiteSpace(resolved) &&
                    !resolved.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                {
                    return resolved;
                }
            }

            return displayName;
        }

        private static string? TryLoadIndirectString(string source)
        {
            StringBuilder buffer = new StringBuilder(1024);
            int result = NativeMethods.SHLoadIndirectString(source, buffer, (uint)buffer.Capacity, IntPtr.Zero);
            if (result != 0)
                return null;

            string resolved = buffer.ToString().Trim();
            return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
        }
    }
}