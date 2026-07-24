using Avalonia.Controls;
using Avalonia.Labs.Gif;
using Avalonia.Platform;
using Material.Icons;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.Tools;
using SuiteOperations;
using SuiteOperations.Package;
using SuiteTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static SuiteTools.MSITools;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;

namespace SuiteCreatorAvalonia.Services
{
    public class SuiteBuilder
    {
        private SuiteCoreManager _suiteCoreManager;
        private AppSettingsControl _appSettingsControl;
        public bool _isBuilding = false;
        private List<SuiteValidationError>? _validationReport = null;
        private const string _uninstallKeyBase32 = @"Computer\HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        private const string _uninstallKeyBase64 = @"Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        public event EventHandler<SuiteBuiltEventArgs>? SuiteBuilt;
        private string _suiteUserPopupName = "SuiteUserPopup.exe";
        private string _suiteProgressPopupName = "SuiteProgressPopup.exe";
        private string _suiteSfxStubName = "SuiteSfxStub.exe";

        internal List<SuiteValidationError>? GetValidationReport()
        {
            return _validationReport;
        }

        public SuiteBuilder(SuiteCoreManager suiteCoreManager, AppSettingsControl appSettingsControl)
        {
            _suiteCoreManager = suiteCoreManager;
            _appSettingsControl = appSettingsControl;
        }

        internal async Task<List<SuiteValidationError>?> Build(CancellationToken cancellationToken, IProgress<string>? progress = null)
        {
            AppLog.Info($"Build started for project: {_suiteCoreManager.GetProjectName()}", "SuiteBuilder");
            progress?.Report("Validating configuration");
            ValidateConfig();
            if (_validationReport != null)
            {
                AppLog.Warning($"Build stopped, validation failed with {_validationReport.Count} error(s)", "SuiteBuilder");
                SuiteBuilt?.Invoke(this, new SuiteBuiltEventArgs(string.Empty, null));
                return _validationReport;
            }
            progress?.Report("Saving project");
            await _suiteCoreManager.SaveAsync();
            // Pre-fetch all UI-thread-bound data before entering Task.Run
            progress?.Report("Preparing build configuration");
            SuiteExecConfig suiteExecConfig = CreateSuiteExecConfig();
            List<BrowserExt> extensionEvents = _suiteCoreManager.GetExtensionEvents().ToList();
            List<Cert> certEvents = _suiteCoreManager.GetCertEvents().ToList();
            List<Driver> driverEvents = _suiteCoreManager.GetDriverEvents().ToList();
            List<PackageBase> packages = _suiteCoreManager.GetPackages().ToList();
            List<FileSysIO> fileEvents = _suiteCoreManager.GetFileEvents().ToList();
            List<Executable> executableEvents = _suiteCoreManager.GetExecutableEvents().ToList();
            List<PowerShell> powerShellEvents = _suiteCoreManager.GetPowerShellEvents().ToList();
            List<Registry> registryEvents = _suiteCoreManager.GetRegistryEvents().ToList();
            Popup popupSettings = _suiteCoreManager.GetPopupSettings();
            popupSettings.BackgroundColor = _appSettingsControl.GetCompanyLogoBackgroundColor();
            suiteExecConfig.PopupSettings.BackgroundColor = popupSettings.BackgroundColor;
            string? companyLogoBase64 = _appSettingsControl.GetCompanyLogoBase64();
            // Extract logo bytes on the UI thread to avoid cross-thread access violations inside Task.Run
            byte[]? companyLogoBytes = null;
            string? companyLogoExt = null;
            if (!string.IsNullOrWhiteSpace(companyLogoBase64))
            {
                Control companyLogo = _appSettingsControl.GetCompanyLogo();
                if (companyLogo is GifImage gif && gif.Source is GifStreamSource gifStream)
                {
                    Stream gifSrc = gifStream.GetStream();
                    if (gifSrc.CanSeek) gifSrc.Position = 0;
                    using var ms = new MemoryStream();
                    gifSrc.CopyTo(ms);
                    companyLogoBytes = ms.ToArray();
                    companyLogoExt = ".gif";
                }
                else if (companyLogo is Avalonia.Controls.Image img && img.Source is Avalonia.Media.Imaging.Bitmap bmp)
                {
                    using var ms = new MemoryStream();
                    bmp.Save(ms);
                    companyLogoBytes = ms.ToArray();
                    companyLogoExt = ".png";
                }
            }
            string? buildPath = null;
            DateTime buildTime = DateTime.Now;
            await Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                buildPath = CreateSuiteFiles(suiteExecConfig, extensionEvents, certEvents, driverEvents, packages, fileEvents, executableEvents, powerShellEvents, registryEvents, popupSettings, companyLogoBase64, companyLogoBytes, companyLogoExt, progress);
            }, cancellationToken);
            progress?.Report("Finalizing build");
            Build buildSettings = _suiteCoreManager.GetBuildSettings();
            buildSettings.Detection = $@"Set to a Detection Reg Key of: {(buildSettings.Architecture == SuiteCreatorAvalonia.Enums.Architecture.x64 ? _uninstallKeyBase64 : _uninstallKeyBase32)}\{buildSettings.UpgradeCode}, with a Property detection of: DisplayVersion greater or equal to {buildSettings.SuiteVersion}";
            _suiteCoreManager.UpdateBuildSettings(buildSettings);
            AppLog.Info($"Build completed, output at: {buildPath}", "SuiteBuilder");
            SuiteBuilt?.Invoke(this, new SuiteBuiltEventArgs(buildPath ?? string.Empty, buildTime));
            return null;
        }

        private SuiteExecConfig CreateSuiteExecConfig()
        {
            var log = new Logger.Log("C:\\Modern-Workplace-Logs\\SuiteBuilder.log");
            SuiteExecConfig suiteExecConfig = new();
            suiteExecConfig.ProjectName = _suiteCoreManager.GetProjectName();
            List<RuleSet> ruleSets = _suiteCoreManager.GetRuleSets().Select(rs => rs.Clone()).ToList();
            suiteExecConfig.RuleSets = ruleSets;
            List<PackageBase> execPackages = new List<PackageBase>();
            foreach (PackageBase pkg in _suiteCoreManager.GetPackages())
            {
                switch (pkg)
                {
                    case MSIPkg msiPkg:
                        if (string.IsNullOrEmpty(msiPkg.MSIPath)) throw new ArgumentNullException("MSIFile must be set before executing.", nameof(msiPkg.MSIPath));
                        Architecture architecture = GetMSISummaryInfoFromMSI(msiPkg.MSIPath).Is64Bit() ? Architecture.X64 : Architecture.X86;
                        Hashtable msiProps = GetMSIProperties(msiPkg.MSIPath);

                        string? upgradeCode = msiProps["UpgradeCode"] as string;
                        if (string.IsNullOrEmpty(upgradeCode))
                            throw new Exception("Could not determine UpgradeCode from the MSI");

                        string? productCode = msiProps["ProductCode"] as string;
                        if (string.IsNullOrEmpty(productCode))
                            throw new Exception("Could not determine ProductCode from the MSI");

                        execPackages.Add(new MSIExec(log)
                        {
                            Id = msiPkg.Id,
                            Name = msiPkg.Name,
                            RequirementRuleSetId = msiPkg.RequirementRuleSetId,
                            SecureTransforms = msiPkg.SecureTransforms,
                            Context = msiPkg.Context,
                            IsCreateLog = msiPkg.IsCreateLog,
                            IsRemoveFamily = msiPkg.IsRemoveFamily,
                            LegacyLongFilePath = msiPkg.LegacyLongFilePath,
                            LogPath = msiPkg.LogPath,
                            Properties = msiPkg.Properties?.Select(p => p.Clone()).ToList(),
                            Rollback = msiPkg.Rollback != null
                                ? new MSIExec(log)
                                {
                                    Id = msiPkg.Rollback.Id,
                                    Name = msiPkg.Rollback.Name,
                                    RequirementRuleSetId = msiPkg.Rollback.RequirementRuleSetId,
                                    SecureTransforms = msiPkg.Rollback.SecureTransforms,
                                    Context = msiPkg.Rollback.Context,
                                    IsCreateLog = msiPkg.Rollback.IsCreateLog,
                                    IsRemoveFamily = msiPkg.Rollback.IsRemoveFamily,
                                    LegacyLongFilePath = msiPkg.Rollback.LegacyLongFilePath,
                                    LogPath = msiPkg.Rollback.LogPath,
                                    Properties = msiPkg.Rollback.Properties?.Select(p => p.Clone()).ToList()
                                }
                                : null,
                            UpgradeCode = upgradeCode,
                            ProductCode = productCode,
                            Architecture = architecture
                        });
                        break;
                    case MSIxPkg msixPkg:
                        if (string.IsNullOrEmpty(msixPkg.MSIxPath)) throw new ArgumentNullException("MSIxPath must be set before executing.", nameof(msixPkg.MSIxPath));
                        string? packageFullName = null;
                        string? packageFamilyName = null;
                        using (var msix = new SuiteTools.MSIx(msixPkg.MSIxPath))
                        {
                            packageFullName = msix.PackageFullName;
                            packageFamilyName = msix.PackageFamilyName;
                        }
                        execPackages.Add(new MSIxExec(log)
                        {
                            Id = msixPkg.Id,
                            Name = msixPkg.Name,
                            RequirementRuleSetId = msixPkg.RequirementRuleSetId,
                            IsForceCloseFamily = msixPkg.IsForceCloseFamily,
                            IsForceThisVersion = msixPkg.IsForceThisVersion,
                            IsDeferInUse = msixPkg.IsDeferInUse,
                            HasDependents = msixPkg.HasDependents,
                            Dependents = msixPkg.Dependents != null ? new List<string>(msixPkg.Dependents) : null,
                            Rollback = !string.IsNullOrWhiteSpace(msixPkg.Rollback?.MSIxFile)
                                ? new MSIxExec(log)
                                {
                                    Id = msixPkg.Rollback.Id,
                                    Name = msixPkg.Rollback.Name,
                                    RequirementRuleSetId = msixPkg.Rollback.RequirementRuleSetId,
                                    MSIxFile = msixPkg.Rollback.MSIxFile,
                                    IsForceCloseFamily = msixPkg.Rollback.IsForceCloseFamily,
                                    IsForceThisVersion = msixPkg.Rollback.IsForceThisVersion,
                                    IsDeferInUse = msixPkg.Rollback.IsDeferInUse,
                                    HasDependents = msixPkg.Rollback.HasDependents,
                                    Dependents = msixPkg.Rollback.Dependents != null ? new List<string>(msixPkg.Rollback.Dependents) : null
                                }
                                : null,
                            PackageFamilyName = packageFamilyName,
                            PackageFullName = packageFullName,
                        });
                        break;
                    case MSIRemoval msiRem:
                        execPackages.Add(new MSIRemovalExec(log)
                        {
                            Id = msiRem.Id,
                            Name = msiRem.Name,
                            RequirementRuleSetId = msiRem.RequirementRuleSetId,
                            RemovalCode = msiRem.RemovalCode,
                            IsProductRemoval = msiRem.IsProductRemoval,
                            IsCreateLog = msiRem.IsCreateLog,
                            LogPath = msiRem.LogPath,
                            Context = msiRem.Context,
                            Architecture = msiRem.Architecture,
                            RestartBehavior = msiRem.RestartBehavior,
                            Properties = msiRem.Properties?.Select(p => p.Clone()).ToList()
                        });
                        break;
                    case MSIxRemoval msixRem:
                        execPackages.Add(new MSIxRemovalExec(log)
                        {
                            Id = msixRem.Id,
                            Name = msixRem.Name,
                            RequirementRuleSetId = msixRem.RequirementRuleSetId,
                            IsSpecificVersionRemoval = msixRem.IsSpecificVersionRemoval,
                            PFN = msixRem.PFN
                        });
                        break;
                    case OtherPkg otherPkg:
                        execPackages.Add(new OtherExec(log, ruleSets)
                        {
                            Id = otherPkg.Id,
                            Name = otherPkg.Name,
                            RequirementRuleSetId = otherPkg.RequirementRuleSetId,
                            DetectionRuleSetId = otherPkg.DetectionRuleSetId,
                            RollbackDetectionRuleSetId = otherPkg.RollbackDetectionRuleSetId,
                            RemovalType = otherPkg.RemovalType,
                            Removal = otherPkg.Removal?.Clone(),
                            Context = otherPkg.Context,
                            SecureParams = otherPkg.SecureParams,
                            LegacyLongFilePath = otherPkg.LegacyLongFilePath,
                            RestartBehavior = otherPkg.RestartBehavior,
                            CustomExitCodes = otherPkg.CustomExitCodes,
                            ExitCodes = otherPkg.ExitCodes?.Select(e => e.Clone()).ToList(),
                            InstallCommand = otherPkg.InstallCommand?.Select(c => c is FileVar fv ? new RelativeFileVar(Path.GetFileName(fv.Node.FullPath)) : c.Clone()).ToList(),
                            RollbackCommand = otherPkg.RollbackCommand?.Select(c => c is FileVar fv ? new RelativeFileVar(Path.GetFileName(fv.Node.FullPath)) : c.Clone()).ToList()
                        });
                        break;
                    case OtherRemovalPkg otherRemPkg:
                        execPackages.Add(new OtherRemovalExec(log, ruleSets)
                        {
                            Id = otherRemPkg.Id,
                            Name = otherRemPkg.Name,
                            RequirementRuleSetId = otherRemPkg.RequirementRuleSetId,
                            DetectionRuleSetId = otherRemPkg.DetectionRuleSetId,
                            RemovalType = otherRemPkg.RemovalType,
                            Removal = otherRemPkg.Removal?.Clone(),
                            Context = otherRemPkg.Context,
                            SecureParams = otherRemPkg.SecureParams,
                            RestartBehavior = otherRemPkg.RestartBehavior,
                            CustomExitCodes = otherRemPkg.CustomExitCodes,
                            ExitCodes = otherRemPkg.ExitCodes?.Select(e => e.Clone()).ToList()
                        });
                        break;
                    default:
                        throw new Exception($"Unknown package type: {pkg.GetType().Name}");
                }
            }

            suiteExecConfig.Packages = execPackages;
            suiteExecConfig.CertEvents = _suiteCoreManager.GetCertEvents().Select(cert => new SuiteOperations.Events.CertExecEvent(log)
            {
                Action = cert.Action,
                Store = cert.Store,
                Thumbprint = string.IsNullOrEmpty(cert.Thumbprint) ? cert.GetThumbprintFromFile() : cert.Thumbprint,
                Password = cert.Password,
                Schedules = cert.Schedules.Select(s => s).ToList(),
                Id = cert.Id,
                IsPermanent = cert.IsPermanent,
            }).ToList();
            suiteExecConfig.DriverEvents = _suiteCoreManager.GetDriverEvents().Select(driver => new SuiteOperations.Events.DriverExecEvent(log)
            {
                Id = driver.Id,
                Action = driver.Action,
                InfPath = driver.InfPath,
                // Removal identifies the driver by its original .inf file name, so derive it from
                // the .inf path for install events where it hasn't been set explicitly.
                InfName = string.IsNullOrWhiteSpace(driver.InfName) && !string.IsNullOrWhiteSpace(driver.InfPath)
                    ? Path.GetFileName(driver.InfPath)
                    : driver.InfName,
                IsPermanent = driver.IsPermanent,
                Schedules = driver.Schedules.Select(s => s).ToList(),
            }).ToList();
            suiteExecConfig.ProcClosureEvents = _suiteCoreManager.GetProcessClosureEvents().Select(proc => new SuiteOperations.Events.ProcClosureExecEvent(log)
            {
                Id = proc.Id,
                Action = proc.Action,
                Name = proc.Name,
                Schedules = proc.Schedules.Select(s => s).ToList(),
            }).ToList();
            suiteExecConfig.EnvironmentEvents = _suiteCoreManager.GetEnvironmentEvents().Select(env => new SuiteOperations.Events.EnvExecEvent(log)
            {
                Id = env.Id,
                Name = env.Name,
                Value = env.Value,
                Behaviour = env.Behaviour,
                Separator = env.Separator,
                Schedules = env.Schedules.Select(s => s).ToList(),
                IsPermanent = env.IsPermanent,
            }).ToList();
            suiteExecConfig.ExecutableEvents = _suiteCoreManager.GetExecutableEvents().Select(exe => new SuiteOperations.Events.ExecutableExecEvent(log)
            {
                Id = exe.Id,
                Command = exe.Command?.Select(c => c is FileVar fv ? new RelativeFileVar(Path.GetFileName(fv.Node.FullPath)) : c).ToList(),
                WorkingDIR = exe.WorkingDIR?.Select(w => w is FileVar fv ? new RelativeFileVar(Path.GetFileName(fv.Node.FullPath)) : w).ToList(),
                ContinueOnError = exe.ContinueOnError,
                ContinueOnNotFound = exe.ContinueOnNotFound,
                SecureParams = exe.SecureParams,
                Schedules = exe.Schedules.Select(s => s).ToList(),
            }).ToList();
            suiteExecConfig.ExtensionEvents = _suiteCoreManager.GetExtensionEvents().Select(ext => new SuiteOperations.Events.BrowserExExecEvent(log)
            {
                Id = ext.Id,
                Action = ext.Action,
                ExtensionId = ext.ExtensionId,
                Browser = ext.Browser,
                Source = ext.Source,
                ExtPath = ext.ExtPath,
                Schedules = ext.Schedules.Select(s => s).ToList(),
                IsPermanent = ext.IsPermanent,
            }).ToList();
            suiteExecConfig.FileEvents = _suiteCoreManager.GetFileEvents().Select(file => new SuiteOperations.Events.FileExecEvent(log)
            {
                Id = file.Id,
                Action = file.Action,
                FileSysIOType = file.FileSysIOType,
                SourcePath = file.SourcePath,
                DestinationPath = file.DestinationPath,
                ReplaceExisting = file.ReplaceExisting,
                OverridePermissions = file.OverridePermissions,
                Permissions = file.Permissions?.Select(p => p.Clone()).ToList(),
                Schedules = file.Schedules.Select(s => s).ToList(),
                TargetsName = file.SourcePath != null ?
                    Path.GetFileName(
                        string.Join(
                            "", file.SourcePath.Select(s => s.GetValue())
                        )
                    ) :
                    Path.GetFileName(
                        string.Join(
                            "", file.DestinationPath.Select(s => s.GetValue())
                        )
                    ),
                IsPermanent = file.IsPermanent,

            }).ToList();
            suiteExecConfig.PowerShellEvents = _suiteCoreManager.GetPowerShellEvents().Select(ps => new SuiteOperations.Events.PowerShellExecEvent(log)
            {
                Id = ps.Id,
                ScriptName = ps.ScriptName,
                ScriptPath = ps.ScriptPath,
                ScriptDoc = ps.ScriptDoc,
                ScriptArgs = ps.ScriptArgs,
                Context = ps.Context,
                Schedules = ps.Schedules.Select(s => s).ToList()
            }).ToList();
            suiteExecConfig.RegistryEvents = _suiteCoreManager.GetRegistryEvents().Select(reg => new SuiteOperations.Events.RegExecEvent(log)
            {
                Id = reg.Id,
                Action = reg.Action,
                KeyPath = reg.KeyPath,
                RegFilePath = reg.RegFilePath,
                PropertyType = reg.PropertyType,
                PropertyName = reg.PropertyName,
                PropertyValue = reg.PropertyValue,
                Overwrite = reg.Overwrite,
                Schedules = reg.Schedules.Select(s => s).ToList(),
                IsPermanent = reg.IsPermanent,
            }).ToList();
            suiteExecConfig.ServiceClosureEvents = _suiteCoreManager.GetServiceClosureEvents().Select(svc => new SuiteOperations.Events.ServiceClosureExecEvent(log)
            {
                Id = svc.Id,
                Type = svc.Type,
                Name = svc.Name,
                Schedules = svc.Schedules.Select(s => s).ToList()
            }).ToList();
            suiteExecConfig.ShortcutEvents = _suiteCoreManager.GetShortcutEvents().Select(shortcut => new SuiteOperations.Events.ShortcutExecEvent(log)
            {
                Id = shortcut.Id,
                Name = shortcut.Name,
                ShortAction = shortcut.ShortAction,
                Context = shortcut.Context,
                ShortcutType = shortcut.ShortcutType,
                Target = shortcut.Target,
                Arguments = shortcut.Arguments,
                PlacementList = shortcut.PlacementList != null ? new(shortcut.PlacementList) : null,
                WorkingDIR = shortcut.WorkingDIR,
                IsManualIcon = shortcut.IsManualIcon,
                IsPathIcon = shortcut.IsPathIcon,
                IconIndex = shortcut.IconIndex,
                IconPath = shortcut.IconPath,
                Schedules = shortcut.Schedules.Select(s => s).ToList(),
                IsPermanent = shortcut.IsPermanent,
            }).ToList();
            suiteExecConfig.Stages = _suiteCoreManager.GetStages()?.ToList();
            suiteExecConfig.BuildSettings = _suiteCoreManager.GetBuildSettings();
            Popup popupSettingsConfig = _suiteCoreManager.GetPopupSettings();
            string? globalCondition = _appSettingsControl.GetGlobalPopupCondition();
            if (!string.IsNullOrWhiteSpace(globalCondition))
            {
                popupSettingsConfig.HasGlobalPSCondition = true;
                popupSettingsConfig.GlobalPSCondition = globalCondition;
            }
            suiteExecConfig.PopupSettings = popupSettingsConfig;
            return suiteExecConfig;
        }

        private string? GetVarPathAsString(List<VariableText>? varPath)
        {
            string result = string.Empty;
            if (varPath != null && varPath.Count > 0)
            {
                foreach (VariableText var in varPath)
                {
                    if (var is LiteralText lText)
                    {
                        result += lText.Value;
                    }
                    else if (var is SpecialDIR sdir)
                    {
                        result += sdir.GetValue();
                    }
                    else if (var is FileVar filevar)
                    {
                        result += filevar.Node.FullPath;
                    }
                    else
                    {
                        throw new Exception("Unknown VariableText type. Only LiteralText, File, & SpecialDIR are allowed");
                    }
                }
            }
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }

        private string CreateSuiteFiles(
            SuiteExecConfig suiteExecConfig,
            List<BrowserExt> extensionEvents,
            List<Cert> certEvents,
            List<Driver> driverEvents,
            List<PackageBase> packages,
            List<FileSysIO> fileEvents,
            List<Executable> executableEvents,
            List<PowerShell> powerShellEvents,
            List<Registry> registryEvents,
            Popup popupSettings,
            string? companyLogoBase64,
            byte[]? companyLogoBytes,
            string? companyLogoExt,
            IProgress<string>? progress = null)
        {
            string projectName = _suiteCoreManager.GetProjectName();
            string? savePath = _suiteCoreManager.GetSavePath();
            if (string.IsNullOrWhiteSpace(savePath))
                throw new Exception("A Project cannot be built before it is saved");
            string saveDir = Path.GetDirectoryName(savePath);
            string saveFileName = Path.GetFileNameWithoutExtension(savePath);
            string tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string buildDir = Path.Combine(saveDir, $"{saveFileName}-Build");
            string stage = "Initializing";
            progress?.Report(stage);
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
                Directory.CreateDirectory(tempRoot);
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
                Directory.CreateDirectory(tempRoot);

                // Create config file
                stage = "Creating configuration file";
                progress?.Report(stage);
                suiteExecConfig.ToJson(Path.Combine(tempRoot, "SuiteConfig.scfg"));

                // Copy over suite executor
                stage = "Copying SuiteExecutor";
                progress?.Report(stage);
                var suiteExecSourceDir = Path.Combine(AppContext.BaseDirectory, "SuiteExec");
                if (Directory.Exists(suiteExecSourceDir))
                {
                    NativeTools.CopyWithRoboCopy(suiteExecSourceDir, tempRoot, false, null, excludeFiles: new[] { _suiteSfxStubName, _suiteUserPopupName, _suiteProgressPopupName });
                }
                else
                {
                    throw new Exception("Suite executor files not found. Expected at: " + suiteExecSourceDir);
                }

                // Brower Extension Files
                stage = "Copying browser extension files";
                progress?.Report(stage);
                foreach (BrowserExt browEvent in extensionEvents)
                {
                    if (browEvent.Action == Enums.ExtAction.Install && !string.IsNullOrWhiteSpace(browEvent.ExtPath))
                    {
                        string fileDir = Path.Combine(tempRoot, "BrowserExt", browEvent.Id.ToString());
                        NativeTools.CopyWithRoboCopy(browEvent.ExtPath, fileDir, false);
                    }
                }

                // Cert files
                stage = "Copying certificate files";
                progress?.Report(stage);
                foreach (Cert certEvent in certEvents)
                {
                    if (certEvent.Action == Enums.CertAction.Add && !string.IsNullOrWhiteSpace(certEvent.FilePath))
                    {
                        string fileDir = Path.Combine(tempRoot, "Certificate", certEvent.Id.ToString());
                        NativeTools.CopyWithRoboCopy(certEvent.FilePath, fileDir, false);
                    }
                }

                // Driver files (copy the whole folder the .inf sits in, since the .sys/.cat files are needed alongside it)
                stage = "Copying driver files";
                progress?.Report(stage);
                foreach (Driver driverEvent in driverEvents)
                {
                    if (driverEvent.Action == Enums.DriverAction.Install && !string.IsNullOrWhiteSpace(driverEvent.InfPath))
                    {
                        string? driverSourceDir = Path.GetDirectoryName(driverEvent.InfPath);
                        if (string.IsNullOrWhiteSpace(driverSourceDir))
                        {
                            throw new InvalidOperationException($"Could not determine the folder containing the driver .inf file: {driverEvent.InfPath}");
                        }
                        string fileDir = Path.Combine(tempRoot, "Driver", driverEvent.Id.ToString());
                        NativeTools.CopyWithRoboCopy(driverSourceDir, fileDir, false);
                    }
                }

                // Package files
                stage = "Copying package files";
                progress?.Report(stage);
                foreach (PackageBase pkgBase in packages)
                {
                    switch (pkgBase)
                    {
                        case MSIPkg msiPkg:
                            if (!string.IsNullOrWhiteSpace(msiPkg.MSIPath) && File.Exists(msiPkg.MSIPath))
                            {
                                string pkgDir = Path.Combine(tempRoot, "Package", msiPkg.Id.ToString());
                                NativeTools.CopyWithRoboCopy(msiPkg.MSIPath, pkgDir);
                            }
                            else
                            {
                                throw new FileNotFoundException($"MSI file for package '{pkgBase.Name}' not found at path '{msiPkg.MSIPath}'");
                            }
                            break;
                        case OtherPkg other:
                            if (other.Files != null && other.Files.Count() > 0)
                            {
                                string pkgDir = Path.Combine(tempRoot, "Package", other.Id.ToString());
                                if (other.Files.FirstOrDefault()?.SubNodes != null && other.Files.FirstOrDefault()?.SubNodes?.Count > 0 )
                                    CopyFileSystemNodes(other.Files.FirstOrDefault().SubNodes, pkgDir); // Go into first node, as its the root of the package files
                            }
                            break;
                        case OtherRemovalPkg otherRem:
                            if (otherRem.Files != null && otherRem.Files.Count() > 0)
                            {
                                string pkgDir = Path.Combine(tempRoot, "Package", otherRem.Id.ToString());
                                if (otherRem.Files.FirstOrDefault()?.SubNodes != null && otherRem.Files.FirstOrDefault()?.SubNodes?.Count > 0)
                                    CopyFileSystemNodes(otherRem.Files.FirstOrDefault()?.SubNodes, pkgDir); // Go into first node, as its the root of the package files
                            }
                            break;
                        case MSIxPkg msix:
                            if (!string.IsNullOrWhiteSpace(msix.MSIxPath) && File.Exists(msix.MSIxPath))
                            {
                                string pkgDir = Path.Combine(tempRoot, "Package", msix.Id.ToString());
                                NativeTools.CopyWithRoboCopy(msix.MSIxPath, pkgDir);
                            }
                            else
                            {
                                throw new FileNotFoundException($"MSIx file for package '{msix.Name}' not found at path '{msix.MSIxPath}'");
                            }
                            break;
                    }
                }

                // File event files
                stage = "Copying file event files";
                progress?.Report(stage);
                foreach (FileSysIO fileEvent in fileEvents)
                {
                    if (fileEvent.Action == Enums.FileSysIOAction.Deploy)
                    {
                        string fileDir = Path.Combine(tempRoot, "File", fileEvent.Id.ToString());
                        string? sourcePath = GetVarPathAsString(fileEvent.SourcePath);
                        if (string.IsNullOrWhiteSpace(sourcePath))
                            throw new FileNotFoundException($"File deploy event '{fileEvent.Id}' has no source path set; cannot build the suite.");
                        NativeTools.CopyWithRoboCopy(sourcePath, fileDir, false);
                    }
                }

                // Executable event files
                stage = "Copying executable event files";
                progress?.Report(stage);
                foreach (Executable exeEvent in executableEvents)
                {
                    if (exeEvent.TreeNodes != null && exeEvent.TreeNodes.Count > 0)
                    {
                        string exeDir = Path.Combine(tempRoot, "Executable", exeEvent.Id.ToString());
                        if (exeEvent.TreeNodes.FirstOrDefault()?.SubNodes != null && exeEvent.TreeNodes.FirstOrDefault()?.SubNodes?.Count > 0)
                            CopyFileSystemNodes(exeEvent.TreeNodes.FirstOrDefault()?.SubNodes, exeDir); // Go into first node, as its the root of the event
                    }
                }

                // PowerShell event files
                stage = "Copying PowerShell event files";
                progress?.Report(stage);
                foreach (PowerShell psEvent in powerShellEvents)
                {
                    if (!string.IsNullOrWhiteSpace(psEvent.ScriptPath))
                    {
                        string exeDir = Path.Combine(tempRoot, "PowerShell", psEvent.Id.ToString());
                        NativeTools.CopyWithRoboCopy(psEvent.ScriptPath, exeDir, false);
                    }
                    if (psEvent.SupportingFiles != null && psEvent.SupportingFiles.Count > 0)
                    {
                        string supportDir = Path.Combine(tempRoot, "PowerShell", psEvent.Id.ToString(), "Support");
                        CopyFileSystemNodes(psEvent.SupportingFiles, supportDir);
                    }
                }

                // Registry event files
                stage = "Copying registry event files";
                progress?.Report(stage);
                foreach (Registry regEvent in registryEvents)
                {
                    if (regEvent.RegFilePath != null && regEvent.RegFilePath.IsFile)
                    {
                        string exeDir = Path.Combine(tempRoot, "Registry", regEvent.Id.ToString());
                        NativeTools.CopyWithRoboCopy(regEvent.RegFilePath.LocalPath, exeDir, false);
                    }
                }

                // A blocked exe launch attempt shows a notice via SuiteUserPopup.exe too (see
                // ProcClosureExecEvent.BuildDebuggerValue), so the popup files must be built into the
                // suite for that case even when the full warning/progress popup isn't enabled.
                bool hasBlockingProcClosures = suiteExecConfig.ProcClosureEvents?
                    .Any(p => p.Action == SuiteCreatorAvalonia.Enums.ProcAction.StopAndBlock) ?? false;

                // Popup files
                if (popupSettings.ShowPopupWarning || popupSettings.ShowProgress || hasBlockingProcClosures)
                {
                    stage = "Creating popup files";
                    progress?.Report(stage);

                    var popDIR = Path.Combine(tempRoot, "Popup");
                    Directory.CreateDirectory(popDIR);

                    // Suite logo is shared by the warning popup and the progress popup
                    if (string.IsNullOrWhiteSpace(popupSettings.SuiteLogoBase64))
                    {
                        SaveAvaloniaAssetToFile(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/SuiteLogo.png"), Path.Combine(popDIR, "SuiteLogo.png"));
                    }
                    else
                    {
                        ImageLoader.GetFromBase64(popupSettings.SuiteLogoBase64)?.Save(Path.Combine(popDIR, "SuiteLogo.png"));
                    }

                    if (popupSettings.ShowPopupWarning || hasBlockingProcClosures)
                    {
                        stage = "Creating warning popup files";
                        progress?.Report(stage);

                        var popConfig = CreatePopConfig(popupSettings, suiteExecConfig);
                        var popJsonPath = Path.Combine(popDIR, "popconfig.json");
                        var popJson = JsonSerializer.Serialize(popConfig, new JsonSerializerOptions
                        {
                            WriteIndented = true,
                        });
                        File.WriteAllText(popJsonPath, popJson);
                        // Company Logo
                        if (string.IsNullOrWhiteSpace(companyLogoBase64) || companyLogoBytes == null)
                        {
                            SaveAvaloniaAssetToFile(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/SuiteCreatorLogoImage.png"), Path.Combine(popDIR, "CompanyLogo.png"));
                        }
                        else
                        {
                            // Write the pre-extracted logo bytes directly — no UI thread access needed
                            string logoDestPath = Path.Combine(popDIR, $"CompanyLogo{companyLogoExt}");
                            Directory.CreateDirectory(Path.GetDirectoryName(logoDestPath)!);
                            File.WriteAllBytes(logoDestPath, companyLogoBytes);
                        }
                        // Popup exec copy
                        var suiteUserPopPath = Path.Combine(AppContext.BaseDirectory, "SuiteExec", _suiteUserPopupName);
                        if (File.Exists(suiteUserPopPath))
                        {
                            File.Copy(suiteUserPopPath, Path.Combine(tempRoot, "Popup", "SuiteUserPopup.exe"), true);
                        }
                        else
                        {
                            throw new Exception("Suite user popup executable not found. Expected at: " + suiteUserPopPath);
                        }
                    }

                    if (popupSettings.ShowProgress)
                    {
                        stage = "Creating progress popup files";
                        progress?.Report(stage);

                        var suiteProgressPopPath = Path.Combine(AppContext.BaseDirectory, "SuiteExec", _suiteProgressPopupName);
                        if (File.Exists(suiteProgressPopPath))
                        {
                            File.Copy(suiteProgressPopPath, Path.Combine(tempRoot, "Popup", "SuiteProgressPopup.exe"), true);
                        }
                        else
                        {
                            throw new Exception("Suite progress popup executable not found. Expected at: " + suiteProgressPopPath);
                        }
                    }
                }

                // Move temp to final location and create bootstrapper
                if (Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories).Any())
                {
                    stage = "Compressing suite files";
                    progress?.Report(stage);
                    string zipGuid = Guid.NewGuid().ToString();
                    string zipPath = Path.Combine(Path.GetTempPath(), $"{zipGuid}.zip");
                    ZipFile.CreateFromDirectory(tempRoot, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                    if (Directory.Exists(buildDir))
                    {
                        Directory.Delete(buildDir, true);
                    }
                    Directory.CreateDirectory(buildDir);
                    var finalZipPath = Path.Combine(buildDir, "SuiteFiles.zip");
                    File.Move(zipPath, finalZipPath, true);
                    stage = "Creating self-extracting bootstrapper";
                    progress?.Report(stage);
                    var sfxPath = Path.Combine(buildDir, $"{projectName}.exe");
                    if (TryCreateSelfExtractingBootstrapper(finalZipPath, sfxPath))
                    {
                        try { File.Delete(finalZipPath); } catch { }
                    }
                }
                stage = "Cleaning up temporary files";
                progress?.Report(stage);
                DeleteDIR(tempRoot);
                return Path.Combine(saveDir, buildDir);
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(tempRoot))
                    {
                        DeleteDIR(tempRoot);
                    }
                }
                catch (Exception cleanupEx)
                {
                    AppLog.Warning($"Failed to clean up build temp folder: {tempRoot}, error: {cleanupEx.Message}", "SuiteBuilder");
                }
                throw new Exception($"Build failed at stage: {stage}, error: {ex.Message}", ex);
            }
        }

        private void DeleteDIR(string tempRoot)
        {
            const int maxRetries = 5;
            const int delayMs = 2000;
            int attempt = 0;
            bool deleted = false;
            Exception? lastException = null;

            while (attempt < maxRetries && !deleted)
            {
                try
                {
                    // Chaning attribute required for deletion via Directory.Delete of the temp folders
                    foreach (var file in Directory.GetFiles(tempRoot, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);

                    foreach (var subDir in Directory.GetDirectories(tempRoot, "*", SearchOption.AllDirectories))
                        File.SetAttributes(subDir, File.GetAttributes(subDir) & ~FileAttributes.ReadOnly);
                    Directory.Delete(tempRoot, true);
                    deleted = true;
                }
                catch (IOException ex)
                {
                    lastException = ex;
                    attempt++;
                    Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastException = ex;
                    attempt++;
                    Thread.Sleep(delayMs);
                }
            }

            if (!deleted && lastException != null)
            {
                throw new Exception($"Failed to delete: {tempRoot}, after {maxRetries} attempts. Last error: {lastException.Message}", lastException);
            }
        }

        internal List<SuiteValidationError>? ValidateConfig()
        {
            // Validate the suite configuration
            List<SuiteValidationError> validationErrors = new();

            // Validate Packages
            foreach (PackageBase pkg in _suiteCoreManager.GetPackages())
            {
                string? valResult = pkg.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = pkg
                    });
                }
            }

            // Validate RuleSets
            foreach (RuleSet ruleSet in _suiteCoreManager.GetRuleSets())
            {
                string? valResult = ruleSet.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = ruleSet
                    });
                }
            }

            // Validate CertEvents
            foreach (Cert cert in _suiteCoreManager.GetCertEvents())
            {
                string? valResult = cert.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = cert
                    });
                }
            }

            // Validate DriverEvents
            foreach (Driver driver in _suiteCoreManager.GetDriverEvents())
            {
                string? valResult = driver.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = driver
                    });
                }
            }

            // Validate ProcessClosureEvents
            foreach (ProcessClosure proc in _suiteCoreManager.GetProcessClosureEvents())
            {
                string? valResult = proc.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = proc
                    });
                }
            }

            // Validate EnvironmentEvents
            foreach (Environment env in _suiteCoreManager.GetEnvironmentEvents())
            {
                string? valResult = env.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = env
                    });
                }
            }

            // Validate ExecutableEvents
            foreach (Executable exe in _suiteCoreManager.GetExecutableEvents())
            {
                string? valResult = exe.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = exe
                    });
                }
            }

            // Validate BrowserExtensions
            foreach (BrowserExt ext in _suiteCoreManager.GetExtensionEvents())
            {
                string? valResult = ext.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = ext
                    });
                }
            }

            // Validate FileEvents
            foreach (FileSysIO file in _suiteCoreManager.GetFileEvents())
            {
                string? valResult = file.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = file
                    });
                }
            }

            // Validate PowerShellEvents
            foreach (PowerShell ps in _suiteCoreManager.GetPowerShellEvents())
            {
                string? valResult = ps.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = ps
                    });
                }
            }

            // Validate RegistryEvents
            foreach (Registry reg in _suiteCoreManager.GetRegistryEvents())
            {
                string? valResult = reg.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = reg
                    });
                }
            }

            // Validate ServiceClosureEvents
            foreach (ServiceClosure svc in _suiteCoreManager.GetServiceClosureEvents())
            {
                string? valResult = svc.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = svc
                    });
                }
            }

            // Validate ShortcutEvents
            foreach (Shortcut shortcut in _suiteCoreManager.GetShortcutEvents())
            {
                string? valResult = shortcut.Validate();
                if (!string.IsNullOrWhiteSpace(valResult))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = valResult,
                        ObjectForError = shortcut
                    });
                }
            }

            // Validate BuildSettings
            Build? buildSettings = _suiteCoreManager.GetBuildSettings();
            if (buildSettings != null)
            {
                // Validate required fields
                if (string.IsNullOrWhiteSpace(buildSettings.Name))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = "Build settings must have a name",
                        ObjectForError = buildSettings
                    });
                }

                if (string.IsNullOrWhiteSpace(buildSettings.Manufacturer))
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = "Build settings must have a manufacturer",
                        ObjectForError = buildSettings
                    });
                }

                if (buildSettings.SuiteVersion == null)
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = "Build settings must have a version",
                        ObjectForError = buildSettings
                    });
                }

                if (buildSettings.UpgradeCode == Guid.Empty)
                {
                    validationErrors.Add(new SuiteValidationError
                    {
                        Message = "Build settings must have a valid upgrade code",
                        ObjectForError = buildSettings
                    });
                }
            }
            else
            {
                validationErrors.Add(new SuiteValidationError
                {
                    Message = "Build settings are missing",
                    ObjectForError = new Build()
                });
            }
            if (validationErrors.Count > 0)
            {
                _validationReport = validationErrors;
                return validationErrors;
            }
            else
            {
                _validationReport = null;
                return null;
            }
        }

        private static void CopyFileSystemNodes(IEnumerable<FileSystemNode> nodes, string destinationRoot)
        {
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                CopyFileSystemNodeRecursive(node, destinationRoot);
            }
        }

        private static void CopyFileSystemNodeRecursive(FileSystemNode node, string destinationRoot)
        {
            if (string.IsNullOrWhiteSpace(node.FullPath)) return;
            string relativePath = GetRelativePath(node.FullPath);
            string targetPath = Path.Combine(destinationRoot, relativePath);
            if (node.IsFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(node.FullPath, targetPath, true);
            }
            else
            {
                Directory.CreateDirectory(targetPath);
                if (node.SubNodes != null)
                {
                    foreach (var sub in node.SubNodes)
                    {
                        CopyFileSystemNodeRecursive(sub, destinationRoot);
                    }
                }
            }
        }

        private static string GetRelativePath(string fullPath)
        {
            return Path.GetFileName(fullPath);
        }

        private PopConfigDto CreatePopConfig(Popup popupSettings, SuiteExecConfig suiteExecCfg)
        {
            var config = new PopConfigDto
            {
                LogFilePath = Path.Combine(_appSettingsControl.GetLogLocation(), $"{suiteExecCfg.BuildSettings.Manufacturer}_{suiteExecCfg.BuildSettings.Name}_{suiteExecCfg.BuildSettings.SuiteVersion}_{suiteExecCfg.BuildSettings.Revision}.log"),
                CompanyLogoBackground = popupSettings.BackgroundColor?.ToString(),
                MainText = "The following apps will be closed before the upgrade. Please save any work required before you continue.",
                IsClosuresAppsVisible = popupSettings.LinkToProcClosures,
                ClosureApps = popupSettings.LinkToProcClosures
                    ? suiteExecCfg.ProcClosureEvents.SelectMany(p => p.GetNames().DefaultIfEmpty(p.Id.ToString())).ToList()
                    : new List<string>(),
                Timer = popupSettings.Timer,
                MaxDelayDays = popupSettings.DelayDays,
                Action = popupSettings.InstAction,
                TimerExpireAction = popupSettings.TimerExpireAction,
                Manufacturer = suiteExecCfg.BuildSettings.Manufacturer,
                ProductName = suiteExecCfg.BuildSettings.Name,
                ProductVersion = suiteExecCfg.BuildSettings.SuiteVersion?.ToString(),
                ActionIconKind = popupSettings.DeployIconKind
            };

            return config;
        }

        private static void SaveAvaloniaAssetToFile(Uri assetUri, string destinationPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            var assets = AssetLoader.Open(assetUri);
            using var output = File.Create(destinationPath);
            assets.CopyTo(output);
        }

        private static bool TryCreateSelfExtractingBootstrapper(string suiteZipPath, string outputExePath)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;
            if (!File.Exists(suiteZipPath))
                return false;

            try
            {
                // Prebuilt stub must be shipped with the SuiteCreatorAvalonia app.
                // Expected location: {AppContext.BaseDirectory}\SuiteExec\SuiteSfxStub.exe
                var stubExe = Path.Combine(AppContext.BaseDirectory, "SuiteExec", "SuiteSfxStub.exe");
                if (!File.Exists(stubExe))
                {
                    AppLog.Warning($"SuiteSfxStub.exe not found at: {stubExe}, suite exe will not be created", "SuiteBuilder");
                    return false;
                }

                CreateSfxFromStubAndZip(stubExe, suiteZipPath, outputExePath);
                return File.Exists(outputExePath);
            }
            catch (Exception ex)
            {
                // Best-effort only.
                AppLog.Warning($"Failed to create self-extracting bootstrapper: {outputExePath}, error: {ex.Message}", "SuiteBuilder");
                return false;
            }
        }

        // Payload format (appended to the end of the exe):
        // [zipBytes...][int32 zipLength][int32 magic 'SUFX']
        private static void CreateSfxFromStubAndZip(string stubExePath, string suiteZipPath, string outputExePath)
        {
            const int magic = unchecked((int)0x53554658); // 'SUFX'
            var zipBytes = File.ReadAllBytes(suiteZipPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputExePath)!);
            using var outFs = new FileStream(outputExePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using (var stubFs = new FileStream(stubExePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                stubFs.CopyTo(outFs);
            }

            using (var bw = new BinaryWriter(outFs, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(zipBytes);
                bw.Write(zipBytes.Length);
                bw.Write(magic);
            }
        }

        // This should match with the PopConfig class from the SuiteUserPopup project, as it will be deserialized by the popup executable.
        // These are separated out so that we dont need to add project references, as the SuiteUserPopup project needs to be as small as possible.
        // Might create a separate project for shared models if we find more models that need to be shared between the two projects.
        private sealed class PopConfigDto
        {
            public string? LogFilePath { get; set; }
            public string? CompanyLogoBackground { get; set; }
            public string? Manufacturer { get; set; }
            public string? ProductName { get; set; }
            public string? ProductVersion { get; set; }
            public string? MainText { get; set; }
            public bool? IsClosuresAppsVisible { get; set; }
            public List<string> ClosureApps { get; set; } = new List<string>();
            public int? Timer { get; set; }
            public int? MaxDelayDays { get; set; }
            public string? Action { get; set; }
            public MaterialIconKind? ActionIconKind { get; set; }
            public PopupAction? TimerExpireAction { get; set; }
        }
    }
}
