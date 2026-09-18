using Avalonia.Media;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Models.Common
{
    /// <summary>One "Label: Value" line shown beneath an item in the read-only Suite Viewer.</summary>
    public class SuiteConfigDetail
    {
        public required string Label { get; init; }
        public required string Value { get; init; }
    }

    /// <summary>A single package, event, rule set or settings block as displayed in the Suite Viewer.</summary>
    public class SuiteConfigItem
    {
        public required string Title { get; init; }
        public string? SubTitle { get; init; }
        public required MaterialIconKind IconKind { get; init; }
        public required IBrush IconColor { get; init; }
        public List<SuiteConfigDetail> Details { get; init; } = new List<SuiteConfigDetail>();
        public bool HasSubTitle => !string.IsNullOrWhiteSpace(SubTitle);
        public bool HasDetails => Details.Count > 0;

        // Only set for a PowerShell event item. The full script text is already embedded in the suite's
        // SuiteConfig.scfg (see SuiteBuilder - ScriptDoc is written straight into the JSON at build time),
        // so the "View script" button in the Suite Viewer needs no further read of the suite exe at all.
        public string? ScriptContent { get; init; }
        public string? ScriptArgs { get; init; }
        public Contexts? ScriptContext { get; init; }
        public bool HasScriptContent => !string.IsNullOrWhiteSpace(ScriptContent);
    }

    /// <summary>A group of items in the Suite Viewer, e.g. "Packages" or "Registry", mirroring a nav pane tab.</summary>
    public class SuiteConfigSection
    {
        public required string Header { get; init; }
        public required MaterialIconKind IconKind { get; init; }
        public required IBrush IconColor { get; init; }
        public List<SuiteConfigItem> Items { get; init; } = new List<SuiteConfigItem>();
        public int Count => Items.Count;
    }

    /// <summary>Everything the Suite Viewer page needs to describe one built suite, derived purely from its SuiteConfig.scfg.</summary>
    public class SuiteConfigSummary
    {
        public string? ProjectName { get; init; }
        public string? BuildSummary { get; init; }
        public int PackageCount { get; init; }
        public int EventCount { get; init; }
        public int RuleSetCount { get; init; }
        public List<SuiteConfigSection> Sections { get; init; } = new List<SuiteConfigSection>();
    }
}
