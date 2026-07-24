using Avalonia;
using Microsoft.Win32;
using SuiteUserPopup.Models.Config;
using SuiteUserPopup.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace SuiteUserPopup
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Show help if any argument contains '?'
            ConsoleHelper.AttachToParentConsole();
            if (args.Any(a => a.Contains('?')))
            {
                ShowHelp();
            }

            bool isBlockedNotice = args.Any(a => a.Equals("--Blocked", StringComparison.OrdinalIgnoreCase));
            string? blockedProcessName = TryGetArgValue(args, "--ProcessName") ?? TryGetArgValue(args, "-p");
            string? blockedExePath = ResolveBlockedExePath(args);
            string? blockedSuiteId = TryGetArgValue(args, "--SuiteId");

            string? configPath = ResolveConfigPath(args);
            string? companyLogoPath = ResolveLogoPath(args, "--CompanyLogo", "-cl", "CompanyLogo.png");
            string? suiteLogoPath = ResolveLogoPath(args, "--SuiteLogo", "-sl", "SuiteLogo.png");

            string logFilePath = ResolveLogFilePath(configPath);
            AppLogService.Initialize(logFilePath);
            AppLogService.Info("Application startup initiated.", nameof(Program));

            // IFEO's Debugger hijack intercepts every launch attempt regardless of who's asking — including
            // the suite's own elevated install steps (an MSI custom action, a post-install launch, etc.)
            // that legitimately need to run the exe being blocked for the interactive user. Since Windows
            // creates this substitute process using the SAME token as whoever tried to launch the real exe,
            // an elevated/SYSTEM caller here means it's the suite's own machinery, not the interactive user
            // — so let it through instead of showing the notice.
            if (isBlockedNotice && IsElevatedOrSystem())
            {
                AppLogService.Info($"Blocked launch of {blockedProcessName} came from an elevated/SYSTEM caller; bypassing the notice and letting it run.", nameof(Program));
                RelaunchRealExeBypassingBlock(args, blockedProcessName);
                return;
            }

            // A blocked-process notice is fired ad hoc from the IFEO Debugger substitution when a user
            // tries to launch a blocked exe, so it can't depend on a fully configured popup: config/logos
            // are used for branding if present, but their absence must not stop the notice from showing.
            if (!isBlockedNotice)
            {
                if (configPath is null)
                {
                    AppLogService.Error("Config argument missing and popconfig.json could not be found.", nameof(Program));
                    Console.WriteLine("Config argument missing, a popconfig.json is required as it gives instructions to the popup on what to say etc");
                    Environment.Exit(2);
                    return;
                }
                if (companyLogoPath is null)
                {
                    AppLogService.Error("Company logo argument missing and fallback logo could not be found.", nameof(Program));
                    Console.WriteLine("CompanyLogo argument missing, and unable to find a CompanyLogo png or gif in the exe directory");
                    Environment.Exit(2);
                    return;
                }
                if (suiteLogoPath is null)
                {
                    AppLogService.Error("Suite logo argument missing and fallback logo could not be found.", nameof(Program));
                    Console.WriteLine("SuiteLogo argument missing, and unable to find a SuiteLogo png in the exe directory");
                    Environment.Exit(2);
                    return;
                }
            }

            StartupOptions.Set(configPath, companyLogoPath ?? string.Empty, suiteLogoPath ?? string.Empty, isBlockedNotice, blockedProcessName, blockedExePath, blockedSuiteId);
            AppLogService.Info("Startup options resolved successfully.", nameof(Program));

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        private static string? ResolveConfigPath(string[] args)
        {
            // Explicit argument takes priority
            string? fromArg = TryGetArgValue(args, "--Config") ?? TryGetArgValue(args, "-cfg");
            if (!string.IsNullOrWhiteSpace(fromArg) && File.Exists(fromArg))
                return fromArg;

            // Fall back to popconfig.json next to the exe
            string candidate = Path.Combine(AppContext.BaseDirectory, "popconfig.json");
            return File.Exists(candidate) ? candidate : null;
        }

        private static string? ResolveLogoPath(string[] args, string longKey, string shortKey, string defaultFileName)
        {
            string? fromArg = TryGetArgValue(args, longKey) ?? TryGetArgValue(args, shortKey);
            if (!string.IsNullOrWhiteSpace(fromArg) && File.Exists(fromArg))
                return fromArg;

            string defaultPath = Path.Combine(AppContext.BaseDirectory, defaultFileName);
            return File.Exists(defaultPath) ? defaultPath : null;
        }

        private static string ResolveLogFilePath(string? configPath)
        {
            string defaultLogFilePath = Path.Combine(AppContext.BaseDirectory, "Logs", "SuiteUserPopup.log");

            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
                return defaultLogFilePath;

            try
            {
                PopConfig? config = popConfigLoader.LoadFromJsonFile(configPath);
                if (config is null || string.IsNullOrWhiteSpace(config.LogFilePath))
                    return defaultLogFilePath;

                if (Path.IsPathRooted(config.LogFilePath))
                    return config.LogFilePath;

                string? configDirectory = Path.GetDirectoryName(configPath);
                if (string.IsNullOrWhiteSpace(configDirectory))
                    return defaultLogFilePath;

                return Path.GetFullPath(Path.Combine(configDirectory, config.LogFilePath));
            }
            catch
            {
                return defaultLogFilePath;
            }
        }

        private static bool IsElevatedOrSystem()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) == true)
                return true;

            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        // IFEO has no per-caller exemption, so the only way to let a legitimate elevated/SYSTEM launch
        // through is to briefly remove the block ourselves, start the real exe, then put it back — just
        // long enough for CreateProcess to load the real image, which isn't retroactively affected by the
        // registry value being restored afterwards. Best-effort throughout: if anything here fails, the
        // caller's launch attempt fails silently, same as it would have before this notice existed.
        private static void RelaunchRealExeBypassingBlock(string[] args, string? processExe)
        {
            if (string.IsNullOrWhiteSpace(processExe))
                return;

            int markerIndex = Array.FindIndex(args, a => a == "--");
            if (markerIndex < 0 || markerIndex + 1 >= args.Length)
                return;

            string realExePath = args[markerIndex + 1];
            if (!File.Exists(realExePath))
                return;

            string regPath = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{processExe}";
            string? savedDebuggerValue = null;
            try
            {
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(regPath, true))
                {
                    savedDebuggerValue = key?.GetValue("Debugger") as string;
                    key?.DeleteValue("Debugger", false);
                }

                ProcessStartInfo psi = new ProcessStartInfo(realExePath) { UseShellExecute = false };
                for (int i = markerIndex + 2; i < args.Length; i++)
                    psi.ArgumentList.Add(args[i]);

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppLogService.Error($"Failed to relaunch {realExePath} past the block: {ex.Message}", nameof(Program));
            }
            finally
            {
                if (!string.IsNullOrEmpty(savedDebuggerValue))
                {
                    try
                    {
                        using RegistryKey? key = Registry.LocalMachine.CreateSubKey(regPath, true);
                        key?.SetValue("Debugger", savedDebuggerValue, RegistryValueKind.String);
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Error($"Failed to restore the block on {processExe} after an elevated bypass: {ex.Message}", nameof(Program));
                    }
                }
            }
        }

        // When --Blocked fires from the IFEO Debugger substitution, Windows appends the real target exe's
        // own full path (and its original arguments) right after whatever we put in the Debugger value.
        // ProcClosureExecEvent.BuildDebuggerValue marks the end of its own args with a literal "--", so
        // the appended data always starts right after that marker regardless of how many of our own
        // switches precede it. Used only to extract the exe's icon, so any mismatch here just means no
        // icon rather than a startup failure.
        private static string? ResolveBlockedExePath(string[] args)
        {
            int markerIndex = Array.FindIndex(args, a => a == "--");
            if (markerIndex < 0 || markerIndex + 1 >= args.Length)
                return null;

            string candidate = args[markerIndex + 1];
            return File.Exists(candidate) ? candidate : null;
        }

        private static string? TryGetArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];

                if (a.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return args[i + 1];
                    return null;
                }

                // Support --key=value
                if (a.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return a[(key.Length + 1)..];
                }
            }

            return null;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("SuiteUserPopup Usage:\n");
            Console.WriteLine("  --Config <path> or -cfg <path>         : Path to Config JSON file");
            Console.WriteLine("  --CompanyLogo <path> or -cl <path>      : Path to Company logo PNG file");
            Console.WriteLine("  --SuiteLogo <path> or -sl <path>        : Path to Suite logo PNG file");
            Console.WriteLine("  LogFilePath in popconfig.json           : Optional path to the log file");
            Console.WriteLine("  --Blocked                                : Show a lightweight notice that a process was blocked, instead of the full popup");
            Console.WriteLine("  --ProcessName <name> or -p <name>       : Name of the blocked process, used with --Blocked");
            Console.WriteLine("  --SuiteId <id>                           : Suite's stable ID, tagged onto the window title so a later unblock can find and close it");
            Console.WriteLine();
            Console.WriteLine("If no parameters are provided, the app will look for 'popconfig.json', 'CompanyLogo.png', and 'SuiteLogo.png' in the executable directory.");
            AppLogService.Info("Application help text displayed.", nameof(Program));
            Environment.Exit(0);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .WithDeveloperTools()
                .LogToTrace();
    }
}
