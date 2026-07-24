using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Microsoft.Extensions.DependencyInjection;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace SuiteCreatorAvalonia
{
    public partial class App : Application
    {
        private static IServiceProvider _serviceProvider;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            SetupCustomTextHighlighting();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            try
            {
                ConfigureServices();

                var vm = _serviceProvider.GetRequiredService<MainWindowViewModel>();
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = vm
                    };

                    string? projectPathArg = desktop.Args?.FirstOrDefault(arg =>
                        !string.IsNullOrWhiteSpace(arg) &&
                        string.Equals(Path.GetExtension(arg), ".scp", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(arg));
                    if (projectPathArg != null && vm.MainView is MainViewModel mainVm)
                    {
                        mainVm.PendingOpenFilePath = projectPathArg;
                    }
                }
                else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
                {
                    singleViewPlatform.MainView = new MainView()
                    {
                        DataContext = vm
                    };
                }

                base.OnFrameworkInitializationCompleted();
            }
            catch (Exception ex)
            {
                AppLog.Error("Fatal error during application startup", ex);
                Environment.Exit(1603);
            }
        }

        private void ConfigureServices()
        {
            var collection = new ServiceCollection();

            collection.AddTransient<BrowserExtCardViewModel>();
            collection.AddTransient<BrowserExtViewModel>();
            collection.AddTransient<BuildViewModel>();
            collection.AddTransient<CertCardViewModel>();
            collection.AddTransient<CertsViewModel>();
            collection.AddTransient<CompanyLogoPickerViewModel>();
            collection.AddTransient<DriverCardViewModel>();
            collection.AddTransient<DriversViewModel>();
            collection.AddTransient<EnvCardViewModel>();
            collection.AddTransient<EnvViewModel>();
            collection.AddTransient<ExecCardViewModel>();
            collection.AddTransient<ExecViewModel>();
            collection.AddTransient<FileIOCardViewModel>();
            collection.AddTransient<FilesViewModel>();
            collection.AddTransient<FileSysPermissionsViewModel>();
            collection.AddTransient<FileTreeViewModel>();
            collection.AddTransient<IOEventViewModel>();

            collection.AddTransient<PackageViewModel>();
            collection.AddTransient<PackageMSIInstallDetailsViewModel>();
            collection.AddTransient<PackageMSIInstallSubDetailsViewModel>();
            collection.AddTransient<PackageMSIRemoveDetailsViewModel>();
            collection.AddTransient<PackageMSIxInstallDetailsViewModel>();
            collection.AddTransient<PackageMSIxInstallSubDetailsViewModel>();
            collection.AddTransient<PackageMSIxRemoveDetailsViewModel>();
            collection.AddTransient<PackageOtherInstallDetailsViewModel>();
            collection.AddTransient<PackageOtherRemoveDetailsViewModel>();

            collection.AddTransient<PathVarsTextBoxViewModel>();
            collection.AddTransient<PopupsViewModel>();
            collection.AddTransient<PowerShellCardViewModel>();
            collection.AddTransient<PowerShellViewModel>();
            collection.AddTransient<ProcClosureCardViewModel>();
            collection.AddTransient<ProcClosuresViewModel>();
            collection.AddTransient<PopupSampleWindowViewModel>();
            collection.AddTransient<RegCardViewModel>();
            collection.AddTransient<RegistryViewModel>();
            collection.AddTransient<RuleBuilderViewModel>();
            collection.AddTransient<RuleCardViewModel>();
            collection.AddTransient<RuleOperatorCardViewModel>();
            collection.AddTransient<RulesViewModel>();
            collection.AddTransient<ServiceClosureCardViewModel>();
            collection.AddTransient<ServClosuresViewModel>();
            collection.AddTransient<SettingsViewModel>();
            collection.AddTransient<ShortcutsViewModel>();
            collection.AddTransient<SupportFilesViewModel>();
            collection.AddTransient<UninstallsViewModel>();
            collection.AddSingleton<Func<Type, ViewModelBase>>(serviceProvider => type =>
            {
                if (!typeof(ViewModelBase).IsAssignableFrom(type))
                {
                    throw new ArgumentException($"Type must be a ViewModelBase, to prevent services being used within code that weren't brought in by a constructor. Type provided was: {type}");
                }
                return (ViewModelBase)serviceProvider.GetRequiredService(type);
            });
            collection.AddSingleton<DialogManager>();
            collection.AddSingleton<MainViewModel>();
            collection.AddSingleton<MainWindowViewModel>();
            collection.AddSingleton<SuiteCoreManager>();
            collection.AddSingleton<ThemeManager>();
            collection.AddSingleton<AppSettingsControl>();
            collection.AddSingleton<ViewFactory>();
            collection.AddSingleton<SuiteBuilder>();
            _serviceProvider = collection.BuildServiceProvider();

            // Load app UI settings
            AppSettingsControl settings = _serviceProvider.GetRequiredService<AppSettingsControl>();
            settings.LoadSettings();

            // Set the theme
            SetTheme();
        }

        private void SetTheme()
        {
            ThemeManager themeManager = _serviceProvider.GetRequiredService<ThemeManager>();
            AppSettingsControl uISettingsControl = _serviceProvider.GetRequiredService<AppSettingsControl>();
            ThemeVariant requestedTheme = (ThemeVariant)uISettingsControl.GetTheme();
            themeManager.SetTheme(requestedTheme);
            ThemeVariant actualTheme = themeManager.GetCurrentTheme();
            Color applicableColor;
            if (actualTheme.Equals(ThemeVariant.Dark))
            {
                applicableColor = (Color)uISettingsControl.GetDarkAccentColor();
            }
            else
            {
                applicableColor = (Color)uISettingsControl.GetLightAccentColor();
            }
            themeManager.SetAccentColor(applicableColor, actualTheme);
        }

        private void SetupCustomTextHighlighting()
        {
            var textHighlightDirURI = new Uri("avares://SuiteCreatorAvalonia/Assets/TextHighlighting");
            IEnumerable<Uri>? xshdFiles = AssetLoader.GetAssets(textHighlightDirURI, null).Where(uri => uri.AbsolutePath.EndsWith(".xshd", StringComparison.OrdinalIgnoreCase));
            foreach (Uri uri in xshdFiles)
            {
                using (Stream stream = AssetLoader.Open(uri))
                using (XmlReader reader = XmlReader.Create(stream))
                {
                    var xshd = HighlightingLoader.LoadXshd(reader);
                    var customHighlighting = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
                    HighlightingManager.Instance.RegisterHighlighting(Path.GetFileNameWithoutExtension(uri.LocalPath), null, customHighlighting);
                }
            }
        }
    }
}