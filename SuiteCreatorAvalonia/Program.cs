using Avalonia;
using SuiteCreatorAvalonia.Services;
using System;
using System.Reflection;

namespace SuiteCreatorAvalonia
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            AppLog.RegisterGlobalExceptionHandlers();
            try
            {
                AppLog.Info($"SuiteCreator {Assembly.GetEntryAssembly()?.GetName().Version} starting");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                AppLog.Info("SuiteCreator exited");
            }
            catch (Exception ex)
            {
                AppLog.Error("Fatal error, application terminated", ex);
                throw;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .WithDeveloperTools();
    }
}
