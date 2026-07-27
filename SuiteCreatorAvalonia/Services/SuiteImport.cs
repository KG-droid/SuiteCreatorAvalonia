using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using SuiteOperations.Package;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;
using FileIO = SuiteCreatorAvalonia.Models.Events.FileSysIO;
using PowerShell = SuiteCreatorAvalonia.Models.Events.PowerShell;
using RuleSet = SuiteCreatorAvalonia.Models.Rules.RuleSet;

namespace SuiteCreatorAvalonia.Services
{
    public static class SuiteImport
    {
        private const int SfxMagic = unchecked((int)0x53554658); // 'SUFX' — matches SuiteSfxStub

        private static void ExtractZipPayload(string exePath, string outputZipPath)
        {
            using FileStream fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 8)
                throw new InvalidDataException("The selected file does not appear to be a valid Suite exe.");

            fs.Seek(-8, SeekOrigin.End);
            using BinaryReader br = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);
            int zipLength = br.ReadInt32();
            int magic = br.ReadInt32();
            if (magic != SfxMagic)
                throw new InvalidDataException("The selected file does not contain a valid Suite payload. Make sure you select a Suite exe created by SuiteCreator.");
            if (zipLength <= 0 || zipLength > fs.Length - 8)
                throw new InvalidDataException("The Suite exe payload length is invalid.");

            long zipStart = fs.Length - 8 - zipLength;
            fs.Seek(zipStart, SeekOrigin.Begin);

            Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
            using FileStream outFs = new FileStream(outputZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920];
            long remaining = zipLength;
            int read;
            while (remaining > 0 && (read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
            {
                outFs.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        public static async Task<SuiteProjectConfig> ImportSuiteAsync(string? importFilePath, string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(importFilePath))
                throw new ArgumentNullException(nameof(importFilePath));
            if (!File.Exists(importFilePath))
                throw new FileNotFoundException($"The specified file does not exist: {importFilePath}");
            if (!Path.GetExtension(importFilePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentOutOfRangeException(nameof(importFilePath), "Only Suite exe files (.exe) can be imported.");
            if (string.IsNullOrWhiteSpace(outputFolder))
                throw new ArgumentNullException(nameof(outputFolder));

            // Extract zip payload from the SFX exe to a temp path, then extract into outputFolder
            string tempZipPath = Path.Combine(Path.GetTempPath(), $"SuiteImport_{Guid.NewGuid():N}.zip");
            try
            {
                await Task.Run(() => ExtractZipPayload(importFilePath, tempZipPath));
                await Task.Run(() =>
                {
                    CleanOutputFolder(outputFolder);

                    Directory.CreateDirectory(outputFolder);
                    using ZipArchive zip = ZipFile.OpenRead(tempZipPath);
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        // Only extract SuiteConfig.scfg from the root; skip all other root-level files (executor binaries etc.)
                        bool isRootEntry = !entry.FullName.Contains('/');
                        if (isRootEntry && !entry.Name.Equals("SuiteConfig.scfg", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (entry.Name.Equals("SuiteUserPopup.exe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string destPath = Path.GetFullPath(Path.Combine(outputFolder, entry.FullName));
                        string outputFolderNormalised = Path.GetFullPath(outputFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (!destPath.StartsWith(outputFolderNormalised, StringComparison.OrdinalIgnoreCase))
                            continue; // zip-slip guard to protect again malicious zips extracting to folders outside the project output

                        if (entry.FullName.EndsWith('/')) // directory entry
                        {
                            Directory.CreateDirectory(destPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                    }
                });
            }
            finally
            {
                try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
            }

            // Locate the .scfg inside the extracted folder
            string? scfgPath = Directory.EnumerateFiles(outputFolder, "*.scfg", SearchOption.AllDirectories).FirstOrDefault();
            if (scfgPath == null)
                throw new FileNotFoundException($"No .scfg config file was found in the extracted suite content at: {outputFolder}");

            string extractRoot = Path.GetDirectoryName(scfgPath)!;

            SuiteOperations.SuiteExecConfig? execConfig = null;
            await Task.Run(() =>
            {
                string json = File.ReadAllText(scfgPath);
                execConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<SuiteOperations.SuiteExecConfig>(json, new Newtonsoft.Json.JsonSerializerSettings
                {
                    TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Auto,
                    Converters =
                    {
                        new Converters.ColorToJson(),
                        new Converters.BitmapToJson(),
                        new Converters.TextDocumentToJson(),
                    },
                    MaxDepth = null,
                });
            });
            if (execConfig == null)
                throw new InvalidOperationException("Failed to deserialize the suite config from the imported exe.");

            string GetPkgDir(Guid pkgId) => Path.Combine(extractRoot, "Package", pkgId.ToString());
            string GetEventDir(string eventType, Guid id) => Path.Combine(extractRoot, eventType, id.ToString());

            var newConfig = new SuiteProjectConfig
            {
                ProjectName = execConfig.ProjectName,
                Packages = new ObservableCollection<PackageBase>(),
                RuleSets = new ObservableCollection<RuleSet>(execConfig.RuleSets ?? new List<RuleSet>()),
                CertEvents = new ObservableCollection<Cert>(),
                DriverEvents = new ObservableCollection<Driver>(),
                ProcClosureEvents = new ObservableCollection<ProcessClosure>((execConfig.ProcClosureEvents ?? new List<SuiteOperations.Events.ProcClosureExecEvent>()).Select(e => new ProcessClosure
                {
                    Id = e.Id,
                    Name = e.Name,
                    Action = e.Action,
                    Schedules = e.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                })),
                EnvironmentEvents = new ObservableCollection<Environment>((execConfig.EnvironmentEvents ?? new List<SuiteOperations.Events.EnvExecEvent>()).Select(e => new Environment
                {
                    Id = e.Id,
                    Name = e.Name,
                    Value = e.Value,
                    Behaviour = e.Behaviour,
                    Separator = e.Separator,
                    IsPermanent = e.IsPermanent,
                    Schedules = e.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                })),
                ExecutableEvents = new ObservableCollection<Executable>(),
                ExtensionEvents = new ObservableCollection<BrowserExt>(),
                FileEvents = new ObservableCollection<FileIO>(),
                PowerShellEvents = new ObservableCollection<PowerShell>(),
                RegistryEvents = new ObservableCollection<Registry>(),
                ServiceClosureEvents = new ObservableCollection<ServiceClosure>((execConfig.ServiceClosureEvents ?? new List<SuiteOperations.Events.ServiceClosureExecEvent>()).Select(e => new ServiceClosure
                {
                    Id = e.Id,
                    Name = e.Name,
                    Type = e.Type,
                    Schedules = e.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                })),
                ShortcutEvents = new ObservableCollection<Shortcut>((execConfig.ShortcutEvents ?? new List<SuiteOperations.Events.ShortcutExecEvent>()).Select(e => new Shortcut
                {
                    Id = e.Id,
                    Name = e.Name,
                    ShortAction = e.ShortAction,
                    Context = e.Context,
                    ShortcutType = e.ShortcutType,
                    Target = e.Target,
                    Arguments = e.Arguments,
                    PlacementList = e.PlacementList != null ? new List<ShortcutPlacement>(e.PlacementList) : null,
                    WorkingDIR = e.WorkingDIR,
                    IsManualIcon = e.IsManualIcon,
                    IsPathIcon = e.IsPathIcon,
                    IconIndex = e.IconIndex,
                    IconPath = e.IconPath,
                    IsPermanent = e.IsPermanent,
                    Schedules = e.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                })),
                Stages = execConfig.Stages != null ? new ObservableCollection<Models.Common.Stage>(execConfig.Stages.Select(s => s.Clone())) : null,
                BuildSettings = execConfig.BuildSettings ?? new Build(),
                PopupSettings = execConfig.PopupSettings ?? new Popup()
            };

            foreach (PackageBase pkg in execConfig.Packages ?? new List<PackageBase>())
            {
                switch (pkg)
                {
                    case MSIExec msiExec:
                    {
                        MSIPkg msiPkg = new MSIPkg
                        {
                            Id = msiExec.Id,
                            Name = msiExec.Name,
                            RequirementRuleSetId = msiExec.RequirementRuleSetId,
                            SecureTransforms = msiExec.SecureTransforms,
                            Context = msiExec.Context,
                            IsCreateLog = msiExec.IsCreateLog,
                            IsRemoveFamily = msiExec.IsRemoveFamily,
                            LegacyLongFilePath = msiExec.LegacyLongFilePath,
                            LogPath = msiExec.LogPath,
                            RestartBehavior = msiExec.RestartBehavior,
                            Properties = msiExec.Properties?.Select(p => p.Clone()).ToList(),
                            Rollback = msiExec.Rollback?.Clone(),
                        };
                        string msiDir = GetPkgDir(msiExec.Id);
                        if (Directory.Exists(msiDir))
                        {
                            msiPkg.MSIPath = Directory.GetFiles(msiDir, "*.msi").FirstOrDefault() ?? string.Empty;
                            msiPkg.TransformsPath = Directory.GetFiles(msiDir, "*.mst").FirstOrDefault();
                            msiPkg.PatchPath = Directory.GetFiles(msiDir, "*.msp").FirstOrDefault();
                        }
                        newConfig.Packages.Add(msiPkg);
                        break;
                    }
                    case MSIRemovalExec msiRemExec:
                    {
                        MSIRemoval msiRem = new MSIRemoval
                        {
                            Id = msiRemExec.Id,
                            Name = msiRemExec.Name,
                            RequirementRuleSetId = msiRemExec.RequirementRuleSetId,
                            RemovalCode = msiRemExec.RemovalCode,
                            IsProductRemoval = msiRemExec.IsProductRemoval,
                            IsCreateLog = msiRemExec.IsCreateLog,
                            LogPath = msiRemExec.LogPath,
                            Architecture = msiRemExec.Architecture,
                            Context = msiRemExec.Context,
                            RestartBehavior = msiRemExec.RestartBehavior,
                            Properties = msiRemExec.Properties?.Select(p => p.Clone()).ToList(),
                        };
                        newConfig.Packages.Add(msiRem);
                        break;
                    }
                    case MSIxExec msixExec:
                    {
                        MSIxPkg msixPkg = new MSIxPkg
                        {
                            Id = msixExec.Id,
                            Name = msixExec.Name,
                            RequirementRuleSetId = msixExec.RequirementRuleSetId,
                            IsForceCloseFamily = msixExec.IsForceCloseFamily,
                            IsForceThisVersion = msixExec.IsForceThisVersion,
                            IsDeferInUse = msixExec.IsDeferInUse,
                            HasDependents = msixExec.HasDependents,
                            Dependents = msixExec.Dependents != null ? new List<string>(msixExec.Dependents) : null,
                            Rollback = msixExec.Rollback?.Clone(),
                        };
                        string msixDir = GetPkgDir(msixExec.Id);
                        if (Directory.Exists(msixDir))
                        {
                            string? msixFile = Directory.GetFiles(msixDir, "*.msix").FirstOrDefault()
                                           ?? Directory.GetFiles(msixDir, "*.msixbundle").FirstOrDefault();
                            if (msixFile != null) msixPkg.MSIxPath = msixFile;
                        }
                        newConfig.Packages.Add(msixPkg);
                        break;
                    }
                    case MSIxRemovalExec msixRemExec:
                    {
                        MSIxRemoval msixRem = new MSIxRemoval
                        {
                            Id = msixRemExec.Id,
                            Name = msixRemExec.Name,
                            RequirementRuleSetId = msixRemExec.RequirementRuleSetId,
                            IsSpecificVersionRemoval = msixRemExec.IsSpecificVersionRemoval,
                            PFN = msixRemExec.PFN,
                        };
                        newConfig.Packages.Add(msixRem);
                        break;
                    }
                    case OtherExec otherExec:
                    {
                        string otherDir = GetPkgDir(otherExec.Id);
                        OtherPkg otherPkg = new OtherPkg
                        {
                            Id = otherExec.Id,
                            Name = otherExec.Name,
                            RequirementRuleSetId = otherExec.RequirementRuleSetId,
                            DetectionRuleSetId = otherExec.DetectionRuleSetId,
                            RemovalType = otherExec.RemovalType,
                            Removal = otherExec.Removal?.Clone(),
                            Context = otherExec.Context,
                            SecureParams = otherExec.SecureParams,
                            LegacyLongFilePath = otherExec.LegacyLongFilePath,
                            RestartBehavior = otherExec.RestartBehavior,
                            CustomExitCodes = otherExec.CustomExitCodes,
                            ExitCodes = otherExec.ExitCodes?.Select(e => e.Clone()).ToList(),
                            InstallCommand = ResolveRelativeFileVars(otherExec.InstallCommand?.Select(c => c.Clone()).ToList(), otherDir),
                            RollbackCommand = ResolveRelativeFileVars(otherExec.RollbackCommand?.Select(c => c.Clone()).ToList(), otherDir),
                        };
                        if (Directory.Exists(otherDir))
                        {
                            FileSystemNode packageRoot = new FileSystemNode("PackageRoot", new ObservableCollection<FileSystemNode>());
                            List<FileSystemNode> otherNodes = await NodeFileSystem.GetNodesFromDirectoryAsync(otherDir, new Progress<NodeFileSystem.NodeProgress>(), CancellationToken.None);
                            packageRoot.SubNodes = new ObservableCollection<FileSystemNode>(otherNodes);
                            otherPkg.Files = new List<FileSystemNode> { packageRoot };
                        }
                        newConfig.Packages.Add(otherPkg);
                        break;
                    }
                    case OtherRemovalExec otherRemExec:
                    {
                        OtherRemovalPkg otherRemPkg = new OtherRemovalPkg
                        {
                            Id = otherRemExec.Id,
                            Name = otherRemExec.Name,
                            RequirementRuleSetId = otherRemExec.RequirementRuleSetId,
                            DetectionRuleSetId = otherRemExec.DetectionRuleSetId,
                            RemovalType = otherRemExec.RemovalType,
                            Removal = otherRemExec.Removal?.Clone(),
                            Context = otherRemExec.Context,
                            SecureParams = otherRemExec.SecureParams,
                            RestartBehavior = otherRemExec.RestartBehavior,
                            CustomExitCodes = otherRemExec.CustomExitCodes,
                            ExitCodes = otherRemExec.ExitCodes?.Select(e => e.Clone()).ToList(),
                        };
                        string otherRemDir = GetPkgDir(otherRemExec.Id);
                        if (Directory.Exists(otherRemDir))
                        {
                            FileSystemNode packageRoot = new FileSystemNode("PackageRoot", new ObservableCollection<FileSystemNode>());
                            List<FileSystemNode> otherRemNodes = await NodeFileSystem.GetNodesFromDirectoryAsync(otherRemDir, new Progress<NodeFileSystem.NodeProgress>(), CancellationToken.None);
                            packageRoot.SubNodes = new ObservableCollection<FileSystemNode>(otherRemNodes);
                            otherRemPkg.Files = new List<FileSystemNode> { packageRoot };
                        }
                        newConfig.Packages.Add(otherRemPkg);
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unrecognised package exec type during import: {pkg.GetType().FullName}");
                }
            }

            AssignCertEventFiles(execConfig.CertEvents ?? new List<SuiteOperations.Events.CertExecEvent>(), newConfig, GetEventDir);
            AssignDriverEventFiles(execConfig.DriverEvents ?? new List<SuiteOperations.Events.DriverExecEvent>(), newConfig, GetEventDir);
            await AssignExecutableEventFiles(execConfig.ExecutableEvents ?? new List<SuiteOperations.Events.ExecutableExecEvent>(), newConfig, GetEventDir);
            AssignExtensionEventFiles(execConfig.ExtensionEvents ?? new List<SuiteOperations.Events.BrowserExExecEvent>(), newConfig, GetEventDir);
            AssignFileEventFiles(execConfig.FileEvents ?? new List<SuiteOperations.Events.FileExecEvent>(), newConfig, GetEventDir);
            await AssignPowerShellEventFiles(execConfig.PowerShellEvents ?? new List<SuiteOperations.Events.PowerShellExecEvent>(), newConfig, GetEventDir);
            AssignRegistryEventFiles(execConfig.RegistryEvents ?? new List<SuiteOperations.Events.RegExecEvent>(), newConfig, GetEventDir);

            return newConfig;
        }

        private static string? ExtractSingleFileFromDir(string dir, string searchPattern = "*")
        {
            if (Directory.Exists(dir))
            {
                return Directory.GetFiles(dir, searchPattern).FirstOrDefault();
            }
            return null;
        }

        private static List<VariableText>? ResolveRelativeFileVars(List<VariableText>? command, string baseDir)
        {
            if (command == null) return null;
            return command.Select(v =>
            {
                if (v is RelativeFileVar rel && !string.IsNullOrEmpty(rel.RelativePath))
                {
                    string fullPath = Path.Combine(baseDir, rel.RelativePath);
                    return (VariableText)new FileVar(new FileSystemNode(fullPath));
                }
                return v.Clone();
            }).ToList();
        }

        private static void AssignCertEventFiles(IEnumerable<SuiteOperations.Events.CertExecEvent> certEvents, SuiteProjectConfig config, Func<string, Guid, string> getEventDir)
        {
            foreach (var cert in certEvents)
            {
                var certDir = getEventDir("Certificate", cert.Id);
                var file = ExtractSingleFileFromDir(certDir);
                if (file != null) cert.FilePath = file;
                config.CertEvents.Add(new Cert
                {
                    Id = cert.Id,
                    Action = cert.Action,
                    Store = cert.Store,
                    FilePath = cert.FilePath,
                    Thumbprint = cert.Thumbprint,
                    Password = cert.Password,
                    IsPermanent = cert.IsPermanent,
                    Schedules = cert.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                });
            }
        }

        private static void AssignDriverEventFiles(IEnumerable<SuiteOperations.Events.DriverExecEvent> driverEvents, SuiteProjectConfig config, Func<string, Guid, string> getEventDir)
        {
            foreach (var driver in driverEvents)
            {
                var driverDir = getEventDir("Driver", driver.Id);
                if (Directory.Exists(driverDir))
                {
                    string[] infFiles = Directory.GetFiles(driverDir, "*.inf", SearchOption.AllDirectories);
                    string? infFile = infFiles.FirstOrDefault(f =>
                        !string.IsNullOrWhiteSpace(driver.InfName) &&
                        Path.GetFileName(f).Equals(driver.InfName, StringComparison.OrdinalIgnoreCase))
                        ?? infFiles.FirstOrDefault();
                    if (infFile != null) driver.InfPath = infFile;
                }
                config.DriverEvents.Add(new Driver
                {
                    Id = driver.Id,
                    Action = driver.Action,
                    InfPath = driver.InfPath,
                    InfName = driver.InfName,
                    IsPermanent = driver.IsPermanent,
                    Schedules = driver.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                });
            }
        }

        private static async Task AssignExecutableEventFiles(IEnumerable<SuiteOperations.Events.ExecutableExecEvent> execEvents, SuiteProjectConfig config, Func<string, Guid, string> getEventDir)
        {
            foreach (var exe in execEvents)
            {
                var exeDir = getEventDir("Executable", exe.Id);
                FileSystemNode execRoot = new FileSystemNode("ExecRoot", new ObservableCollection<FileSystemNode>());
                if (Directory.Exists(exeDir))
                {
                    List<FileSystemNode> execNodes = await NodeFileSystem.GetNodesFromDirectoryAsync(exeDir, new Progress<NodeFileSystem.NodeProgress>(), CancellationToken.None);
                    execRoot.SubNodes = new ObservableCollection<FileSystemNode>(execNodes);
                }
                config.ExecutableEvents.Add(new Executable
                {
                    Id = exe.Id,
                    Command = ResolveRelativeFileVars(exe.Command?.Select(c => c.Clone()).ToList(), exeDir),
                    WorkingDIR = exe.WorkingDIR?.Select(w => w.Clone()).ToList(),
                    TreeNodes = new List<FileSystemNode> { execRoot },
                    ContinueOnError = exe.ContinueOnError,
                    ContinueOnNotFound = exe.ContinueOnNotFound,
                    SecureParams = exe.SecureParams,
                    Schedules = exe.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>(),
                });
            }
        }

        private static void AssignExtensionEventFiles(IEnumerable<SuiteOperations.Events.BrowserExExecEvent> extEvents, SuiteProjectConfig config, Func<string, Guid, string> getEventDir)
        {
            foreach (var ext in extEvents)
            {
                var extDir = getEventDir("BrowserExt", ext.Id);
                var file = ExtractSingleFileFromDir(extDir);
                if (file != null) ext.ExtPath = file;
                config.ExtensionEvents.Add(new BrowserExt
                {
                    Id = ext.Id,
                    Action = ext.Action,
                    ExtensionId = ext.ExtensionId,
                    Browser = ext.Browser,
                    Source = ext.Source,
                    ExtPath = ext.ExtPath,
                    IsPermanent = ext.IsPermanent,
                    Schedules = ext.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                });
            }
        }

        private static void AssignFileEventFiles(IEnumerable<SuiteOperations.Events.FileExecEvent> fileEvents, SuiteProjectConfig config, Func<string, Guid, string> getEventDir)
        {
            foreach (var fileEvent in fileEvents)
            {
                string? fileDir = getEventDir("File", fileEvent.Id);
                string? file = ExtractSingleFileFromDir(fileDir);
                if (string.IsNullOrWhiteSpace(file)) throw new FileNotFoundException($"File cannot be found for FileEvent: {fileEvent.Id}");
                config.FileEvents.Add(new FileIO
                {
                    Id = fileEvent.Id,
                    Action = fileEvent.Action,
                    FileSysIOType = fileEvent.FileSysIOType,
                    SourcePath = new List<VariableText> { new LiteralText(file) },
                    DestinationPath = fileEvent.DestinationPath?.Select(v => v.Clone()).ToList(),
                    ReplaceExisting = fileEvent.ReplaceExisting,
                    OverridePermissions = fileEvent.OverridePermissions,
                    Permissions = fileEvent.Permissions?.Select(p => p.Clone()).ToList(),
                    IsPermanent = fileEvent.IsPermanent,
                    Schedules = fileEvent.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                });
            }
        }

        private static async Task AssignPowerShellEventFiles(IEnumerable<SuiteOperations.Events.PowerShellExecEvent> psEvents, SuiteProjectConfig config, Func<string, Guid, string> getEventDir)
        {
            foreach (var psEvent in psEvents)
            {
                string psDir = getEventDir("PowerShell", psEvent.Id);
                string? file = ExtractSingleFileFromDir(psDir, "*.ps1");
                if (file != null) psEvent.ScriptPath = file;

                ObservableCollection<FileSystemNode> supportingFiles = new ObservableCollection<FileSystemNode>();
                if (Directory.Exists(psDir))
                {
                    List<FileSystemNode> psNodes = await NodeFileSystem.GetNodesFromDirectoryAsync(psDir, new Progress<NodeFileSystem.NodeProgress>(), CancellationToken.None);
                    foreach (FileSystemNode node in psNodes)
                    {
                        if (!string.Equals(node.FullPath, file, StringComparison.OrdinalIgnoreCase))
                            supportingFiles.Add(node);
                    }
                }

                config.PowerShellEvents.Add(new PowerShell
                {
                    Id = psEvent.Id,
                    ScriptName = psEvent.ScriptName,
                    ScriptDoc = psEvent.ScriptDoc,
                    ScriptPath = psEvent.ScriptPath,
                    ScriptArgs = psEvent.ScriptArgs,
                    SupportingFiles = supportingFiles,
                    Schedules = psEvent.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>(),
                    Context = psEvent.Context,
                });
            }
        }

        private static void AssignRegistryEventFiles(IEnumerable<SuiteOperations.Events.RegExecEvent> regEvents, SuiteProjectConfig config, Func<string, Guid, string> getEventDir)
        {
            foreach (var regEvent in regEvents)
            {
                var regDir = getEventDir("Registry", regEvent.Id);
                var file = ExtractSingleFileFromDir(regDir);
                if (file != null)
                {
                    regEvent.RegFilePath = new Uri(file);
                }
                config.RegistryEvents.Add(new Registry
                {
                    Id = regEvent.Id,
                    Action = regEvent.Action,
                    KeyPath = regEvent.KeyPath,
                    RegFilePath = regEvent.RegFilePath,
                    PropertyType = regEvent.PropertyType,
                    PropertyName = regEvent.PropertyName,
                    PropertyValue = regEvent.PropertyValue,
                    Overwrite = regEvent.Overwrite,
                    IsPermanent = regEvent.IsPermanent,
                    Schedules = regEvent.Schedules?.Select(s => s.Clone()).ToList() ?? new List<Schedule>()
                });
            }
        }

        private static void CleanOutputFolder(string outputFolder)
        {
            if (string.IsNullOrWhiteSpace(outputFolder)) return;

            try
            {
                Directory.CreateDirectory(outputFolder);

                // Delete SuiteConfig.scfg at the root of the output folder (case-insensitive)
                string? scfgFile = Directory.EnumerateFiles(outputFolder, "*").FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f), "SuiteConfig.scfg", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(scfgFile))
                {
                    try { File.Delete(scfgFile); } catch { }
                }

                // Named folders to remove if present at the root
                string[] foldersToDelete = { "Certificate", "Driver", "Executable", "File", "Package", "Popup" };
                foreach (string folderName in foldersToDelete)
                {
                    string? dir = Directory.EnumerateDirectories(outputFolder, "*").FirstOrDefault(d =>
                        string.Equals(Path.GetFileName(d), folderName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }
            }
            catch
            {
                // Swallow any errors — cleanup failure shouldn't prevent importing in most cases
            }
        }
    }
}


