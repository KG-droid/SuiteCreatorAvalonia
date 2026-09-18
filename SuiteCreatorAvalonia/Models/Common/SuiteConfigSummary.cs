using Avalonia.Media;
using Material.Icons;
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
