using Avalonia.Media;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorControls.Text;
using SuiteOperations;
using SuiteOperations.Events;
using SuiteOperations.Package;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EventCore = SuiteCreatorAvalonia.Models.Events.EventCore;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>
    /// Turns a built suite's <see cref="SuiteExecConfig"/> into the read-only sections and items shown by the
    /// Suite Viewer page. Works purely from the config JSON, so nothing has to be extracted from the suite exe.
    /// Only sections that actually contain something are produced, mirroring the nav pane's tab order.
    /// </summary>
    public static class SuiteConfigSummariser
    {
        private static readonly IBrush RemovalIconColor = Brush.Parse("#c92506");
        private static readonly Regex PascalCaseBoundary = new Regex("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

        public static SuiteConfigSummary Summarise(SuiteExecConfig config)
        {
            Dictionary<Guid, string> stageNames = BuildStageNames(config);
            Dictionary<Guid, string> ruleSetNames = BuildRuleSetNames(config);

            List<SuiteConfigSection> sections = new List<SuiteConfigSection>();
            int eventCount = 0;

            SuiteConfigSection packages = BuildPackagesSection(config, ruleSetNames);
            if (packages.Count > 0) sections.Add(packages);

            AddEventSection(sections, EventTabMappings.Files, config.FileEvents, DescribeFileEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.Registry, config.RegistryEvents, DescribeRegistryEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.ProcessClosures, config.ProcClosureEvents, DescribeProcClosureEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.Certs, config.CertEvents, DescribeCertEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.Environments, config.EnvironmentEvents, DescribeEnvEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.Executables, config.ExecutableEvents, DescribeExecutableEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.Extensions, config.ExtensionEvents, DescribeExtensionEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.ServiceClosures, config.ServiceClosureEvents, DescribeServiceClosureEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.Shortcuts, config.ShortcutEvents, DescribeShortcutEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.PSScripts, config.PowerShellEvents, DescribePowerShellEvent, stageNames, ref eventCount);
            AddEventSection(sections, EventTabMappings.Drivers, config.DriverEvents, DescribeDriverEvent, stageNames, ref eventCount);

            SuiteConfigSection rules = BuildRulesSection(config);
            if (rules.Count > 0) sections.Add(rules);

            SuiteConfigSection? popups = BuildPopupSection(config.PopupSettings);
            if (popups != null) sections.Add(popups);

            sections.Add(BuildBuildSection(config.BuildSettings));

            Build build = config.BuildSettings ?? new Build();
            return new SuiteConfigSummary
            {
                ProjectName = config.ProjectName,
                BuildSummary = DescribeBuildSummary(build),
                PackageCount = packages.Count,
                EventCount = eventCount,
                RuleSetCount = rules.Count,
                Sections = sections,
            };
        }

        #region Lookups

        private static Dictionary<Guid, string> BuildStageNames(SuiteExecConfig config)
        {
            Dictionary<Guid, string> names = new Dictionary<Guid, string>();
            foreach (Stage stage in config.Stages ?? new List<Stage>())
            {
                if (!string.IsNullOrWhiteSpace(stage.Name)) names[stage.Id] = stage.Name;
            }
            // Packages are stages too, and are the authoritative name for their own Id.
            foreach (PackageBase pkg in config.Packages ?? new List<PackageBase>())
            {
                if (!string.IsNullOrWhiteSpace(pkg.Name)) names[pkg.Id] = pkg.Name;
            }
            names[Stage.StartStageId] = "Start";
            names[Stage.EndStageId] = "End";
            return names;
        }

        private static Dictionary<Guid, string> BuildRuleSetNames(SuiteExecConfig config)
        {
            Dictionary<Guid, string> names = new Dictionary<Guid, string>();
            foreach (RuleSet ruleSet in config.RuleSets ?? new List<RuleSet>())
            {
                if (!string.IsNullOrWhiteSpace(ruleSet.Name)) names[ruleSet.Id] = ruleSet.Name;
            }
            return names;
        }

        private static string? RuleSetName(Guid? id, Dictionary<Guid, string> ruleSetNames)
        {
            if (!id.HasValue || id.Value == Guid.Empty) return null;
            return ruleSetNames.TryGetValue(id.Value, out string? name) ? name : "Unknown rule set";
        }

        #endregion

        #region Packages

        private static SuiteConfigSection BuildPackagesSection(SuiteExecConfig config, Dictionary<Guid, string> ruleSetNames)
        {
            SuiteConfigSection section = NewSection(EventTabMappings.Packages);
            int index = 0;
            foreach (PackageBase pkg in config.Packages ?? new List<PackageBase>())
            {
                index++;
                SuiteConfigItem item = pkg switch
                {
                    MSIExec msi => DescribeMsi(index, msi, ruleSetNames),
                    MSIRemovalExec msiRemoval => DescribeMsiRemoval(index, msiRemoval, ruleSetNames),
                    MSIxExec msix => DescribeMsix(index, msix, ruleSetNames),
                    MSIxRemovalExec msixRemoval => DescribeMsixRemoval(index, msixRemoval, ruleSetNames),
                    OtherRemovalExec otherRemoval => DescribeOtherRemoval(index, otherRemoval, ruleSetNames),
                    OtherExec other => DescribeOther(index, other, ruleSetNames),
                    _ => new SuiteConfigItem
                    {
                        Title = PackageTitle(index, pkg.Name),
                        SubTitle = pkg.GetType().Name,
                        IconKind = MaterialIconKind.Package,
                        IconColor = EventTabMappings.Packages.IconColor,
                    },
                };
                section.Items.Add(item);
            }
            return section;
        }

        private static SuiteConfigItem DescribeMsi(int index, MSIExec msi, Dictionary<Guid, string> ruleSetNames)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "MSI file", msi.MSIFile);
            Add(details, "Transforms", msi.TransformsFile);
            Add(details, "Patch", msi.PatchFile);
            Add(details, "Product code", msi.ProductCode);
            Add(details, "Upgrade code", msi.UpgradeCode);
            Add(details, "Architecture", msi.Architecture.ToString());
            Add(details, "Context", HumaniseNullable(msi.Context));
            Add(details, "Properties", DescribeMsiProperties(msi.Properties));
            Add(details, "Restart behaviour", Humanise(msi.RestartBehavior));
            if (msi.IsCreateLog) Add(details, "MSI log", string.IsNullOrWhiteSpace(msi.LogPath) ? "Enabled" : msi.LogPath);
            AddFlags(details, "Options",
                ("Secure transforms", msi.SecureTransforms),
                ("Remove product family first", msi.IsRemoveFamily),
                ("Legacy long file path", msi.LegacyLongFilePath),
                ("Remove on suite removal", msi.RemoveOnSuiteRemoval));
            if (msi.Rollback != null) Add(details, "Rollback MSI", FirstNonEmpty(msi.RollbackMSIFile, msi.Rollback.Name, "Configured"));
            Add(details, "Requirement rule", RuleSetName(msi.RequirementRuleSetId, ruleSetNames));
            AddEstimates(details, msi.EstimatedInstallSeconds, msi.EstimatedUninstallSeconds);
            return NewPackageItem(index, msi.Name, "MSI install", MaterialIconKind.PackageVariantClosed, EventTabMappings.Packages.IconColor, details);
        }

        private static SuiteConfigItem DescribeMsiRemoval(int index, MSIRemovalExec msiRemoval, Dictionary<Guid, string> ruleSetNames)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            if (!string.IsNullOrWhiteSpace(msiRemoval.RemovalCode))
                Add(details, "Removes", $"{msiRemoval.RemovalCode} ({(msiRemoval.IsProductRemoval ? "product code" : "upgrade code")})");
            Add(details, "Architecture", msiRemoval.Architecture.ToString());
            Add(details, "Context", HumaniseNullable(msiRemoval.Context));
            Add(details, "Properties", DescribeMsiProperties(msiRemoval.Properties));
            Add(details, "Restart behaviour", Humanise(msiRemoval.RestartBehavior));
            if (msiRemoval.IsCreateLog) Add(details, "MSI log", string.IsNullOrWhiteSpace(msiRemoval.LogPath) ? "Enabled" : msiRemoval.LogPath);
            AddFlags(details, "Options", ("Repair old product on failure", msiRemoval.RepairOldProductOnFailure));
            Add(details, "Requirement rule", RuleSetName(msiRemoval.RequirementRuleSetId, ruleSetNames));
            AddEstimates(details, 0, msiRemoval.EstimatedUninstallSeconds);
            return NewPackageItem(index, msiRemoval.Name, "MSI removal", MaterialIconKind.PackageVariantClosedRemove, RemovalIconColor, details);
        }

        private static SuiteConfigItem DescribeMsix(int index, MSIxExec msix, Dictionary<Guid, string> ruleSetNames)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "MSIX file", msix.MSIxFile);
            Add(details, "Package family name", msix.PackageFamilyName);
            Add(details, "Package full name", msix.PackageFullName);
            AddFlags(details, "Options",
                ("Force close package family", msix.IsForceCloseFamily),
                ("Force this version", msix.IsForceThisVersion),
                ("Defer if in use", msix.IsDeferInUse),
                ("Remove on suite removal", msix.RemoveOnSuiteRemoval));
            if (msix.HasDependents && msix.Dependents != null && msix.Dependents.Count > 0)
                Add(details, "Dependents", string.Join(", ", msix.Dependents));
            if (msix.Rollback != null) Add(details, "Rollback MSIX", FirstNonEmpty(msix.Rollback.MSIxFile, msix.Rollback.Name, "Configured"));
            Add(details, "Requirement rule", RuleSetName(msix.RequirementRuleSetId, ruleSetNames));
            AddEstimates(details, msix.EstimatedInstallSeconds, msix.EstimatedUninstallSeconds);
            return NewPackageItem(index, msix.Name, "MSIX install", MaterialIconKind.MicrosoftWindows, EventTabMappings.Packages.IconColor, details);
        }

        private static SuiteConfigItem DescribeMsixRemoval(int index, MSIxRemovalExec msixRemoval, Dictionary<Guid, string> ruleSetNames)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Package family name", msixRemoval.PFN);
            AddFlags(details, "Options", ("Remove this specific version only", msixRemoval.IsSpecificVersionRemoval));
            Add(details, "Requirement rule", RuleSetName(msixRemoval.RequirementRuleSetId, ruleSetNames));
            AddEstimates(details, 0, msixRemoval.EstimatedUninstallSeconds);
            return NewPackageItem(index, msixRemoval.Name, "MSIX removal", MaterialIconKind.MicrosoftWindows, RemovalIconColor, details);
        }

        private static SuiteConfigItem DescribeOther(int index, OtherExec other, Dictionary<Guid, string> ruleSetNames)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Install", DescribeOtherAction(other.InstallType, other.InstallCommand, other.PowerShellScriptPath, other.PowerShellScriptArgs));
            Add(details, "Rollback", DescribeOtherAction(other.RollbackInstallType, other.RollbackCommand, other.RollbackPowerShellScriptPath, other.RollbackPowerShellScriptArgs));
            AddOtherRemoval(details, other, other.RemovePowerShellScriptPath, other.RemovePowerShellScriptArgs);
            AddOtherCommon(details, other);
            AddFlags(details, "Options",
                ("Secure params", other.SecureParams),
                ("Legacy long file path", other.LegacyLongFilePath),
                ("Remove on suite removal", other.RemoveOnSuiteRemoval));
            Add(details, "Detection rule", RuleSetName(other.DetectionRuleSetId, ruleSetNames));
            Add(details, "Rollback detection rule", RuleSetName(other.RollbackDetectionRuleSetId, ruleSetNames));
            Add(details, "Requirement rule", RuleSetName(other.RequirementRuleSetId, ruleSetNames));
            AddEstimates(details, other.EstimatedInstallSeconds, other.EstimatedUninstallSeconds);
            return NewPackageItem(index, other.Name, "Other installer", MaterialIconKind.Console, EventTabMappings.Packages.IconColor, details);
        }

        private static SuiteConfigItem DescribeOtherRemoval(int index, OtherRemovalExec otherRemoval, Dictionary<Guid, string> ruleSetNames)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            AddOtherRemoval(details, otherRemoval, otherRemoval.PowerShellScriptPath, otherRemoval.PowerShellScriptArgs);
            AddOtherCommon(details, otherRemoval);
            AddFlags(details, "Options",
                ("Secure params", otherRemoval.SecureParams),
                ("Legacy long file path", otherRemoval.LegacyLongFilePath));
            Add(details, "Detection rule", RuleSetName(otherRemoval.DetectionRuleSetId, ruleSetNames));
            Add(details, "Requirement rule", RuleSetName(otherRemoval.RequirementRuleSetId, ruleSetNames));
            AddEstimates(details, 0, otherRemoval.EstimatedUninstallSeconds);
            return NewPackageItem(index, otherRemoval.Name, "Other removal", MaterialIconKind.ConsoleLine, RemovalIconColor, details);
        }

        private static string? DescribeOtherAction(OtherInstallType type, List<VariableText>? command, string? scriptPath, string? scriptArgs)
        {
            if (type == OtherInstallType.PowerShell)
            {
                if (string.IsNullOrWhiteSpace(scriptPath)) return null;
                string script = FileNameOnly(scriptPath);
                return string.IsNullOrWhiteSpace(scriptArgs) ? $"PowerShell: {script}" : $"PowerShell: {script} {scriptArgs}";
            }
            string commandText = DescribeVarText(command);
            return string.IsNullOrWhiteSpace(commandText) ? null : $"Command: {commandText}";
        }

        private static void AddOtherRemoval(List<SuiteConfigDetail> details, OtherCore pkg, string? scriptPath, string? scriptArgs)
        {
            if (pkg.RemovalType == OtherRemovalType.PowerShell)
            {
                string script = string.IsNullOrWhiteSpace(scriptPath) ? "Script not set" : FileNameOnly(scriptPath);
                Add(details, "Removal (PowerShell)", string.IsNullOrWhiteSpace(scriptArgs) ? script : $"{script} {scriptArgs}");
                return;
            }

            switch (pkg.Removal)
            {
                case OtherCMDRemoval cmdRemoval:
                    List<string> commands = (cmdRemoval.RemoveCommands ?? new List<List<VariableText>>())
                        .Select(command => DescribeVarText(command))
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .ToList();
                    if (commands.Count == 0) Add(details, "Removal (command)", "No command set");
                    foreach (string command in commands) Add(details, "Removal (command)", command);
                    break;
                case OtherMSIRemoval msiRemoval:
                    List<MSIInstallRemovalItem> items = msiRemoval.MSIRemovalItems ?? new List<MSIInstallRemovalItem>();
                    if (items.Count == 0) Add(details, "Removal (MSI)", "No MSI codes set");
                    foreach (MSIInstallRemovalItem item in items)
                    {
                        string codeType = item.IsProductRemoval ? "product code" : "upgrade code";
                        string extra = string.IsNullOrWhiteSpace(item.AdditionalParams) ? string.Empty : $" {item.AdditionalParams}";
                        Add(details, "Removal (MSI)", $"{item.Code} ({codeType}){extra}");
                    }
                    break;
                case OtherRegexRemoval regexRemoval:
                    string regexText = $"Manufacturer '{regexRemoval.Manufacturer}', product '{regexRemoval.ProductName}'";
                    if (!string.IsNullOrWhiteSpace(regexRemoval.RemovalParams)) regexText += $", params: {regexRemoval.RemovalParams}";
                    if (regexRemoval.IsCreateLog && !string.IsNullOrWhiteSpace(regexRemoval.LogDirectory)) regexText += $", log: {regexRemoval.LogDirectory}";
                    Add(details, "Removal (RegEx)", regexText);
                    break;
                default:
                    Add(details, "Removal type", Humanise(pkg.RemovalType));
                    break;
            }
        }

        private static void AddOtherCommon(List<SuiteConfigDetail> details, OtherCore pkg)
        {
            Add(details, "Context", Humanise(pkg.Context));
            bool isDelayedRestart = pkg.RestartBehavior == RestartBehavior.BasedOnReturnCodeDelayed || pkg.RestartBehavior == RestartBehavior.AlwaysDelayed;
            Add(details, "Restart behaviour", isDelayedRestart
                ? $"{Humanise(pkg.RestartBehavior)} ({pkg.RestartCountdown}s countdown)"
                : Humanise(pkg.RestartBehavior));
            if (pkg.CustomExitCodes && pkg.ExitCodes != null && pkg.ExitCodes.Any())
                Add(details, "Exit codes", string.Join("; ", pkg.ExitCodes.Select(c => $"{c.Code} = {Humanise(c.Action)}")));
        }

        private static string? DescribeMsiProperties(List<MSIProperty>? properties)
        {
            if (properties == null || properties.Count == 0) return null;
            return string.Join("; ", properties
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => $"{p.Name}={p.Value}"));
        }

        private static SuiteConfigItem NewPackageItem(int index, string? name, string subTitle, MaterialIconKind iconKind, IBrush iconColor, List<SuiteConfigDetail> details)
        {
            return new SuiteConfigItem
            {
                Title = PackageTitle(index, name),
                SubTitle = subTitle,
                IconKind = iconKind,
                IconColor = iconColor,
                Details = details,
            };
        }

        private static string PackageTitle(int index, string? name) => $"{index}. {(string.IsNullOrWhiteSpace(name) ? "Unnamed package" : name)}";

        #endregion

        #region Events

        private static void AddEventSection<TEvent>(
            List<SuiteConfigSection> sections,
            EventTabMappings.Tab tab,
            List<TEvent>? events,
            Func<TEvent, SuiteConfigItem> describe,
            Dictionary<Guid, string> stageNames,
            ref int eventCount) where TEvent : EventCore
        {
            if (events == null || events.Count == 0) return;

            SuiteConfigSection section = NewSection(tab);
            foreach (TEvent evt in events)
            {
                SuiteConfigItem item = describe(evt);
                List<Schedule> schedules = evt.Schedules ?? new List<Schedule>();
                if (schedules.Count == 0)
                {
                    item.Details.Add(Detail("Runs", "No schedule set"));
                }
                foreach (Schedule schedule in schedules)
                {
                    item.Details.Add(Detail("Runs", DescribeSchedule(schedule, stageNames)));
                }
                section.Items.Add(item);
            }
            eventCount += section.Count;
            sections.Add(section);
        }

        private static string DescribeSchedule(Schedule schedule, Dictionary<Guid, string> stageNames)
        {
            string stage = schedule.EventStageId.HasValue && stageNames.TryGetValue(schedule.EventStageId.Value, out string? stageName)
                ? (schedule.EventStageId.Value == Stage.StartStageId || schedule.EventStageId.Value == Stage.EndStageId ? stageName : $"'{stageName}'")
                : "an unknown stage";

            string when = schedule.StageSequence switch
            {
                Sequence.DuringInstallBeforeStage => $"During install, before {stage}",
                Sequence.DuringInstallAfterStage => $"During install, after {stage}",
                Sequence.DuringRemoveBeforeStage => $"During removal, before {stage}",
                Sequence.DuringRemoveAfterStage => $"During removal, after {stage}",
                Sequence.DuringRollbackBeforeStage => $"During rollback, before {stage}",
                Sequence.DuringRollbackAfterStage => $"During rollback, after {stage}",
                Sequence.AlwaysBeforeStage => $"Always, before {stage}",
                Sequence.AlwaysAfterStage => $"Always, after {stage}",
                _ => $"At {stage}",
            };

            if (schedule.Condition != null && !string.IsNullOrWhiteSpace(schedule.Condition.Name))
                when += $" (only if rule '{schedule.Condition.Name}' is met)";
            return when;
        }

        private static SuiteConfigItem DescribeFileEvent(FileExecEvent file)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            string source = file.Action == FileSysIOAction.Deploy
                ? FirstNonEmpty(file.TargetsName, "Bundled in suite")
                : DescribeVarText(file.SourcePath);
            Add(details, file.Action == FileSysIOAction.Deploy ? "Deploys" : "Source", source);
            Add(details, "Destination", DescribeVarText(file.DestinationPath));
            AddFlags(details, "Options",
                ("Replace existing", file.ReplaceExisting),
                ("Override permissions", file.OverridePermissions),
                ("Kept on uninstall", file.IsPermanent));
            if (file.OverridePermissions && file.Permissions != null)
            {
                foreach (Permission permission in file.Permissions)
                {
                    string group = FirstNonEmpty(permission.UserGroup?.FriendlyName, permission.UserGroup?.SID?.Value, "Unknown group");
                    string rights = permission.PermissionRights.Count > 0 ? string.Join(", ", permission.PermissionRights) : "No rights";
                    Add(details, "Permission", $"{group}: {permission.PermissionType?.ToString() ?? "Allow"} {rights}");
                }
            }
            string title = $"{Humanise(file.Action)} {Humanise(file.FileSysIOType).ToLowerInvariant()}: {FirstNonEmpty(file.TargetsName, source)}";
            return NewEventItem(title, EventTabMappings.Files, details);
        }

        private static SuiteConfigItem DescribeRegistryEvent(RegExecEvent reg)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            string title;
            if (reg.Action == RegAction.Import)
            {
                string regFile = reg.RegFilePath == null
                    ? "reg file"
                    : FileNameOnly(reg.RegFilePath.IsAbsoluteUri && reg.RegFilePath.IsFile ? reg.RegFilePath.LocalPath : reg.RegFilePath.OriginalString);
                title = $"Import {regFile}";
            }
            else
            {
                title = $"{HumaniseNullable(reg.Action) ?? "Registry"}: {FirstNonEmpty(reg.KeyPath, "No key set")}";
                Add(details, "Key", reg.KeyPath);
                Add(details, "Value name", reg.PropertyName);
                Add(details, "Value type", HumaniseNullable(reg.PropertyType));
                Add(details, "Data", reg.PropertyValue);
            }
            AddFlags(details, "Options", ("Overwrite existing", reg.Overwrite), ("Kept on uninstall", reg.IsPermanent));
            return NewEventItem(title, EventTabMappings.Registry, details);
        }

        private static SuiteConfigItem DescribeProcClosureEvent(ProcClosureExecEvent proc)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Processes", string.Join(", ", proc.GetNames()));
            string title = $"{HumaniseNullable(proc.Action) ?? "Close"}: {FirstNonEmpty(proc.Name, "No process set")}";
            return NewEventItem(title, EventTabMappings.ProcessClosures, details);
        }

        private static SuiteConfigItem DescribeCertEvent(CertExecEvent cert)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Store", Humanise(cert.Store));
            if (cert.Action == CertAction.Add)
            {
                Add(details, "Certificate", FileNameOnly(cert.FilePath));
                if (!string.IsNullOrWhiteSpace(cert.Password)) Add(details, "Password", "Set (hidden)");
            }
            else
            {
                Add(details, "Thumbprint", cert.Thumbprint);
            }
            AddFlags(details, "Options", ("Kept on uninstall", cert.IsPermanent));
            string subject = cert.Action == CertAction.Add ? FirstNonEmpty(FileNameOnly(cert.FilePath), "certificate") : FirstNonEmpty(cert.Thumbprint, "certificate");
            string title = cert.Action == CertAction.Add
                ? $"Add {subject} to {Humanise(cert.Store)}"
                : $"Remove {subject} from {Humanise(cert.Store)}";
            return NewEventItem(title, EventTabMappings.Certs, details);
        }

        private static SuiteConfigItem DescribeEnvEvent(EnvExecEvent env)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Value", env.Value);
            Add(details, "Behaviour", Humanise(env.Behaviour));
            if (env.Behaviour == Environmentbehaviour.Append || env.Behaviour == Environmentbehaviour.Prepend)
                Add(details, "Separator", $"'{(char)env.Separator}'");
            AddFlags(details, "Options", ("Kept on uninstall", env.IsPermanent));
            string title = $"{Humanise(env.Behaviour)} {FirstNonEmpty(env.Name, "variable")}";
            return NewEventItem(title, EventTabMappings.Environments, details);
        }

        private static SuiteConfigItem DescribeExecutableEvent(ExecutableExecEvent exe)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            string command = DescribeVarText(exe.Command);
            Add(details, "Command", command);
            Add(details, "Working directory", DescribeVarText(exe.WorkingDIR));
            AddFlags(details, "Options",
                ("Continue on error", exe.ContinueOnError),
                ("Continue if not found", exe.ContinueOnNotFound),
                ("Secure params", exe.SecureParams));
            string title = string.IsNullOrWhiteSpace(command) ? "Executable (no command set)" : Truncate(command, 120);
            return NewEventItem(title, EventTabMappings.Executables, details);
        }

        private static SuiteConfigItem DescribeExtensionEvent(BrowserExExecEvent ext)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Extension ID", ext.ExtensionId);
            Add(details, "Source", HumaniseNullable(ext.Source));
            if (ext.Source == BrowserExtensionSource.Local) Add(details, "File", FileNameOnly(ext.ExtPath));
            AddFlags(details, "Options", ("Kept on uninstall", ext.IsPermanent));
            string title = $"{HumaniseNullable(ext.Action) ?? "Install"} {HumaniseNullable(ext.Browser) ?? "browser"} extension {FirstNonEmpty(ext.ExtensionId, FileNameOnly(ext.ExtPath))}".TrimEnd();
            return NewEventItem(title, EventTabMappings.Extensions, details);
        }

        private static SuiteConfigItem DescribeServiceClosureEvent(ServiceClosureExecEvent svc)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Services", string.Join(", ", svc.GetNames()));
            string title = $"{Humanise(svc.Type)}: {FirstNonEmpty(svc.Name, "No service set")}";
            return NewEventItem(title, EventTabMappings.ServiceClosures, details);
        }

        private static SuiteConfigItem DescribeShortcutEvent(ShortcutExecEvent shortcut)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Target", shortcut.Target?.OriginalString);
            Add(details, "Arguments", shortcut.Arguments);
            Add(details, "Type", HumaniseNullable(shortcut.ShortcutType));
            if (shortcut.PlacementList != null && shortcut.PlacementList.Count > 0)
                Add(details, "Placement", string.Join(", ", shortcut.PlacementList.Select(p => Humanise(p))));
            Add(details, "Working directory", shortcut.WorkingDIR?.OriginalString);
            Add(details, "Context", HumaniseNullable(shortcut.Context));
            if (shortcut.IsManualIcon)
                Add(details, "Icon", shortcut.IsPathIcon ? shortcut.IconPath : $"{shortcut.IconPath} (index {shortcut.IconIndex})");
            AddFlags(details, "Options", ("Kept on uninstall", shortcut.IsPermanent));
            string title = $"{HumaniseNullable(shortcut.ShortAction) ?? "Create"} shortcut: {FirstNonEmpty(shortcut.Name, "Unnamed")}";
            return NewEventItem(title, EventTabMappings.Shortcuts, details);
        }

        private static SuiteConfigItem DescribePowerShellEvent(PowerShellExecEvent ps)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Arguments", ps.ScriptArgs);
            Add(details, "Context", HumaniseNullable(ps.Context));
            string? scriptText = ps.ScriptDoc?.Text;
            if (!string.IsNullOrWhiteSpace(scriptText))
            {
                string[] lines = scriptText.Split('\n');
                string? firstLine = lines.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                string preview = string.IsNullOrWhiteSpace(firstLine) ? string.Empty : $", starts with: {Truncate(firstLine, 120)}";
                Add(details, "Script", $"{lines.Length} line(s){preview}");
            }
            string title = FirstNonEmpty(ps.ScriptName, FileNameOnly(ps.ScriptPath), "Unnamed script");
            return NewEventItem(title, EventTabMappings.PSScripts, details);
        }

        private static SuiteConfigItem DescribeDriverEvent(DriverExecEvent driver)
        {
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "INF file", FirstNonEmpty(driver.InfName, FileNameOnly(driver.InfPath)));
            AddFlags(details, "Options", ("Kept on uninstall", driver.IsPermanent));
            string title = $"{Humanise(driver.Action)} driver {FirstNonEmpty(driver.InfName, FileNameOnly(driver.InfPath))}".TrimEnd();
            return NewEventItem(title, EventTabMappings.Drivers, details);
        }

        private static SuiteConfigItem NewEventItem(string title, EventTabMappings.Tab tab, List<SuiteConfigDetail> details)
        {
            return new SuiteConfigItem
            {
                Title = title,
                IconKind = tab.IconKind,
                IconColor = tab.IconColor,
                Details = details,
            };
        }

        #endregion

        #region Rules, Popups and Build

        private static SuiteConfigSection BuildRulesSection(SuiteExecConfig config)
        {
            SuiteConfigSection section = NewSection(EventTabMappings.Rules);
            foreach (RuleSet ruleSet in config.RuleSets ?? new List<RuleSet>())
            {
                List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
                foreach (RuleBase rule in ruleSet.Rules)
                {
                    details.Add(DescribeRule(rule));
                }
                section.Items.Add(new SuiteConfigItem
                {
                    Title = FirstNonEmpty(ruleSet.Name, "Unnamed rule set"),
                    SubTitle = $"{ruleSet.Rules.Count} rule(s)",
                    IconKind = EventTabMappings.Rules.IconKind,
                    IconColor = EventTabMappings.Rules.IconColor,
                    Details = details,
                });
            }
            return section;
        }

        private static SuiteConfigDetail DescribeRule(RuleBase rule)
        {
            if (rule is RuleOperator op)
            {
                string opText = op.Type switch
                {
                    RuleType.OR => "OR",
                    RuleType.GroupOpen => "( group start",
                    RuleType.GroupClose => ") group end",
                    _ => op.Type.ToString(),
                };
                return Detail("Operator", opText);
            }

            if (rule is Rule r)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(Humanise(r.DetectionType));
                string? basePath = r.BasePath != null && r.BasePath.Count > 0 ? DescribeVarText(r.BasePath) : r.Base;
                if (!string.IsNullOrWhiteSpace(basePath)) sb.Append(": ").Append(basePath);
                if (!string.IsNullOrWhiteSpace(r.ComparatorProperty)) sb.Append(" [").Append(r.ComparatorProperty).Append(']');
                if (r.Comparator.HasValue) sb.Append(' ').Append(Humanise(r.Comparator.Value).ToLowerInvariant());
                if (!string.IsNullOrWhiteSpace(r.ComparatorValue)) sb.Append(" '").Append(r.ComparatorValue).Append('\'');
                if (r.Type == RuleType.PowerShell && !string.IsNullOrWhiteSpace(r.ScriptName)) sb.Append(" (script: ").Append(r.ScriptName).Append(')');
                if (r.Architecture.HasValue) sb.Append(", ").Append(r.Architecture.Value);
                if (r.Context.HasValue) sb.Append(", ").Append(Humanise(r.Context.Value)).Append(" context");
                return Detail(Humanise(r.Type), Truncate(sb.ToString()));
            }

            return Detail("Rule", rule.Type.ToString());
        }

        private static SuiteConfigSection? BuildPopupSection(Popup? popup)
        {
            if (popup == null || (!popup.ShowProgress && !popup.ShowPopupWarning)) return null;

            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            AddFlags(details, "Enabled",
                ("Progress popup", popup.ShowProgress),
                ("Warning popup", popup.ShowPopupWarning),
                ("Linked to process closures", popup.LinkToProcClosures),
                ("Pause during meetings", popup.PauseDuringMeeting),
                ("Lockdown", popup.LockdownEnabled));
            if (popup.ShowPopupWarning)
            {
                if (popup.Timer > 0) Add(details, "Timer", $"{popup.Timer}s, then {HumaniseNullable(popup.TimerExpireAction) ?? "continue"}");
                if (popup.DelayDays.HasValue) Add(details, "Deferral", $"Up to {popup.DelayDays.Value} day(s)");
                Add(details, "If logged off", HumaniseNullable(popup.LoggedOffAction));
                Add(details, "If locked", HumaniseNullable(popup.LockedAction));
                Add(details, "During enrollment (ESP)", HumaniseNullable(popup.ESPAction));
                if (popup.HasInstallTxt) Add(details, "Install message", RichTextMarkup.ToPlainText(popup.InstallTxt));
                if (popup.HasUninstallTxt) Add(details, "Uninstall message", RichTextMarkup.ToPlainText(popup.UninstallTxt));
                if (popup.HasPSCondition) Add(details, "PowerShell condition", "Set");
            }
            if (popup.LockdownEnabled)
                Add(details, "Lockdown", $"Up to {popup.LockdownMaxMinutes} minute(s): {popup.LockdownMessage}");

            SuiteConfigSection section = NewSection(EventTabMappings.Popup);
            section.Items.Add(new SuiteConfigItem
            {
                Title = "Popup settings",
                IconKind = EventTabMappings.Popup.IconKind,
                IconColor = EventTabMappings.Popup.IconColor,
                Details = details,
            });
            return section;
        }

        private static SuiteConfigSection BuildBuildSection(Build? buildSettings)
        {
            Build build = buildSettings ?? new Build();
            List<SuiteConfigDetail> details = new List<SuiteConfigDetail>();
            Add(details, "Manufacturer", build.Manufacturer);
            Add(details, "Product", build.Name);
            string? version = build.SuiteVersion?.ToString();
            if (!string.IsNullOrWhiteSpace(version) && build.Revision > 0) version += $" (revision {build.Revision})";
            Add(details, "Version", version);
            if (build.UpgradeCode != Guid.Empty) Add(details, "Upgrade code", build.UpgradeCode.ToString());
            Add(details, "Log directory", build.LogDir);
            AddFlags(details, "Options",
                ("Fail-safe recovery task", build.FailSafe),
                ("Refresh environment", build.RefreshEnv),
                ("Keep cache", build.KeepCache),
                ("Retry on failure", build.FailureRetry),
                ("Restart on failure", build.RestartOnFailure),
                ("Reverse order on uninstall", build.ReverseUninstall),
                ("Create uninstall media", build.CreateUninstallMedia));
            Add(details, "Detection rule", build.Detection);

            SuiteConfigSection section = NewSection(EventTabMappings.Build);
            section.Items.Add(new SuiteConfigItem
            {
                Title = "Build settings",
                IconKind = EventTabMappings.Build.IconKind,
                IconColor = EventTabMappings.Build.IconColor,
                Details = details,
            });
            return section;
        }

        private static string? DescribeBuildSummary(Build build)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(build.Manufacturer)) parts.Add(build.Manufacturer.Trim());
            if (!string.IsNullOrWhiteSpace(build.Name)) parts.Add(build.Name.Trim());
            if (build.SuiteVersion != null) parts.Add(build.SuiteVersion.ToString());
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }

        #endregion

        #region Helpers

        private static SuiteConfigSection NewSection(EventTabMappings.Tab tab)
        {
            return new SuiteConfigSection
            {
                Header = tab.Header,
                IconKind = tab.IconKind,
                IconColor = tab.IconColor,
            };
        }

        private static SuiteConfigDetail Detail(string label, string value) => new SuiteConfigDetail { Label = label, Value = value };

        private static void Add(List<SuiteConfigDetail> details, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            details.Add(Detail(label, Truncate(value.Trim())));
        }

        private static void AddFlags(List<SuiteConfigDetail> details, string label, params (string Name, bool IsOn)[] flags)
        {
            string enabled = string.Join(", ", flags.Where(f => f.IsOn).Select(f => f.Name));
            details.Add(Detail(label, enabled.Length > 0 ? enabled : "None"));
        }

        private static void AddEstimates(List<SuiteConfigDetail> details, int installSeconds, int uninstallSeconds)
        {
            List<string> parts = new List<string>();
            if (installSeconds > 0) parts.Add($"install ~{FormatSeconds(installSeconds)}");
            if (uninstallSeconds > 0) parts.Add($"uninstall ~{FormatSeconds(uninstallSeconds)}");
            if (parts.Count > 0) details.Add(Detail("Estimated time", string.Join(", ", parts)));
        }

        private static string FormatSeconds(int seconds)
        {
            if (seconds < 60) return $"{seconds}s";
            return seconds % 60 == 0 ? $"{seconds / 60}m" : $"{seconds / 60}m {seconds % 60}s";
        }

        /// <summary>Renders a variable-text command/path as one readable string, e.g. "%ProgramFiles% \App\ &lt;setup.exe&gt; /S".</summary>
        private static string DescribeVarText(IEnumerable<VariableText>? parts)
        {
            if (parts == null) return string.Empty;
            StringBuilder sb = new StringBuilder();
            foreach (VariableText part in parts)
            {
                switch (part)
                {
                    case LiteralText literal:
                        sb.Append(literal.Value);
                        break;
                    case RelativeFileVar relative:
                        // A file bundled inside the suite alongside the package/event, referenced by name.
                        sb.Append('<').Append(relative.RelativePath).Append('>');
                        break;
                    case SpecialDIR special:
                        sb.Append('%').Append(special.Value).Append('%');
                        break;
                    case FileVar fileVar:
                        sb.Append(fileVar.Node?.FullPath);
                        break;
                    default:
                        try { sb.Append(part.GetValue()); } catch { sb.Append(part.GetType().Name); }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>"StopAndBlock" -> "Stop And Block", keeping known product names intact.</summary>
        private static string Humanise<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            string spaced = PascalCaseBoundary.Replace(value.ToString(), " ");
            return spaced.Replace("Power Shell", "PowerShell").Replace("Reg Ex", "RegEx");
        }

        private static string? HumaniseNullable<TEnum>(TEnum? value) where TEnum : struct, Enum
        {
            return value.HasValue ? Humanise(value.Value) : null;
        }

        private static string FileNameOnly(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string trimmed = path.Trim().TrimEnd('\\', '/');
            int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return cut >= 0 ? trimmed.Substring(cut + 1) : trimmed;
        }

        private static string FirstNonEmpty(params string?[] candidates)
        {
            foreach (string? candidate in candidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate)) return candidate;
            }
            return string.Empty;
        }

        private static string Truncate(string value, int max = 400)
        {
            return value.Length <= max ? value : value.Substring(0, max) + "...";
        }

        #endregion
    }
}
