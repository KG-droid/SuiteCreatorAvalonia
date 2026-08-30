using Avalonia.Controls;
using Avalonia.Labs.Gif;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Tools;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class PopupsViewModel : ViewModelBase
    {
        private readonly SuiteCoreManager _suiteCoreManager;
        private readonly AppSettingsControl _uISettingsControl;
        private bool _isLoading = false;

        // Backs the Esc-to-cancel behaviour on the fullscreen lockdown test - a global low-level
        // keyboard hook so Esc works no matter which window (the lockdown popup, not us) has focus.
        // Keeps the delegate rooted for as long as the hook is installed, since SetWindowsHookEx does
        // not itself prevent the GC from collecting it.
        private IntPtr _lockdownTestHookHandle = IntPtr.Zero;
        private NativeMethods.LowLevelKeyboardProc? _lockdownTestKeyboardProc;
        private CancellationTokenSource? _lockdownTestCts;

        [ObservableProperty]
        private ViewFactory _viewFact; // Factory for creating view model for the Popups Preview in the PopupsView code behind

        [ObservableProperty]
        private bool _showProgress = false;

        [ObservableProperty]
        private bool _showPopupWarning = false;

        [ObservableProperty]
        private bool _showPopupPreview = true;

        [ObservableProperty]
        private bool _lockdownEnabled = false;

        [ObservableProperty]
        private int? _lockdownMaxMinutes = 30;

        [ObservableProperty]
        private string _lockdownMessage = "Please do not turn off your computer.";

        [ObservableProperty]
        private string? _lockdownTestError = null;

        [ObservableProperty]
        private bool _linkToProcClosures = true;

        [ObservableProperty]
        private bool _pauseDuringMeeting = true;

        [ObservableProperty]
        private TextDocument _pSCondition = new();

        [ObservableProperty]
        private bool _hasPopupCondition = false;

        [ObservableProperty]
        private bool _hasInstallPopup = true;

        [ObservableProperty]
        private bool _hasUninstallPopup = true;

        [ObservableProperty]
        private TextDocument _installTxt = new();

        [ObservableProperty]
        private string _instAction = "Upgrade";

        [ObservableProperty]
        private TextDocument _uninstallTxt = new();

        [ObservableProperty]
        private string _uninstAction = "Rollback";

        [ObservableProperty]
        private List<MaterialIconKind> _applicableIconKinds = new List<MaterialIconKind>
        {
            MaterialIconKind.Update,
            MaterialIconKind.CloseCircle,
            MaterialIconKind.Undo,
            MaterialIconKind.PackageVariantRemove,
            MaterialIconKind.PackageVariantPlus,
            MaterialIconKind.PackageVariantMinus,
            MaterialIconKind.PackageDown,
            MaterialIconKind.PackageUp,
            MaterialIconKind.OpenInApp,
            MaterialIconKind.Application,
            MaterialIconKind.ApplicationEdit,
            MaterialIconKind.ApplicationImport,
            MaterialIconKind.ArrowLeft,
            MaterialIconKind.ArrowLeftCircle,
            MaterialIconKind.ArrowLeftThick,
            MaterialIconKind.ArrowULeftTopBold,
            MaterialIconKind.ArrowUpCircle,
            MaterialIconKind.ArrowUpThick,
            MaterialIconKind.ArrowUpCircleOutline,
            MaterialIconKind.MonitorArrowDown,
            MaterialIconKind.MonitorArrowDownVariant,
            MaterialIconKind.RotateLeft,
            MaterialIconKind.RotateRight,
            MaterialIconKind.Restore,
            MaterialIconKind.Reload,
        };

        [ObservableProperty]
        private MaterialIconKind _selectedDeployIconKind = MaterialIconKind.PackageUp;

        [ObservableProperty]
        private MaterialIconKind _selectedRemoveIconKind = MaterialIconKind.Restore;

        [ObservableProperty]
        private int? _timer = 40;

        [ObservableProperty]
        private PopupAction _timerExpireAction = PopupAction.Continue;

        [ObservableProperty]
        private int? _delayDays = 7;

        [ObservableProperty]
        private CompanyLogoPickerView _companyLogoPicker = new();

        [ObservableProperty]
        private Bitmap? _suiteLogo = null;

        [ObservableProperty]
        private string? _suiteLogoBase64 = null;

        [ObservableProperty]
        private string? _suiteLogoError = null;

        [ObservableProperty]
        private PopupAction _loggedOffAction = PopupAction.Continue;

        [ObservableProperty]
        private PopupAction _lockedAction = PopupAction.Continue;

        [ObservableProperty]
        private PopupAction _espAction = PopupAction.Continue;

        [ObservableProperty]
        private SuiteAction _showSampleFor = SuiteAction.Deployment;

        partial void OnShowProgressChanged(bool value)
        {
            // Only surface this the moment an admin actually flips it on (not while settings are
            // still being loaded from disk) - it's a one-time pointer to the per-package Estimated
            // Duration fields that only become visible once this toggle is enabled.
            if (_isLoading || !value) return;
            _ = this.ShowDialogAsync(new ProgressTimingInfoViewModel());
        }

        partial void OnHasInstallPopupChanged(bool value)
        {
            SetDefaultShowSampleFor();
        }

        partial void OnHasUninstallPopupChanged(bool value)
        {
            SetDefaultShowSampleFor();
        }

        public PopupsViewModel() : this(new ViewFactory(type => { return (ViewModelBase)Activator.CreateInstance(type)!; }), new AppSettingsControl(), new SuiteCoreManager())
        {
        }

        public PopupsViewModel(ViewFactory viewFactory, AppSettingsControl uISettingsControl, SuiteCoreManager suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
            _uISettingsControl = uISettingsControl;
            ViewFact = viewFactory;
            CompanyLogoPicker.DataContext = viewFactory.GetVM(typeof(CompanyLogoPickerViewModel));
            SetupEvents();
            LoadPopupSettings();
            PropertyChanged += (sender, args) => SavePopupSettings();
        }

        private void SetDefaultShowSampleFor()
        {
            if (HasInstallPopup)
                ShowSampleFor = SuiteAction.Deployment;
            else
                ShowSampleFor = SuiteAction.Removal;
        }

        private void SetupEvents()
        {
            InstallTxt.TextChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(InstallTxt));
            };
            UninstallTxt.TextChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(InstallTxt));
            };
            PSCondition.TextChanged += (sender, args) =>
            {
                OnPropertyChanged(nameof(PSCondition));
            };
        }

        [RelayCommand]
        private void AddInstallPop()
        {
            HasInstallPopup = true;
        }

        [RelayCommand]
        private void RemoveInstallPop()
        {
            HasInstallPopup = false;
        }

        [RelayCommand]
        private void AddUninstallPop()
        {
            HasUninstallPopup = true;
        }

        [RelayCommand]
        private void RemoveUninstallPop()
        {
            HasUninstallPopup = false;
        }

        [RelayCommand]
        private async Task ChangeSuiteLogo()
        {
            IEnumerable<string>? result = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Select a Logo", FileTypeFilter = SysIOPickerTypes.ImageIncGif });
            if (result != null && result.Any())
            {
                string filePath = result.First();
                try
                {
                    byte[] bmpBytes = ImageLoader.GetBytes(new Uri(filePath));
                    byte[]? LogoBytes = bmpBytes;
                    MemoryStream ms = new(bmpBytes);
                    Bitmap bitmap = new(ms);
                    SuiteLogo = bitmap;
                    SuiteLogoBase64 = ImageLoader.GetBase64(SuiteLogo);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load suite logo image from: {filePath}", ex, "Popups");
                    Dispatcher.UIThread.Post(() =>
                    {
                        SuiteLogoError = $"Error loading the provided image: {ex.Message}";
                    });
                    return;
                }
            }
        }

        [RelayCommand]
        private void SetDefaultSuiteLogo()
        {
            SuiteLogo = ImageLoader.Get(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/SuiteLogo.png"));
            SuiteLogoBase64 = ImageLoader.GetBase64(SuiteLogo);
        }

        [RelayCommand]
        private async Task ShowPopupConditionExamples()
        {
            await this.ShowDialogAsync(new PopupConditionExamplesViewModel());
        }

        [RelayCommand]
        private void TestLockdownPopup()
        {
            LockdownTestError = null;
            // A previous test may still be mid-sequence (e.g. the button was clicked again) - stop it
            // and drop its hook before starting a fresh one.
            _lockdownTestCts?.Cancel();
            UninstallLockdownTestEscapeHook();
            try
            {
                string exeDir = Path.Combine(AppContext.BaseDirectory, "SuiteExec");
                string exePath = Path.Combine(exeDir, "SuiteProgressPopup.exe");
                if (!File.Exists(exePath))
                {
                    LockdownTestError = "SuiteProgressPopup.exe wasn't found in the published SuiteExec folder - publish the app first.";
                    AppLog.Warning($"Lockdown test could not find: {exePath}", "Popups");
                    return;
                }

                string testDir = Path.Combine(Path.GetTempPath(), "SuiteCreatorLockdownTest");
                Directory.CreateDirectory(testDir);
                string progressFilePath = Path.Combine(testDir, "progress.json");
                string logFilePath = Path.Combine(testDir, "SuiteProgressPopup.log");
                string suiteLogoPath = Path.Combine(testDir, "SuiteLogo.png");

                // SuiteLogo.png is deliberately excluded from the popup's own publish output
                // (SuiteProgressPopup.csproj: CopyToPublishDirectory=Never) - a real suite run always
                // supplies --SuiteLogo itself, so write out the currently configured logo for the test.
                if (SuiteLogo is not null)
                    SuiteLogo.Save(suiteLogoPath);

                string companyLogoPath = SaveCurrentCompanyLogo(testDir);

                WriteLockdownTestProgress(progressFilePath, 0, "Preparing example package...", isComplete: false);

                string arguments = $"--SuiteLogo \"{suiteLogoPath}\" --CompanyLogo \"{companyLogoPath}\" --ProgressFile \"{progressFilePath}\" --LogFile \"{logFilePath}\" --Lockdown --MaxMinutes \"1\" --LockdownMessage \"{EscapeArgument(LockdownMessage)}\"";

                Process.Start(new ProcessStartInfo(exePath, arguments)
                {
                    WorkingDirectory = exeDir,
                    UseShellExecute = false
                });

                CancellationTokenSource cts = new();
                _lockdownTestCts = cts;
                InstallLockdownTestEscapeHook(progressFilePath, cts);

                _ = Task.Run(() =>
                {
                    RunLockdownTestProgressSequence(progressFilePath, cts.Token);
                    Dispatcher.UIThread.Post(() => UninstallLockdownTestEscapeHook(cts));
                });
            }
            catch (Exception ex)
            {
                LockdownTestError = $"Failed to launch the lockdown test: {ex.Message}";
                AppLog.Error("Failed to launch lockdown popup test", ex, "Popups");
            }
        }

        // Drives ~10 seconds of example progress against progress.json so the fullscreen
        // lockdown window has something to poll and animate without a real suite running -
        // mirrors how SuiteExecutor writes progress in Suite.Progress.cs. Bails out early (without
        // the final "complete" write) if Esc cancelled it - InstallLockdownTestEscapeHook already
        // wrote its own completion status in that case.
        private static void RunLockdownTestProgressSequence(string progressFilePath, CancellationToken token)
        {
            (int Percent, string Status)[] steps =
            {
                (15, "Preparing example package..."),
                (35, "Installing example package 1 of 2..."),
                (60, "Installing example package 2 of 2..."),
                (85, "Applying configuration..."),
            };

            foreach (var step in steps)
            {
                if (token.WaitHandle.WaitOne(2000)) return;
                WriteLockdownTestProgress(progressFilePath, step.Percent, step.Status, isComplete: false);
            }

            if (token.WaitHandle.WaitOne(2000)) return;
            WriteLockdownTestProgress(progressFilePath, 100, "Example complete", isComplete: true);
        }

        // Global low-level keyboard hook, active only while a lockdown test is running, so Esc can end
        // the test early even though the fullscreen lockdown window (a separate process) has focus -
        // avoids touching SuiteProgressPopup itself. Rather than killing the process, it writes a
        // completion status to the same progress.json the popup already polls, so it exits through its
        // own existing completion/exit path (ProgressWindowViewModel.ScheduleExit).
        private void InstallLockdownTestEscapeHook(string progressFilePath, CancellationTokenSource cts)
        {
            _lockdownTestKeyboardProc = (nCode, wParam, lParam) =>
            {
                if (nCode >= 0
                    && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN)
                    && Marshal.ReadInt32(lParam) == NativeMethods.VK_ESCAPE
                    && !cts.IsCancellationRequested)
                {
                    cts.Cancel();
                    WriteLockdownTestProgress(progressFilePath, 100, "Lockdown test cancelled", isComplete: true);
                    UninstallLockdownTestEscapeHook(cts);
                }
                return NativeMethods.CallNextHookEx(_lockdownTestHookHandle, nCode, wParam, lParam);
            };

            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule? curModule = curProcess.MainModule;
            _lockdownTestHookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _lockdownTestKeyboardProc,
                NativeMethods.GetModuleHandle(curModule?.ModuleName),
                0);
        }

        // cts is the token the hook/sequence was started with - guards against a fresh test's hook
        // (installed after a stale background continuation was already scheduled) being torn down by
        // that stale continuation instead of its own.
        private void UninstallLockdownTestEscapeHook(CancellationTokenSource? cts = null)
        {
            if (cts != null && !ReferenceEquals(cts, _lockdownTestCts))
                return;

            if (_lockdownTestHookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_lockdownTestHookHandle);
                _lockdownTestHookHandle = IntPtr.Zero;
            }
            _lockdownTestKeyboardProc = null;
        }

        private static class NativeMethods
        {
            internal const int WH_KEYBOARD_LL = 13;
            internal const int WM_KEYDOWN = 0x0100;
            internal const int WM_SYSKEYDOWN = 0x0104;
            internal const int VK_ESCAPE = 0x1B;

            internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr GetModuleHandle(string? lpModuleName);
        }

        private static void WriteLockdownTestProgress(string progressFilePath, int percentage, string statusText, bool isComplete)
        {
            JsonObject json = new JsonObject
            {
                ["Percentage"] = Math.Clamp(percentage, 0, 100),
                ["StatusText"] = statusText,
                ["ProductName"] = "Lockdown Test",
                ["IsComplete"] = isComplete,
                ["IsError"] = false
            };

            File.WriteAllText(progressFilePath, json.ToJsonString());
        }

        // Escapes a value for embedding inside a double-quoted Win32 command-line argument
        // (CommandLineToArgvW rules: a literal quote must be backslash-escaped).
        private static string EscapeArgument(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Mirrors the company-logo extraction SuiteBuilder.cs does for a real build: the logo is
        // admin-controlled (AppSettings.json), rendered as either a static Image or an animated
        // GifImage - pull the underlying bytes out of whichever one is active. Falls back to the same
        // bundled sample image the real build uses when no company logo has been configured.
        private string SaveCurrentCompanyLogo(string testDir)
        {
            string? companyLogoBase64 = _uISettingsControl.GetCompanyLogoBase64();
            if (!string.IsNullOrWhiteSpace(companyLogoBase64))
            {
                Control companyLogo = _uISettingsControl.GetCompanyLogo();
                if (companyLogo is GifImage gif && gif.Source is GifStreamSource gifStream)
                {
                    Stream gifSrc = gifStream.GetStream();
                    if (gifSrc.CanSeek) gifSrc.Position = 0;
                    string gifPath = Path.Combine(testDir, "CompanyLogo.gif");
                    using FileStream output = File.Create(gifPath);
                    gifSrc.CopyTo(output);
                    return gifPath;
                }
                if (companyLogo is Image img && img.Source is Bitmap bmp)
                {
                    string pngPath = Path.Combine(testDir, "CompanyLogo.png");
                    bmp.Save(pngPath);
                    return pngPath;
                }
            }

            string fallbackPath = Path.Combine(testDir, "CompanyLogo.png");
            SaveAvaloniaAssetToFile(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/SuiteCreatorLogoImage.png"), fallbackPath);
            return fallbackPath;
        }

        private static void SaveAvaloniaAssetToFile(Uri assetUri, string destinationPath)
        {
            using Stream assets = AssetLoader.Open(assetUri);
            using FileStream output = File.Create(destinationPath);
            assets.CopyTo(output);
        }

        private void LoadPopupSettings()
        {
            _isLoading = true;
            Popup popSettings = _suiteCoreManager.GetPopupSettings();
            ShowProgress = popSettings.ShowProgress;
            ShowPopupWarning = popSettings.ShowPopupWarning;
            LinkToProcClosures = popSettings.LinkToProcClosures;
            PauseDuringMeeting = popSettings.PauseDuringMeeting;
            // Decode PSCondition from Base64 if possible, else fallback to plain text
            string? psConditionRaw = popSettings.PSCondition;
            if (!string.IsNullOrEmpty(psConditionRaw))
            {
                try
                {
                    byte[] base64Bytes = Convert.FromBase64String(psConditionRaw);
                    string decoded = System.Text.Encoding.UTF8.GetString(base64Bytes);
                    PSCondition.Text = decoded;
                }
                catch (FormatException)
                {
                    // Not valid Base64, fallback to plain text
                    PSCondition.Text = string.Empty;
                }
            }
            else
            {
                PSCondition.Text = string.Empty;
            }
            InstallTxt.Text = popSettings.InstallTxt ?? "";
            UninstallTxt.Text = popSettings.UninstallTxt ?? "";
            Timer = popSettings.Timer > 0 ? popSettings.Timer : 40;
            TimerExpireAction = popSettings.TimerExpireAction ?? PopupAction.Continue;
            DelayDays = popSettings.DelayDays ?? 7;
            LoggedOffAction = popSettings.LoggedOffAction ?? PopupAction.Continue;
            LockedAction = popSettings.LockedAction ?? PopupAction.Continue;
            EspAction = popSettings.ESPAction ?? PopupAction.Continue;
            HasInstallPopup = popSettings.HasInstallTxt;
            HasUninstallPopup = popSettings.HasUninstallTxt;
            SelectedDeployIconKind = popSettings.DeployIconKind;
            SelectedRemoveIconKind = popSettings.RemoveIconKind;
            InstAction = popSettings.InstAction ?? "Upgrade";
            UninstAction = popSettings.UninstAction ?? "Rollback";
            HasPopupCondition = popSettings.HasPSCondition;
            SuiteLogoBase64 = popSettings.SuiteLogoBase64;
            if (string.IsNullOrWhiteSpace(popSettings.SuiteLogoBase64))
                SetDefaultSuiteLogo();
            else
                SuiteLogo = ImageLoader.GetFromBase64(popSettings.SuiteLogoBase64);
            LockdownEnabled = popSettings.LockdownEnabled;
            LockdownMaxMinutes = popSettings.LockdownMaxMinutes > 0 ? popSettings.LockdownMaxMinutes : 30;
            LockdownMessage = string.IsNullOrWhiteSpace(popSettings.LockdownMessage) ? "Please do not turn off your computer." : popSettings.LockdownMessage;
            _isLoading = false;
        }

        private void SavePopupSettings()
        {
            if (_isLoading) return; // Prevent saving while loading settings
            // Encode PSCondition.Text as Base64
            string psConditionBase64 = string.Empty;
            if (!string.IsNullOrEmpty(PSCondition.Text))
            {
                byte[] textBytes = System.Text.Encoding.UTF8.GetBytes(PSCondition.Text);
                psConditionBase64 = Convert.ToBase64String(textBytes);
            }
            Popup popSettings = new Popup
            {
                ShowProgress = ShowProgress,
                ShowPopupWarning = ShowPopupWarning,
                LinkToProcClosures = LinkToProcClosures,
                PauseDuringMeeting = PauseDuringMeeting,
                PSCondition = psConditionBase64,
                InstallTxt = InstallTxt.Text,
                UninstallTxt = UninstallTxt.Text,
                Timer = Timer != null ? (int)Timer : 40,
                TimerExpireAction = TimerExpireAction,
                DelayDays = DelayDays != null ? (int)DelayDays : 7,
                LoggedOffAction = LoggedOffAction,
                LockedAction = LockedAction,
                ESPAction = EspAction,
                DeployIconKind = SelectedDeployIconKind,
                HasInstallTxt = HasInstallPopup,
                HasUninstallTxt = HasUninstallPopup,
                RemoveIconKind = SelectedRemoveIconKind,
                InstAction = InstAction,
                UninstAction = UninstAction,
                HasPSCondition = HasPopupCondition,
                SuiteLogoBase64 = SuiteLogoBase64, // Saving takes too long if we dont cache this base64
                LockdownEnabled = LockdownEnabled,
                LockdownMaxMinutes = LockdownMaxMinutes != null ? (int)LockdownMaxMinutes : 30,
                LockdownMessage = LockdownMessage,
            };
            _suiteCoreManager.UpdatePopupSettings(popSettings);
        }
    }
}
