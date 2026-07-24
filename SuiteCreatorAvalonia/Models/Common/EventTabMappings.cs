using Avalonia.Media;
using Material.Icons;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.ViewModels;
using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class EventTabMappings
    {
        public static EventTab Packages { get; set; } = new EventTab
        {
            Header = "Packages",
            IconKind = MaterialIconKind.Package,
            IconColor = Brush.Parse("#c48806"),
            ContentType = typeof(PackageViewModel),
            ModelType = typeof(PackageBase),
        };

        public static EventTab Files { get; set; } = new EventTab
        {
            Header = "Files & Folders",
            IconKind = MaterialIconKind.Files,
            IconColor = Brush.Parse("#488a01"),
            ContentType = typeof(FilesViewModel),
            ModelType = typeof(FileSysIO),
        };

        public static EventTab Registry { get; set; } = new EventTab
        {
            Header = "Registry",
            IconKind = MaterialIconKind.CubeUnfolded,
            IconColor = Brush.Parse("#1e9df7"),
            ContentType = typeof(RegistryViewModel),
            ModelType = typeof(Registry),
        };

        public static SettingsTab Popup { get; set; } = new SettingsTab
        {
            Header = "Popups",
            IconKind = MaterialIconKind.Application,
            IconColor = Brush.Parse("#727375"),
            ContentType = typeof(PopupsViewModel),
            ModelType = typeof(Popup),
        };

        public static EventTab Certs { get; set; } = new EventTab
        {
            Header = "Certs",
            IconKind = MaterialIconKind.Certificate,
            IconColor = Brush.Parse("#a17f03"),
            ContentType = typeof(CertsViewModel),
            ModelType = typeof(Cert),
        };

        public static EventTab Environments { get; set; } = new EventTab
        {
            Header = "Environment",
            IconKind = MaterialIconKind.InvoiceList,
            IconColor = Brush.Parse("#9e00ff"),
            ContentType = typeof(EnvViewModel),
            ModelType = typeof(SuiteCreatorAvalonia.Models.Events.Environment),
        };

        public static EventTab Executables { get; set; } = new EventTab
        {
            Header = "Executables",
            IconKind = MaterialIconKind.CodeArray,
            IconColor = Brush.Parse("#01a698"),
            ContentType = typeof(ExecViewModel),
            ModelType = typeof(Executable),
        };

        public static EventTab Extensions { get; set; } = new EventTab
        {
            Header = "Extensions",
            IconKind = MaterialIconKind.Extension,
            IconColor = Brush.Parse("#0254b3"),
            ContentType = typeof(BrowserExtViewModel),
            ModelType = typeof(BrowserExt),
        };

        public static EventTab ServiceClosures { get; set; } = new EventTab
        {
            Header = "ServiceClosures",
            IconKind = MaterialIconKind.CogOff,
            IconColor = Brush.Parse("#c92506"),
            ContentType = typeof(ServClosuresViewModel),
            ModelType = typeof(ServiceClosure),
        };

        public static EventTab ProcessClosures { get; set; } = new EventTab
        {
            Header = "ProcessClosures",
            IconKind = MaterialIconKind.CloseBoxes,
            IconColor = Brush.Parse("#c92506"),
            ContentType = typeof(ProcClosuresViewModel),
            ModelType = typeof(ProcessClosure),
        };

        public static EventTab Shortcuts { get; set; } = new EventTab
        {
            Header = "Shortcuts",
            IconKind = MaterialIconKind.FileLink,
            IconColor = Brush.Parse("#488a01"),
            ContentType = typeof(ShortcutsViewModel),
            ModelType = typeof(Shortcut),
        };

        public static EventTab PSScripts { get; set; } = new EventTab
        {
            Header = "PS Scripts",
            IconKind = MaterialIconKind.Powershell,
            IconColor = Brush.Parse("#0497b5"),
            ContentType = typeof(PowerShellViewModel),
            ModelType = typeof(PowerShell),
        };

        public static EventTab Drivers { get; set; } = new EventTab
        {
            Header = "Drivers",
            IconKind = MaterialIconKind.Chip,
            IconColor = Brush.Parse("#2e8b57"),
            ContentType = typeof(DriversViewModel),
            ModelType = typeof(Driver),
        };

        public static EventTab Rules { get; set; } = new EventTab
        {
            Header = "Rules",
            IconKind = MaterialIconKind.Legal,
            IconColor = Brush.Parse("#a17f03"),
            ContentType = typeof(RulesViewModel),
            ModelType = typeof(Rules.RuleSet),
        };

        public static SettingsTab Build { get; set; } = new SettingsTab
        {
            Header = "Build",
            IconKind = MaterialIconKind.PackageVariant,
            IconColor = Brush.Parse("#c48806"),
            ContentType = typeof(BuildViewModel),
            Pinned = true,
            ModelType = typeof(Build),
        };

        public static SettingsTab Settings { get; set; } = new SettingsTab
        {
            Header = "Settings",
            IconKind = MaterialIconKind.SettingsApplications,
            IconColor = Brushes.Gray,
            ContentType = typeof(SettingsViewModel),
            Pinned = true,
            ModelType = typeof(UISettings),
        };

        public class Tab
        {
            public required string Header { get; init; }
            public required MaterialIconKind IconKind { get; init; }
            public required IBrush IconColor { get; init; }
            public required Type ContentType { get; init; }
            public required Type ModelType { get; init; }
            public bool Pinned { get; init; } = false;
        }

        public class EventTab : Tab
        {
            public List<object>? List { get; set; } = new List<object>();
        }

        public class SettingsTab : Tab
        {
        }

        public static EventTab? GetEventTabByModelType(Type modelType)
        {
            foreach (var prop in typeof(EventTabMappings).GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public))
            {
                if (prop.PropertyType == typeof(EventTab))
                {
                    var tab = prop.GetValue(null) as EventTab;
                    if (tab != null && tab.ModelType.IsAssignableFrom(modelType))
                        return tab;
                }
            }
            return null;
        }
    }
}
