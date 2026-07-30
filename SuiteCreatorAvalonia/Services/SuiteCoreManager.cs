using AvaloniaEdit.Document;
using Newtonsoft.Json;
using SuiteCreatorAvalonia.Converters;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;
using FileIO = SuiteCreatorAvalonia.Models.Events.FileSysIO;
using PowerShell = SuiteCreatorAvalonia.Models.Events.PowerShell;
using RuleSet = SuiteCreatorAvalonia.Models.Rules.RuleSet;

namespace SuiteCreatorAvalonia.Services
{
    public class SuiteCoreManager
    {
        internal static readonly string AppLocalAppDataPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "SuiteCreator");
        internal static readonly string AutoSaveFolder = Path.Combine(AppLocalAppDataPath, "AutoSaves");
        private readonly string _autoSaveFilePath;
        private static readonly SemaphoreSlim _suiteFileSemaphore = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _autoSaveSemaphore = new SemaphoreSlim(1, 1);
        private Timer? _autoSaveDebounceTimer;
        private bool _isLoading = false;
        public event EventHandler? ProjectCleared;
        public event EventHandler? ProjectLoaded;
        public event EventHandler? ProjectSaved;
        public event EventHandler<string>? LoadFailed;
        public event EventHandler? ProjectNameChanged;
        public event EventHandler<object?>? ProjectChanged;
        public event EventHandler? ProjectMatchesSaved;
        public event EventHandler? PackageNameChanged;
        public event EventHandler<bool>? HasUnsavedChangesChanged;
        private string? _suiteSaveFilePath;
        private string? _lastSavedSnapshot;
        private bool _hasUnsavedChanges = false;
        private SuiteProjectConfig _suiteConfig;

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                if (_hasUnsavedChanges != value)
                {
                    _hasUnsavedChanges = value;
                    HasUnsavedChangesChanged?.Invoke(this, value);
                }
            }
        }

        public SuiteCoreManager()
        {
            _autoSaveFilePath = Path.Combine(AutoSaveFolder, $"AutoSave_{Guid.NewGuid():N}.sctp");
            _suiteConfig = new SuiteProjectConfig();
            SetDefaultConfig(deleteAutoSave: false);
            _suiteConfig.ProjectNameChanged += (s, e) => ProjectNameChanged?.Invoke(this, EventArgs.Empty);
            ProjectChanged += OnProjectChangedAutoSave;
        }

        private void OnProjectChangedAutoSave(object? sender, object? e)
        {
            if (_isLoading) return;
            HasUnsavedChanges = true;
            _autoSaveDebounceTimer?.Dispose();
            _autoSaveDebounceTimer = new Timer(_ => _ = TriggerAutoSaveAsync(), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        }

        private async Task TriggerAutoSaveAsync()
        {
            if (_isLoading) return;
            if (!HasUnsavedChanges) return;
            await _autoSaveSemaphore.WaitAsync();
            try
            {
                Directory.CreateDirectory(AutoSaveFolder);
                string json = JsonConvert.SerializeObject(_suiteConfig, typeof(SuiteProjectConfig), Formatting.None, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    Converters =
                    {
                        new ColorToJson(),
                        new BitmapToJson(),
                        new TextDocumentToJson(),
                        new SecurityIdentifierToJson(),
                    },
                    MaxDepth = null,
                });
                await File.WriteAllTextAsync(_autoSaveFilePath, json);
            }
            catch (Exception ex)
            {
                AppLog.Error("AutoSave failed", ex, "SuiteCore");
            }
            finally
            {
                _autoSaveSemaphore.Release();
            }
        }

        internal bool AutoSaveExists() => File.Exists(_autoSaveFilePath);

        internal static IReadOnlyList<string> GetAllAutoSavePaths()
        {
            if (!Directory.Exists(AutoSaveFolder)) return [];
            return Directory.GetFiles(AutoSaveFolder, "AutoSave_*.sctp")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();
        }

        internal static string PeekProjectName(string autoSavePath)
        {
            try
            {
                string json = File.ReadAllText(autoSavePath);
                // Fast regex-free extraction: look for "ProjectName":"..."
                int idx = json.IndexOf("\"ProjectName\"", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    int colon = json.IndexOf(':', idx);
                    if (colon >= 0)
                    {
                        int quote1 = json.IndexOf('"', colon + 1);
                        if (quote1 >= 0)
                        {
                            int quote2 = json.IndexOf('"', quote1 + 1);
                            if (quote2 > quote1)
                                return json.Substring(quote1 + 1, quote2 - quote1 - 1);
                        }
                    }
                }
            }
            catch { }
            return "Untitled Suite";
        }

        internal void DeleteAutoSave()
        {
            try
            {
                if (File.Exists(_autoSaveFilePath))
                    File.Delete(_autoSaveFilePath);
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Failed to delete autosave: {ex.Message}", "SuiteCore");
            }
        }

        internal static void DeleteAutoSaveAt(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Failed to delete autosave at: {path}, error: {ex.Message}", "SuiteCore");
            }
        }

        internal async Task LoadAutoSaveAsync(string autoSavePath)
        {
            await LoadAsync(autoSavePath);
            _suiteSaveFilePath = null;
            _lastSavedSnapshot = null;
            HasUnsavedChanges = true;
        }

        #region Stages
        internal ObservableCollection<Stage>? GetStages()
        {
            if (_suiteConfig.Stages == null)
            {
                return null;
            }
            else
            {
                ObservableCollection<Stage> stages = new(_suiteConfig.Stages.Select(s => s.Clone()));
                foreach (PackageBase pkg in _suiteConfig.Packages)
                {
                    stages.Add(
                        new PackageStageRef
                        {
                            Id = pkg.Id,
                            Name = pkg.Name,
                        }
                    );
                }
                return stages;
            }
        }

        internal void SetDefaultStages()
        {
            _suiteConfig.Stages = new ObservableCollection<Stage>{
                new DefaultStage { Id = Stage.StartStageId, Name = "Start", Description = "The beginning of the Suite" },
                new DefaultStage { Id = Stage.EndStageId, Name = "End", Description = "After all Packages and Uninstalls have completed" }
            };
        }
        internal void PopulateStagesOnEvents()
        {
            List<Stage>? stages = GetStages()?.ToList();
            if (stages == null) return;
            var eventCoreType = typeof(EventCore);
            var suiteConfigType = _suiteConfig.GetType();
            foreach (var prop in suiteConfigType.GetProperties())
            {
                if (!prop.CanRead) continue;
                var propType = prop.PropertyType;
                if (propType.IsGenericType &&
                    propType.GetGenericTypeDefinition() == typeof(ObservableCollection<>) &&
                    eventCoreType.IsAssignableFrom(propType.GetGenericArguments()[0]))
                {
                    var collection = prop.GetValue(_suiteConfig) as System.Collections.IEnumerable;
                    if (collection == null) continue;
                    foreach (var evt in collection)
                    {
                        var schedulesProp = eventCoreType.GetProperty("Schedules");
                        if (schedulesProp == null) continue;
                        var schedules = schedulesProp.GetValue(evt) as IEnumerable<Schedule>;
                        if (schedules == null) continue;
                        foreach (var sch in schedules)
                        {
                            if (sch.EventStage == null)
                            {
                                var match = stages.FirstOrDefault(s => s.Id == sch.EventStageId);
                                if (match != null)
                                    sch.EventStage = match;
                                else
                                {
                                    sch.EventStage = stages.First(s => !string.IsNullOrWhiteSpace(s.Name) && s.Name.Equals("End"));
                                    sch.EventStageId = sch.EventStage.Id;
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region BuildSettings
        internal Build GetBuildSettings()
        {
            return _suiteConfig.BuildSettings.Clone();
        }

        internal void UpdateBuildSettings(Build build)
        {
            if (build == null) return;
            string before = JsonConvert.SerializeObject(_suiteConfig.BuildSettings);
            _suiteConfig.BuildSettings.UpdateFrom(build);
            if (!string.Equals(before, JsonConvert.SerializeObject(_suiteConfig.BuildSettings), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, EventArgs.Empty);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region PopupSettings
        internal Popup GetPopupSettings()
        {
            return _suiteConfig.PopupSettings.Clone();
        }
        internal void UpdatePopupSettings(Popup popups)
        {
            if (popups == null) return;
            string before = JsonConvert.SerializeObject(_suiteConfig.PopupSettings);
            _suiteConfig.PopupSettings.UpdateFrom(popups);
            if (!string.Equals(before, JsonConvert.SerializeObject(_suiteConfig.PopupSettings), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, EventArgs.Empty);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region Packages

        internal ObservableCollection<PackageBase> GetPackages()
        {
            return new ObservableCollection<PackageBase>(_suiteConfig.Packages.Select(pkg => pkg.Clone()));
        }

        internal PackageBase GetPackage(Guid packageId)
        {
            PackageBase? matchingPkg = _suiteConfig.Packages.FirstOrDefault(p => p.Id.Equals(packageId));
            if (matchingPkg == null) throw new ArgumentException($"No package found with ID: {packageId}", nameof(packageId));
            return matchingPkg.Clone();
        }

        internal PackageBase NewPackage(Type packageType, string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentNullException(nameof(packageName), "Package name cannot be null or empty");
            if (!typeof(PackageBase).IsAssignableFrom(packageType))
            {
                throw new ArgumentException($"The provided type must derive from {nameof(PackageBase)}", nameof(packageType));
            }
            PackageBase pkg = (PackageBase)Activator.CreateInstance(packageType)!;

            switch (pkg)
            {
                case MSIxPkg msixPkg:
                    msixPkg.HasDependents = false;
                    msixPkg.Dependents = new List<string>();
                    msixPkg.IsForceCloseFamily = true;
                    msixPkg.IsForceThisVersion = false;
                    msixPkg.MSIxPath = string.Empty;
                    break;
                case MSIxRemoval msixRemPkg:
                    msixRemPkg.IsSpecificVersionRemoval = false;
                    break;
                case MSIPkg:
                    MSIPkg msiPkg = (MSIPkg)pkg;
                    msiPkg.Context = Enums.Contexts.System;
                    msiPkg.IsCreateLog = false;
                    msiPkg.LegacyLongFilePath = false;
                    msiPkg.LogPath = string.Empty;
                    msiPkg.MSIPath = string.Empty;
                    msiPkg.PatchPath = string.Empty;
                    msiPkg.Properties = new List<MSIProperty>();
                    msiPkg.TransformsPath = string.Empty;
                    break;
                case MSIRemoval:
                    MSIRemoval msiRemPkg = (MSIRemoval)pkg;
                    msiRemPkg.IsProductRemoval = false;
                    msiRemPkg.RemovalCode = string.Empty;
                    msiRemPkg.IsCreateLog = false;
                    msiRemPkg.LogPath = string.Empty;
                    break;
                case OtherPkg:
                    OtherPkg otherPkg = (OtherPkg)pkg;
                    otherPkg.Context = Enums.Contexts.System;
                    otherPkg.RemovalType = OtherRemovalType.MSI;
                    otherPkg.CustomExitCodes = false;
                    otherPkg.LegacyLongFilePath = false;
                    otherPkg.SecureParams = false;
                    otherPkg.InstallCommand = new List<VariableText> { new LiteralText(string.Empty) };
                    otherPkg.ExitCodes = new List<ReturnCode>();
                    otherPkg.Files = new ObservableCollection<FileSystemNode> { new FileSystemNode("PackageRoot", new ObservableCollection<FileSystemNode>()) };
                    otherPkg.RestartBehavior = Enums.RestartBehavior.Ignore;
                    otherPkg.RemovalType = OtherRemovalType.MSI;
                    otherPkg.Removal = new OtherMSIRemoval
                    {
                        MSIRemovalItems = new List<MSIInstallRemovalItem>
                    {
                        new MSIInstallRemovalItem
                        {
                            IsProductRemoval = false,
                        }
                    },
                    };
                    break;
                case OtherRemovalPkg:
                    OtherRemovalPkg otherRemPkg = (OtherRemovalPkg)pkg;
                    otherRemPkg.RemovalType = OtherRemovalType.MSI;
                    otherRemPkg.Removal = new OtherMSIRemoval
                    {
                        MSIRemovalItems = new List<MSIInstallRemovalItem> { new MSIInstallRemovalItem() },
                    };
                    otherRemPkg.Context = Enums.Contexts.System;
                    otherRemPkg.LegacyLongFilePath = false;
                    otherRemPkg.CustomExitCodes = false;
                    otherRemPkg.SecureParams = false;
                    otherRemPkg.ExitCodes = new List<ReturnCode>();
                    otherRemPkg.RestartBehavior = Enums.RestartBehavior.Ignore;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pkg), pkg.GetType().Name, $"Unrecognised package type: {pkg.GetType().Name}");
            }
            pkg.Name = packageName;
            _suiteConfig.Packages.Add(pkg);
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return pkg.Clone();
        }

        internal void RemovePackage(PackageBase package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package), "Package cannot be null");
            var matching = _suiteConfig.Packages.Where(p => p.Id.Equals(package.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.RuleSets.Where(r => r.Id == matching.RequirementRuleSetId)
                    .ToList()
                    .ForEach(r => _suiteConfig.RuleSets.Remove(r));
                _suiteConfig.Packages.Remove(matching);
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal void ReorderPackages(IEnumerable<Guid> orderedIds)
        {
            List<Guid> order = orderedIds.ToList();
            _suiteConfig.Packages = new ObservableCollection<PackageBase>(
                order.Select(id => _suiteConfig.Packages.First(p => p.Id == id)));
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            CheckIfMatchesSaved();
        }

        internal void UpdatePackage(PackageBase package)
        {
            if (package == null) return;
            PackageBase? match = _suiteConfig.Packages.Where(p => p.Id == package.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided package does not exist the Core Service");

            switch (package?.GetType()) // Remove rollback if its not got a file or command.
            { 
                case Type t when t == typeof(MSIxPkg): 
                    MSIxPkg msix = (MSIxPkg)package;
                    if (string.IsNullOrWhiteSpace(msix.Rollback?.MSIxFile))
                        msix.Rollback = null;
                    break;
                case Type t when t == typeof(MSIPkg):
                    MSIPkg msi = (MSIPkg)package;
                    MSIPkg? rollbackMSI = (MSIPkg?)msi.Rollback;
                    if (string.IsNullOrWhiteSpace(rollbackMSI?.MSIPath))
                        msi.Rollback = null;
                    break;
            }

            bool triggerNameUpdated = false;
            if (
                match.Name != null &&
                package.Name != null &&
                !match.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase)
            )
            {
                triggerNameUpdated = true;
            }

            string before = JsonConvert.SerializeObject(match, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto });
            match.UpdateFrom(package);
            bool hasChanges = !string.Equals(before, JsonConvert.SerializeObject(match, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto }), StringComparison.Ordinal);

            if (triggerNameUpdated)
            {
                PackageNameChanged?.Invoke(this, EventArgs.Empty);
            }
            if (hasChanges)
            {
                ProjectChanged?.Invoke(this, EventArgs.Empty);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region RuleSets
        internal ObservableCollection<RuleSet> GetRuleSets()
        {
            UpdatePackageBasedRuleSetNames();
            return new ObservableCollection<RuleSet>(_suiteConfig.RuleSets.Select(r => r.Clone()));
        }

        internal RuleSet GetRuleSet(Guid ruleSetId)
        {
            UpdatePackageBasedRuleSetNames();
            var matchingRule = _suiteConfig.RuleSets.Where(r => r.Id == ruleSetId).FirstOrDefault();
            if (matchingRule == null) throw new ArgumentException($"No RuleSet found with ID: {ruleSetId}", nameof(ruleSetId));
            return matchingRule.Clone();
        }

        internal RuleSet NewRuleSet(string ruleSetName)
        {
            if (string.IsNullOrEmpty(ruleSetName)) throw new ArgumentNullException(nameof(ruleSetName), "Rule set name cannot be null or empty");
            RuleSet? existingRuleSetWithName = _suiteConfig.RuleSets
                .Where(r => r.Name != null && r.Name.Equals(ruleSetName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (existingRuleSetWithName != null)
            {
                if (existingRuleSetWithName.Rules.Count > 0)
                    existingRuleSetWithName.Rules.Clear();
                return existingRuleSetWithName.Clone();
            }
            RuleSet ruleSet = new RuleSet(ruleSetName);
            _suiteConfig.RuleSets.Add(ruleSet);
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            return ruleSet.Clone();
        }

        internal void RemoveRuleSet(RuleSet ruleSet)
        {
            if (ruleSet == null) throw new ArgumentNullException(nameof(ruleSet), "Rule set cannot be null");
            var matching = _suiteConfig.RuleSets.Where(r => r.Id.Equals(ruleSet.Id)).FirstOrDefault();
            if (matching != null)
            {
                ValidateRuleRemoval(matching);
                _suiteConfig.RuleSets.Remove(matching);
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal void RemoveRuleSet(Guid ruleSetId)
        {
            var matching = _suiteConfig.RuleSets.Where(r => r.Id == ruleSetId).FirstOrDefault();
            if (matching != null)
            {
                ValidateRuleRemoval(matching);
                _suiteConfig.RuleSets.Remove(matching);
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ValidateRuleRemoval(RuleSet ruleSet)
        {
            // Check if this RuleSet is used by any package
            var usedInPackages = _suiteConfig.Packages.Where(p => p.RequirementRuleSetId == ruleSet.Id || (p is OtherBase other && other.DetectionRuleSetId == ruleSet.Id));
            if (usedInPackages != null && usedInPackages.Count() > 0)
            {
                throw new InvalidOperationException($"Cannot remove RuleSet as it is being used by the following packages: {string.Join(',', usedInPackages.Select(p => p.Name))}");
            }
            // Check if any events are using this RuleSet
            foreach (SuiteProperty prop in Enum.GetValues<SuiteProperty>().Where(e => e.ToString().EndsWith("Events")))
            {
                PropertyInfo? propertyInfo = _suiteConfig.GetType().GetProperty(prop.ToString());
                if (propertyInfo == null) continue;
                if (propertyInfo.GetValue(_suiteConfig) is System.Collections.IEnumerable eCoreCollection &&
                    propertyInfo.PropertyType.IsGenericType &&
                    propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(ObservableCollection<>) &&
                    typeof(EventCore).IsAssignableFrom(propertyInfo.PropertyType.GetGenericArguments()[0]))
                {
                    var usedInEvents = eCoreCollection.Cast<EventCore>().Where(c => c.Schedules.Any(s => s.Condition != null && s.Condition?.Id == ruleSet.Id));
                    if (usedInEvents != null && usedInEvents.Count() > 0)
                    {
                        throw new InvalidOperationException($"Cannot remove RuleSet as it is being used by one or more {prop.ToString()}");
                    }
                }
            }
        }

        internal void UpdateRuleSet(RuleSet ruleSet)
        {
            if (ruleSet == null) return;
            RuleSet match = _suiteConfig.RuleSets.Where(r => r.Id == ruleSet.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided RuleSet does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(ruleSet);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, ruleSet);
                CheckIfMatchesSaved();
            }
        }

        internal (List<PackageBase> Packages, List<EventCore> Events) GetRuleSetLinks(Guid ruleSetId)
        {
            List<PackageBase> linkedPackages = _suiteConfig.Packages
                .Where(p => p.RequirementRuleSetId == ruleSetId || (p is OtherBase other && other.DetectionRuleSetId == ruleSetId))
                .ToList();

            List<EventCore> linkedEvents = new List<EventCore>();
            foreach (SuiteProperty prop in Enum.GetValues<SuiteProperty>().Where(e => e.ToString().EndsWith("Events")))
            {
                PropertyInfo? propertyInfo = _suiteConfig.GetType().GetProperty(prop.ToString());
                if (propertyInfo == null) continue;
                if (propertyInfo.GetValue(_suiteConfig) is System.Collections.IEnumerable eCoreCollection &&
                    propertyInfo.PropertyType.IsGenericType &&
                    propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(ObservableCollection<>) &&
                    typeof(EventCore).IsAssignableFrom(propertyInfo.PropertyType.GetGenericArguments()[0]))
                {
                    linkedEvents.AddRange(eCoreCollection.Cast<EventCore>()
                        .Where(c => c.Schedules.Any(s => s.Condition != null && s.Condition.Id == ruleSetId)));
                }
            }

            return (linkedPackages, linkedEvents);
        }

        private void UpdatePackageBasedRuleSetNames()
        {
            foreach (PackageBase pkg in _suiteConfig.Packages)
            {
                Guid? requirementRuleSetId = pkg.RequirementRuleSetId;
                Guid? detectionRuleSetId = (pkg is OtherBase otherPkg) ? otherPkg.DetectionRuleSetId : null;
                if (requirementRuleSetId == null && detectionRuleSetId == null) continue;

                if (requirementRuleSetId is Guid reqGuid)
                {
                    var matchingRuleSet = _suiteConfig.RuleSets.Where(r => r.Id == reqGuid).First();
                    if (matchingRuleSet.Name == null || !matchingRuleSet.Name.Equals($"{pkg.Name} (Pkg Req)"))
                    {
                        matchingRuleSet.Name = $"{pkg.Name} (Pkg Req)";
                    }
                }
                else if (detectionRuleSetId is Guid dectGuid)
                {
                    var matchingRuleSet = _suiteConfig.RuleSets.Where(r => r.Id == dectGuid).First();
                    if (matchingRuleSet.Name == null || !matchingRuleSet.Name.Equals($"{pkg.Name} (Pkg Dect)"))
                    {
                        matchingRuleSet.Name = $"{pkg.Name} (Pkg Req)";
                    }
                }
            }
        }

        #endregion

        #region CertEvents
        internal ObservableCollection<Cert> GetCertEvents()
        {
            return new ObservableCollection<Cert>(_suiteConfig.CertEvents.Select(c => c.Clone()));
        }

        internal Cert NewCertEvent()
        {
            Cert cert = new Cert
            {
                Action = CertAction.Add,
                Store = CertStore.TrustedRootCA,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                }
            };
            _suiteConfig.CertEvents.Add(cert);
            ProjectChanged?.Invoke(this, cert);
            return cert.Clone();
        }

        internal void RemoveCertEvent(Cert cert)
        {
            if (cert == null) throw new ArgumentNullException(nameof(cert), "Cert cannot be null");
            var matching = _suiteConfig.CertEvents.Where(p => p.Id.Equals(cert.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.CertEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateCertEvent(Cert cert)
        {
            if (cert == null) return;
            Cert match = _suiteConfig.CertEvents.Where(c => c.Id == cert.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided Cert does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(cert);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, cert);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region DriverEvents
        internal ObservableCollection<Driver> GetDriverEvents()
        {
            return new ObservableCollection<Driver>(_suiteConfig.DriverEvents.Select(d => d.Clone()));
        }

        internal Driver NewDriverEvent()
        {
            Driver driver = new Driver
            {
                Action = DriverAction.Install,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                }
            };
            _suiteConfig.DriverEvents.Add(driver);
            ProjectChanged?.Invoke(this, driver);
            return driver.Clone();
        }

        internal void RemoveDriverEvent(Driver driver)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver), "Driver cannot be null");
            var matching = _suiteConfig.DriverEvents.Where(p => p.Id.Equals(driver.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.DriverEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateDriverEvent(Driver driver)
        {
            if (driver == null) return;
            Driver match = _suiteConfig.DriverEvents.Where(d => d.Id == driver.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided Driver does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(driver);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, driver);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region EnvironmentEvents
        internal ObservableCollection<Environment> GetEnvironmentEvents()
        {
            return new ObservableCollection<Environment>(_suiteConfig.EnvironmentEvents.Select(e => e.Clone()));
        }

        internal Environment NewEnvironmentEvent()
        {
            Environment env = new Environment
            {
                Behaviour = Environmentbehaviour.Append,
                Separator = Enums.Separator.Semicolon,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                }
            };
            _suiteConfig.EnvironmentEvents.Add(env);
            ProjectChanged?.Invoke(this, env);
            return env.Clone();
        }

        internal void RemoveEnvironmentEvent(Environment env)
        {
            if (env == null) throw new ArgumentNullException(nameof(env), "Environment event cannot be null");
            var matching = _suiteConfig.EnvironmentEvents.Where(p => p.Id.Equals(env.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.EnvironmentEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateEnvironmentEvent(Environment env)
        {
            if (env == null) return;
            Environment match = _suiteConfig.EnvironmentEvents.Where(e => e.Id == env.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided Env does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(env);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, env);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region ExecutableEvents
        internal ObservableCollection<Executable> GetExecutableEvents()
        {
            return new ObservableCollection<Executable>(_suiteConfig.ExecutableEvents.Select(e => e.Clone()));
        }

        internal Executable NewExecutableEvent()
        {
            Executable exe = new Executable
            {
                SecureParams = false,
                WorkingDIR = null,
                ContinueOnError = false,
                ContinueOnNotFound = false,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                },
                TreeNodes = new List<FileSystemNode> { new FileSystemNode("ExecRoot", new ObservableCollection<FileSystemNode>()) },
            };
            _suiteConfig.ExecutableEvents.Add(exe);
            ProjectChanged?.Invoke(this, exe);
            return exe.Clone();
        }

        internal void RemoveExecutableEvent(Executable exe)
        {
            if (exe == null) throw new ArgumentNullException(nameof(exe), "Executable event cannot be null");
            var matching = _suiteConfig.ExecutableEvents.Where(p => p.Id.Equals(exe.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.ExecutableEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateExecutableEvent(Executable exe)
        {
            if (exe == null) return;
            Executable? match = _suiteConfig.ExecutableEvents.Where(e => e.Id == exe.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided Executable does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(exe);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, exe);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region ExtensionEvents
        internal ObservableCollection<BrowserExt> GetExtensionEvents()
        {
            return new ObservableCollection<BrowserExt>(_suiteConfig.ExtensionEvents.Select(e => e.Clone()));
        }

        internal BrowserExt NewExtensionEvent()
        {
            BrowserExt ext = new()
            {
                Action = ExtAction.Install,
                Browser = BrowserType.MSEdge,
                Source = BrowserExtensionSource.MSEdgeWebStore,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                }
            };
            _suiteConfig.ExtensionEvents.Add(ext);
            ProjectChanged?.Invoke(this, ext);
            return ext.Clone();
        }

        internal void RemoveExtensionEvent(BrowserExt ext)
        {
            if (ext == null) throw new ArgumentNullException(nameof(ext), "Extension event cannot be null");
            var matching = _suiteConfig.ExtensionEvents.Where(p => p.Id.Equals(ext.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.ExtensionEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateExtensionEvent(BrowserExt ext)
        {
            if (ext == null) return;
            BrowserExt match = _suiteConfig.ExtensionEvents.Where(e => e.Id == ext.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided ext does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(ext);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, ext);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region FileEvents
        internal ObservableCollection<FileIO> GetFileEvents()
        {
            return new ObservableCollection<FileIO>(_suiteConfig.FileEvents.Select(f => f.Clone()));
        }

        internal FileIO NewFileEvent()
        {
            FileIO fileEvent = new FileIO();
            fileEvent.Action = FileSysIOAction.Deploy;
            fileEvent.FileSysIOType = FileSysIOType.File;
            fileEvent.OverridePermissions = false;
            fileEvent.ReplaceExisting = true;
            fileEvent.Schedules.Add(new Schedule
            {
                EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                Condition = null,
                StageSequence = Sequence.DuringInstallBeforeStage,
            });
            _suiteConfig.FileEvents.Add(fileEvent);
            ProjectChanged?.Invoke(this, fileEvent);
            return fileEvent.Clone();
        }

        internal void RemoveFileEvent(FileIO fileEvent)
        {
            if (fileEvent == null) throw new ArgumentNullException(nameof(fileEvent), "File event cannot be null");
            var matching = _suiteConfig.FileEvents.Where(p => p.Id.Equals(fileEvent.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.FileEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateFileEvent(FileIO fileEvent)
        {
            if (fileEvent == null) return;
            FileIO match = _suiteConfig.FileEvents.Where(e => e.Id == fileEvent.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided FileIO does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(fileEvent);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, fileEvent);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region PowerShellEvents
        internal ObservableCollection<PowerShell> GetPowerShellEvents()
        {
            return new ObservableCollection<PowerShell>(_suiteConfig.PowerShellEvents.Select(p => p.Clone()));
        }

        internal PowerShell NewPowerShellEvent()
        {
            PowerShell psEvent = new PowerShell
            {
                ScriptDoc = new TextDocument(),
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                },
                Context = Enums.Contexts.System,
            };
            _suiteConfig.PowerShellEvents.Add(psEvent);
            ProjectChanged?.Invoke(this, psEvent);
            return psEvent.Clone();
        }

        internal void RemovePowerShellEvent(PowerShell psEvent)
        {
            if (psEvent == null) throw new ArgumentNullException(nameof(psEvent), "PowerShell event cannot be null");
            var matching = _suiteConfig.PowerShellEvents.Where(p => p.Id.Equals(psEvent.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.PowerShellEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdatePowerShellEvent(PowerShell psEvent)
        {
            if (psEvent == null) return;
            PowerShell match = _suiteConfig.PowerShellEvents.Where(e => e.Id == psEvent.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided PowerShell does not exist the Core Service");
            var serializerSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Converters =
                {
                    new ColorToJson(),
                    new BitmapToJson(),
                    new TextDocumentToJson(),
                    new SecurityIdentifierToJson(),
                },
                MaxDepth = null,
            };
            string before = JsonConvert.SerializeObject(match, serializerSettings);
            match.UpdateFrom(psEvent);
            if (!string.Equals(before, JsonConvert.SerializeObject(match, serializerSettings), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, psEvent);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region RegistryEvents
        internal ObservableCollection<Registry> GetRegistryEvents()
        {
            return new ObservableCollection<Registry>(_suiteConfig.RegistryEvents.Select(r => r.Clone()));
        }

        internal Registry NewRegistryEvent()
        {
            Registry regEvent = new Registry
            {
                Action = RegAction.AddAmend,
                PropertyType = RegistryPropertyType.String,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                },
            };
            _suiteConfig.RegistryEvents.Add(regEvent);
            ProjectChanged?.Invoke(this, regEvent);
            return regEvent.Clone();
        }

        internal void RemoveRegistryEvent(Registry regEvent)
        {
            if (regEvent == null) throw new ArgumentNullException(nameof(regEvent), "Registry event cannot be null");
            var matching = _suiteConfig.RegistryEvents.Where(p => p.Id.Equals(regEvent.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.RegistryEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateRegistryEvent(Registry regEvent)
        {
            if (regEvent == null) return;
            Registry match = _suiteConfig.RegistryEvents.Where(e => e.Id == regEvent.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided Registry does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(regEvent);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, regEvent);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region ServiceClosureEvents
        internal ObservableCollection<ServiceClosure> GetServiceClosureEvents()
        {
            return new ObservableCollection<ServiceClosure>(_suiteConfig.ServiceClosureEvents.Select(s => s.Clone()));
        }

        internal ServiceClosure NewServiceClosureEvent()
        {
            ServiceClosure svcEvent = new ServiceClosure
            {
                Type = ClosureType.Disable,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                },
            };
            _suiteConfig.ServiceClosureEvents.Add(svcEvent);
            ProjectChanged?.Invoke(this, svcEvent);
            return svcEvent.Clone();
        }

        internal void RemoveServiceClosureEvent(ServiceClosure svcEvent)
        {
            if (svcEvent == null) throw new ArgumentNullException(nameof(svcEvent), "Service closure event cannot be null");
            var matching = _suiteConfig.ServiceClosureEvents.Where(p => p.Id.Equals(svcEvent.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.ServiceClosureEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateServiceClosureEvent(ServiceClosure svcEvent)
        {
            if (svcEvent == null) return;
            ServiceClosure match = _suiteConfig.ServiceClosureEvents.Where(e => e.Id == svcEvent.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided ServiceClosure does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(svcEvent);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, svcEvent);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region ProcessClosureEvents
        internal ObservableCollection<ProcessClosure> GetProcessClosureEvents()
        {
            return new ObservableCollection<ProcessClosure>(_suiteConfig.ProcClosureEvents.Select(p => p.Clone()));
        }

        internal ProcessClosure NewProcessClosureEvent()
        {
            ProcessClosure procEvent = new ProcessClosure
            {
                Action = ProcAction.StopAndBlock,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                },
            };
            _suiteConfig.ProcClosureEvents.Add(procEvent);
            ProjectChanged?.Invoke(this, procEvent);
            return procEvent.Clone();
        }

        internal void RemoveProcessClosureEvent(ProcessClosure procEvent)
        {
            if (procEvent == null) throw new ArgumentNullException(nameof(procEvent), "Service closure event cannot be null");
            var matching = _suiteConfig.ProcClosureEvents.Where(p => p.Id.Equals(procEvent.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.ProcClosureEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateProcessClosureEvent(ProcessClosure procEvent)
        {
            if (procEvent == null) return;
            ProcessClosure match = _suiteConfig.ProcClosureEvents.Where(e => e.Id == procEvent.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided ProcessClosure does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(procEvent);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, procEvent);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        #region ShortcutEvents
        internal ObservableCollection<Shortcut> GetShortcutEvents()
        {
            return new ObservableCollection<Shortcut>(_suiteConfig.ShortcutEvents.Select(s => s.Clone()));
        }

        internal Shortcut NewShortcutEvent()
        {
            Shortcut shortcut = new Shortcut
            {
                Name = "New Shortcut",
                ShortAction = ShortcutAction.Create,
                Context = Contexts.System,
                ShortcutType = ShortcutType.Program,
                IsManualIcon = false,
                Schedules = new List<Schedule>
                {
                    new Schedule
                    {
                        EventStage = GetStages().Where(s => s.Name.Equals("End")).First(),
                        Condition = null,
                        StageSequence = Sequence.DuringInstallBeforeStage,
                    },
                },
            };
            _suiteConfig.ShortcutEvents.Add(shortcut);
            ProjectChanged?.Invoke(this, shortcut);
            return shortcut.Clone();
        }

        internal void RemoveShortcutEvent(Shortcut shortcut)
        {
            if (shortcut == null) throw new ArgumentNullException(nameof(shortcut), "Shortcut event cannot be null");
            var matching = _suiteConfig.ShortcutEvents.Where(p => p.Id.Equals(shortcut.Id)).FirstOrDefault();
            if (matching != null)
            {
                _suiteConfig.ShortcutEvents.Remove(matching);
                ProjectChanged?.Invoke(this, matching);
            }
        }

        internal void UpdateShortcutEvent(Shortcut shortcut)
        {
            if (shortcut == null) return;
            Shortcut match = _suiteConfig.ShortcutEvents.Where(e => e.Id == shortcut.Id).FirstOrDefault();
            if (match == null) throw new ArgumentException("The provided Shortcut does not exist the Core Service");
            string before = JsonConvert.SerializeObject(match);
            match.UpdateFrom(shortcut);
            if (!string.Equals(before, JsonConvert.SerializeObject(match), StringComparison.Ordinal))
            {
                ProjectChanged?.Invoke(this, shortcut);
                CheckIfMatchesSaved();
            }
        }

        #endregion

        internal void ReorderEvents(SuiteProperty suiteProp, IEnumerable<Guid> orderedIds)
        {
            List<Guid> order = orderedIds.ToList();
            switch (suiteProp)
            {
                case SuiteProperty.CertEvents:
                    _suiteConfig.CertEvents = new ObservableCollection<Cert>(
                        order.Select(id => _suiteConfig.CertEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.DriverEvents:
                    _suiteConfig.DriverEvents = new ObservableCollection<Driver>(
                        order.Select(id => _suiteConfig.DriverEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.EnvironmentEvents:
                    _suiteConfig.EnvironmentEvents = new ObservableCollection<Environment>(
                        order.Select(id => _suiteConfig.EnvironmentEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.ExecutableEvents:
                    _suiteConfig.ExecutableEvents = new ObservableCollection<Executable>(
                        order.Select(id => _suiteConfig.ExecutableEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.ExtensionEvents:
                    _suiteConfig.ExtensionEvents = new ObservableCollection<BrowserExt>(
                        order.Select(id => _suiteConfig.ExtensionEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.FileEvents:
                    _suiteConfig.FileEvents = new ObservableCollection<FileIO>(
                        order.Select(id => _suiteConfig.FileEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.PowerShellEvents:
                    _suiteConfig.PowerShellEvents = new ObservableCollection<PowerShell>(
                        order.Select(id => _suiteConfig.PowerShellEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.ProcClosureEvents:
                    _suiteConfig.ProcClosureEvents = new ObservableCollection<ProcessClosure>(
                        order.Select(id => _suiteConfig.ProcClosureEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.RegistryEvents:
                    _suiteConfig.RegistryEvents = new ObservableCollection<Registry>(
                        order.Select(id => _suiteConfig.RegistryEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.ServiceClosureEvents:
                    _suiteConfig.ServiceClosureEvents = new ObservableCollection<ServiceClosure>(
                        order.Select(id => _suiteConfig.ServiceClosureEvents.First(e => e.Id == id)));
                    break;
                case SuiteProperty.ShortcutEvents:
                    _suiteConfig.ShortcutEvents = new ObservableCollection<Shortcut>(
                        order.Select(id => _suiteConfig.ShortcutEvents.First(e => e.Id == id)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(suiteProp), suiteProp, "Unknown Suite Property type, so can't reorder its events");
            }
            ProjectChanged?.Invoke(this, EventArgs.Empty);
            CheckIfMatchesSaved();
        }

        internal void AddClonedEvent(EventCore source)
        {
            EventCore clone = source.Clone();
            clone.Id = Guid.NewGuid();
            switch (clone)
            {
                case Cert cert:
                    _suiteConfig.CertEvents.Add(cert);
                    break;
                case Driver driver:
                    _suiteConfig.DriverEvents.Add(driver);
                    break;
                case Environment env:
                    _suiteConfig.EnvironmentEvents.Add(env);
                    break;
                case Executable exe:
                    _suiteConfig.ExecutableEvents.Add(exe);
                    break;
                case BrowserExt ext:
                    _suiteConfig.ExtensionEvents.Add(ext);
                    break;
                case FileIO file:
                    _suiteConfig.FileEvents.Add(file);
                    break;
                case PowerShell ps:
                    _suiteConfig.PowerShellEvents.Add(ps);
                    break;
                case ProcessClosure proc:
                    _suiteConfig.ProcClosureEvents.Add(proc);
                    break;
                case Registry reg:
                    _suiteConfig.RegistryEvents.Add(reg);
                    break;
                case ServiceClosure svc:
                    _suiteConfig.ServiceClosureEvents.Add(svc);
                    break;
                case Shortcut shortcutClone:
                    _suiteConfig.ShortcutEvents.Add(shortcutClone);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(source), source.GetType().Name, "Unknown event type for clone");
            }
            ProjectChanged?.Invoke(this, clone);
        }

        #region ConfigManagement
        internal void SetDefaultConfig(bool deleteAutoSave = true)
        {
            _suiteConfig.ProjectName = "Untitled Suite";
            _suiteConfig.Packages = new ObservableCollection<PackageBase>();
            _suiteConfig.RuleSets = new ObservableCollection<RuleSet>();
            _suiteConfig.CertEvents = new ObservableCollection<Cert>();
            _suiteConfig.DriverEvents = new ObservableCollection<Driver>();
            _suiteConfig.ProcClosureEvents = new ObservableCollection<ProcessClosure>();
            _suiteConfig.EnvironmentEvents = new ObservableCollection<Environment>();
            _suiteConfig.ExecutableEvents = new ObservableCollection<Executable>();
            _suiteConfig.ExtensionEvents = new ObservableCollection<BrowserExt>();
            _suiteConfig.FileEvents = new ObservableCollection<FileIO>();
            _suiteConfig.PowerShellEvents = new ObservableCollection<PowerShell>();
            _suiteConfig.RegistryEvents = new ObservableCollection<Registry>();
            _suiteConfig.ServiceClosureEvents = new ObservableCollection<ServiceClosure>();
            _suiteConfig.ShortcutEvents = new ObservableCollection<Shortcut>();
            SetDefaultStages();
            _suiteConfig.BuildSettings = new Build
            {
                UpgradeCode = Guid.NewGuid(),
                Revision = 1,
                SuiteVersion = new Version(1, 0, 0, 0),
                FailSafe = false,
                RefreshEnv = false,
                KeepCache = false,
                FailureRetry = false,
                RestartOnFailure = false,
                ReverseUninstall = false,
            };
            _suiteConfig.PopupSettings = new Popup
            {
                ShowPopupWarning = false,
                DelayDays = 7,
                ShowProgress = false,
                Timer = 40,
                TimerExpireAction = PopupAction.Continue,
                LoggedOffAction = PopupAction.Continue,
                LockedAction = PopupAction.Continue,
                ESPAction = PopupAction.Continue,
                LinkToProcClosures = false,
                PSCondition = "",
                InstallTxt = "The below apps will be closed before \nthe upgrade. Please save any work required \nbefore continuing.",
                UninstallTxt = "The below apps will be closed before \nthe rollback. Please save any work required \nbefore continuing.",
                DeployIconKind = Material.Icons.MaterialIconKind.PackageUp,
                HasInstallTxt = true,
                HasUninstallTxt = true,
                InstAction = "Upgrade",
                RemoveIconKind = Material.Icons.MaterialIconKind.Restore,
                UninstAction = "Rollback",
                HasPSCondition = false,
            };
            _lastSavedSnapshot = CreateConfigSnapshot();
            HasUnsavedChanges = false;
            if (deleteAutoSave) DeleteAutoSave();
            ProjectCleared?.Invoke(this, EventArgs.Empty);
        }

        private string CreateConfigSnapshot()
        {
            return JsonConvert.SerializeObject(_suiteConfig, typeof(SuiteProjectConfig), Formatting.None, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Converters =
                {
                    new ColorToJson(),
                    new BitmapToJson(),
                    new TextDocumentToJson(),
                    new SecurityIdentifierToJson(),
                },
                MaxDepth = null,
            });
        }

        private void CheckIfMatchesSaved()
        {
            if (_lastSavedSnapshot != null &&
                string.Equals(_lastSavedSnapshot, CreateConfigSnapshot(), StringComparison.Ordinal))
            {
                HasUnsavedChanges = false;
                ProjectMatchesSaved?.Invoke(this, EventArgs.Empty);
            }
        }

        #endregion

        #region Project
        internal async Task LoadAsync(string fileToLoad)
        {
            try
            {
                _isLoading = true;
                if (!File.Exists(fileToLoad))
                {
                    SetDefaultConfig();
                    return;
                }
                AppLog.Info($"Loading suite project from: {fileToLoad}", "SuiteCore");
                try
                {
                    await Task.Run(() =>
                    {
                        var config = JsonConvert.DeserializeObject<SuiteProjectConfig>(File.ReadAllText(fileToLoad), new JsonSerializerSettings()
                        {
                            TypeNameHandling = TypeNameHandling.Auto,
                            Converters =
                            {
                                new ColorToJson(),
                                new BitmapToJson(),
                                new TextDocumentToJson(),
                                new SecurityIdentifierToJson(),
                            },
                            MaxDepth = null,
                        });
                        if (config != null)
                        {
                            _suiteConfig = config;
                            return;
                        }
                    });
                    _suiteSaveFilePath = fileToLoad;
                    SetDefaultStages();
                    PopulateStagesOnEvents();
                    _suiteConfig.ProjectName = Path.GetFileNameWithoutExtension(_suiteSaveFilePath);
                    _lastSavedSnapshot = CreateConfigSnapshot();
                    if (!string.Equals(fileToLoad, _autoSaveFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        HasUnsavedChanges = false;
                        DeleteAutoSave();
                    }
                    ProjectLoaded?.Invoke(this, EventArgs.Empty);
                    ProjectNameChanged?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to load suite project from: {fileToLoad}", ex, "SuiteCore");
                    AppLog.Info("Will revert to default settings", "SuiteCore");
                    SetDefaultConfig();
                    LoadFailed?.Invoke(this, ex.Message);
                }
            }
            catch (IOException)
            {
                throw;
            }
            finally
            {
                _isLoading = false;
            }
        }

        internal async Task SaveAsync()
        {
            await PrivateSaveAsync(null);
        }

        internal async Task SaveAsync(string saveFilePath)
        {
            await PrivateSaveAsync(saveFilePath);
        }

        private async Task PrivateSaveAsync(string? saveFilePath)
        {
            if (_isLoading) { return; }
            if (string.IsNullOrWhiteSpace(saveFilePath) && string.IsNullOrWhiteSpace(_suiteSaveFilePath))
            {
                throw new ArgumentException("Save file path cannot be null or empty, if you haven't previously saved, or opened a project", nameof(saveFilePath));
            }
            await _suiteFileSemaphore.WaitAsync();
            string savePath = string.IsNullOrWhiteSpace(saveFilePath) ? _suiteSaveFilePath! : saveFilePath;
            try
            {
                AppLog.Info($"Saving project to: {savePath}", "SuiteCore");
                string json = JsonConvert.SerializeObject(_suiteConfig, typeof(SuiteProjectConfig), Formatting.Indented, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    Converters =
                    {
                        new ColorToJson(),
                        new BitmapToJson(),
                        new TextDocumentToJson(),
                        new SecurityIdentifierToJson(),
                    },
                    MaxDepth = null,
                });
                File.WriteAllText(savePath, json);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to save project to: {savePath}", ex, "SuiteCore");
                throw;
            }
            finally
            {
                _suiteFileSemaphore.Release();
                _suiteConfig.ProjectName = Path.GetFileNameWithoutExtension(savePath);
                _suiteSaveFilePath = savePath;
                _lastSavedSnapshot = CreateConfigSnapshot();
                _autoSaveDebounceTimer?.Dispose();
                _autoSaveDebounceTimer = null;
                HasUnsavedChanges = false;
                DeleteAutoSave();
                ProjectSaved?.Invoke(this, EventArgs.Empty);
            }
        }

        internal async Task ImportSuiteAsync(string? importFilePath, string outputFolder, IProgress<string>? progress = null)
        {
            AppLog.Info($"Importing suite from: {importFilePath}", "SuiteCore");
            SuiteProjectConfig newConfig = await SuiteImport.ImportSuiteAsync(importFilePath, outputFolder, progress);
            _suiteConfig = newConfig;
            SetDefaultStages();
            PopulateStagesOnEvents();
            _suiteConfig.ProjectName = Path.GetFileNameWithoutExtension(importFilePath);
            _suiteSaveFilePath = null;
            _lastSavedSnapshot = CreateConfigSnapshot();
            HasUnsavedChanges = true;
            DeleteAutoSave();
            ProjectLoaded?.Invoke(this, EventArgs.Empty);
            ProjectNameChanged?.Invoke(this, EventArgs.Empty);
        }

        internal string GetProjectName()
        {
            return _suiteConfig.ProjectName ?? "Untitled Suite";
        }

        internal string? GetSavePath()
        {
            return _suiteSaveFilePath;
        }

        #endregion
    }
}