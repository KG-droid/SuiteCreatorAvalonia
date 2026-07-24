using Newtonsoft.Json;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using System;
using System.Collections.ObjectModel;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;

namespace SuiteCreatorAvalonia.Models.Common
{
    public enum SuiteProperty
    {
        Packages,
        SupportFiles,
        RuleSets,
        CertEvents,
        DriverEvents,
        ProcClosureEvents,
        EnvironmentEvents,
        ExecutableEvents,
        ExtensionEvents,
        FileEvents,
        PowerShellEvents,
        RegistryEvents,
        ServiceClosureEvents,
        ShortcutEvents,
        Stages,
        BuildSettings,
        PopupSettings
    }

    public class SuiteProjectConfig
    {
        public event EventHandler? ProjectNameChanged;

        private string? _projectName;
        public string? ProjectName
        {
            get => _projectName;
            set
            {
                if (_projectName != value)
                {
                    _projectName = value;
                    ProjectNameChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        public ObservableCollection<PackageBase> Packages { get; set; } = new();
        public ObservableCollection<RuleSet> RuleSets { get; set; } = new();
        public ObservableCollection<Cert> CertEvents { get; set; } = new();
        public ObservableCollection<Driver> DriverEvents { get; set; } = new();
        public ObservableCollection<ProcessClosure> ProcClosureEvents { get; set; } = new();
        public ObservableCollection<Environment> EnvironmentEvents { get; set; } = new();
        public ObservableCollection<Executable> ExecutableEvents { get; set; } = new();
        public ObservableCollection<BrowserExt> ExtensionEvents { get; set; } = new();
        public ObservableCollection<FileSysIO> FileEvents { get; set; } = new();
        public ObservableCollection<PowerShell> PowerShellEvents { get; set; } = new();
        public ObservableCollection<Registry> RegistryEvents { get; set; } = new();
        public ObservableCollection<ServiceClosure> ServiceClosureEvents { get; set; } = new();
        public ObservableCollection<Shortcut> ShortcutEvents { get; set; } = new();

        [JsonIgnore]
        public ObservableCollection<Stage>? Stages { get; set; }
        public Build BuildSettings { get; set; } = new Build
        {
            SuiteVersion = new Version(1, 0, 0, 0),
            Revision = 1,
            UpgradeCode = Guid.NewGuid(),
            FailSafe = false,
            RefreshEnv = false,
            KeepCache = false,
            FailureRetry = false,
            RestartOnFailure = false,
            ReverseUninstall = false,
        };
        public Popup PopupSettings { get; set; } = new();
    }
}
