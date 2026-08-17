using Avalonia;
using Avalonia.Media;
using SuiteProgressPopup.Services;
using System;
using System.IO;
using System.Linq;

namespace SuiteProgressPopup
{
    internal class Program
    {
        private static readonly string DefaultLogPath = @"C:\Modern-Workplace-Logs\SuiteProgressPopup.log";

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

            string? logFilePath = ResolveLogFilePath(args);
            string? suiteLogoPath = ResolveLogoPath(args, "--SuiteLogo", "-sl", "SuiteLogo.png");
            string? progressFilePath = ResolveProgressFilePath(args);
            SolidColorBrush progressColourBrush = ResolveProgColourBrush(args);
            bool isLockdown = HasFlag(args, "--Lockdown", "-lk");
            string? companyLogoPath = TryGetArgValue(args, "--CompanyLogo") ?? TryGetArgValue(args, "-cl");
            int lockdownMaxMinutes = ResolveLockdownMaxMinutes(args);
            string? lockdownMessage = TryGetArgValue(args, "--LockdownMessage") ?? TryGetArgValue(args, "-lm");

            AppLogService.Initialize(logFilePath);
            AppLogService.Info("Application startup initiated.", "SuiteProgressPopup");

            if (suiteLogoPath is null)
            {
                AppLogService.Error("Suite logo argument missing and fallback logo could not be found.", "SuiteProgressPopup");
                Console.WriteLine("SuiteLogo argument missing, and unable to find a SuiteLogo png in the exe directory");
                Environment.Exit(2);
                return;
            }
            if (progressFilePath is null)
            {
                AppLogService.Error("ProgressFile argument missing.", "SuiteProgressPopup");
                Console.WriteLine("ProgressFile argument missing, a path to a progress.json file is required so this popup knows what to display");
                Environment.Exit(2);
                return;
            }

            StartupOptions.Set(suiteLogoPath, progressFilePath, progressColourBrush, isLockdown, companyLogoPath, lockdownMaxMinutes, lockdownMessage);
            AppLogService.Info($"Startup options resolved successfully. Lockdown={isLockdown}", "SuiteProgressPopup");

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        private static string? ResolveLogoPath(string[] args, string longKey, string shortKey, string defaultFileName)
        {
            string? fromArg = TryGetArgValue(args, longKey) ?? TryGetArgValue(args, shortKey);
            if (!string.IsNullOrWhiteSpace(fromArg) && File.Exists(fromArg))
                return fromArg;

            string defaultPath = Path.Combine(AppContext.BaseDirectory, defaultFileName);
            return File.Exists(defaultPath) ? defaultPath : null;
        }

        private static string? ResolveProgressFilePath(string[] args)
        {
            string? fromArg = TryGetArgValue(args, "--ProgressFile") ?? TryGetArgValue(args, "-pf");
            if (!string.IsNullOrWhiteSpace(fromArg))
                return fromArg;

            string defaultPath = Path.Combine(AppContext.BaseDirectory, "progress.json");
            return defaultPath;
        }

        private static SolidColorBrush ResolveProgColourBrush(string[] args)
        {
            string? fromArg = TryGetArgValue(args, "--ProgressColour")
                              ?? TryGetArgValue(args, "-pc");

            if (!string.IsNullOrWhiteSpace(fromArg))
            {
                try
                {
                    return new SolidColorBrush(Color.Parse(fromArg));
                }
                catch
                {
                    // Invalid colour string, fallback to default
                }
            }

            return new SolidColorBrush(Colors.DarkGreen);
        }

        private static string? ResolveLogFilePath(string[] args)
        {
            string? fromArg = TryGetArgValue(args, "--LogFile") ?? TryGetArgValue(args, "-l");
            if (!string.IsNullOrWhiteSpace(fromArg))
                return fromArg;
            return DefaultLogPath;
        }

        private static int ResolveLockdownMaxMinutes(string[] args)
        {
            string? fromArg = TryGetArgValue(args, "--MaxMinutes") ?? TryGetArgValue(args, "-mm");
            if (!string.IsNullOrWhiteSpace(fromArg) && int.TryParse(fromArg, out int minutes) && minutes > 0)
                return minutes;
            return 30;
        }

        private static bool HasFlag(string[] args, string longKey, string shortKey)
        {
            foreach (string a in args)
            {
                if (a.Equals(longKey, StringComparison.OrdinalIgnoreCase) || a.Equals(shortKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
            Console.WriteLine("SuiteProgressPopup Usage:\n");
            Console.WriteLine("  --SuiteLogo <path> or -sl <path>        : Path to Suite logo PNG file");
            Console.WriteLine("  --ProgressFile <path> or -pf <path>     : Path to a progress JSON file this popup will poll for updates");
            Console.WriteLine("  --LogFile <path> or -l <path>          : Path to log file for this popup");
            Console.WriteLine("  --Lockdown or -lk                       : Show a fullscreen, topmost lockdown popup instead of the small progress toast");
            Console.WriteLine("  --CompanyLogo <path> or -cl <path>      : Path to a company logo image shown centered in lockdown mode");
            Console.WriteLine("  --MaxMinutes <n> or -mm <n>              : Maximum minutes lockdown mode may run before it self-terminates (default 30)");
            Console.WriteLine("  --LockdownMessage <text> or -lm <text>  : Message shown underneath the progress bar for the duration of lockdown mode");
            Console.WriteLine();
            Console.WriteLine("If no parameters are provided, the app will look for 'SuiteLogo.png' and 'progress.json' in the executable directory.");
            AppLogService.Info("Application help text displayed.", "SuiteProgressPopup");
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
