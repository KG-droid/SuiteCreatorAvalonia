using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteOperations.Events;
using SuiteOperations.Package;
using Windows.Storage;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private void ResolveExtractedPaths()
        {
            _log.WriteLog("Resolving extracted file paths against suite root directory", "PathResolution", Log.Severity.Info);
            ResolvePackagePaths();
            ResolveEventPaths();
        }

        private void ResolvePackagePaths()
        {
            foreach (PackageBase package in _suiteConfig.Packages)
            {
                string packageDir = GetResolvedPackageDirectory(package.Id);

                switch (package)
                {
                    case MSIExec msi:
                        if (_action != SuiteAction.Removal)
                        {
                            ResolveMSIExecPaths(msi, packageDir);
                        }
                        break;
                    case MSIxExec msix:
                        if (_action != SuiteAction.Removal)
                        {
                            ResolveMSIxExecPaths(msix, packageDir);
                        }
                        break;
                    case OtherExec other:
                        other.SourceDir = packageDir;
                        _log.WriteLog($"Set OtherExec SourceDir for '{other.Name}': {packageDir}", "PathResolution", Log.Severity.Info);
                        ResolveRelativeFileVarsInCommand(other.InstallCommand, packageDir);
                        ResolveRelativeFileVarsInCommand(other.RollbackCommand, packageDir);
                        // Removal.RemoveCommands must stay relative through a Deployment run: CreateUninstallMedia()
                        // later clones _suiteConfig as-is to persist a standalone uninstall SuiteConfig.scfg, and
                        // GetUninstallRelativeFilePaths()/CopyPackageFileForUninstall() need the raw relative paths
                        // to know which files to bundle. OtherExec.ExecuteUninstall() isn't invoked during
                        // Deployment anyway (only during a later Removal run, or via Rollback), so resolving here
                        // would only corrupt that snapshot without being needed for this run's own execution.
                        if (_action != SuiteAction.Deployment)
                        {
                            ResolveRemovalRelativeFileVars(other.Removal, packageDir);
                        }
                        break;
                    case OtherRemovalExec otherRem:
                        otherRem.SourceDir = packageDir;
                        _log.WriteLog($"Set OtherRemovalExec SourceDir for '{otherRem.Name}': {packageDir}", "PathResolution", Log.Severity.Info);
                        ResolveRemovalRelativeFileVars(otherRem.Removal, packageDir);
                        break;
                    case MSIRemovalExec:
                    case MSIxRemovalExec:
                        // No file paths to resolve — these use removal codes
                        break;
                }
            }
        }

        private void ResolveRemovalRelativeFileVars(OtherRemovalObject? removal, string packageDir)
        {
            if (removal is OtherCMDRemoval cmdRemoval && cmdRemoval.RemoveCommands != null)
            {
                foreach (List<VariableText> commandList in cmdRemoval.RemoveCommands)
                {
                    ResolveRelativeFileVarsInCommand(commandList, packageDir);
                }
            }
        }

        private void ResolveRelativeFileVarsInCommand(List<VariableText>? command, string packageDir)
        {
            if (command == null) return;
            foreach (RelativeFileVar fileVar in command.OfType<RelativeFileVar>())
            {
                if (!string.IsNullOrEmpty(fileVar.RelativePath))
                {
                    fileVar.RelativePath = Path.Combine(packageDir, fileVar.RelativePath);
                    _log.WriteLog($"Resolved OtherPkg command file: {fileVar.RelativePath}", "PathResolution", Log.Severity.Info);
                }
            }
        }

        private string GetResolvedPackageDirectory(Guid packageId)
        {
            string singularPackageDir = Path.Combine(_suiteRootDir, "Package", packageId.ToString());
            if (Directory.Exists(singularPackageDir))
            {
                return singularPackageDir;
            }

            string pluralPackageDir = Path.Combine(_suiteRootDir, "Packages", packageId.ToString());
            if (Directory.Exists(pluralPackageDir))
            {
                return pluralPackageDir;
            }

            return singularPackageDir;
        }

        private void ResolveMSIExecPaths(MSIExec msi, string packageDir)
        {
            if (!Directory.Exists(packageDir))
            {
                _log.WriteLog($"Package directory not found for MSI '{msi.Name}': {packageDir}", "PathResolution", Log.Severity.Warning);
                return;
            }

            string[] msiFiles = Directory.GetFiles(packageDir, "*.msi");
            if (msiFiles.Length > 0)
            {
                msi.MSIFile = msiFiles[0];
                _log.WriteLog($"Resolved MSI file for '{msi.Name}': {msi.MSIFile}", "PathResolution", Log.Severity.Info);
            }

            string[] mstFiles = Directory.GetFiles(packageDir, "*.mst");
            if (mstFiles.Length > 0)
            {
                msi.TransformsFile = mstFiles[0];
                _log.WriteLog($"Resolved MST file for '{msi.Name}': {msi.TransformsFile}", "PathResolution", Log.Severity.Info);
            }

            string[] mspFiles = Directory.GetFiles(packageDir, "*.msp");
            if (mspFiles.Length > 0)
            {
                msi.PatchFile = mspFiles[0];
                _log.WriteLog($"Resolved MSP file for '{msi.Name}': {msi.PatchFile}", "PathResolution", Log.Severity.Info);
            }
        }

        private void ResolveMSIxExecPaths(MSIxExec msix, string packageDir)
        {
            if (!Directory.Exists(packageDir))
            {
                _log.WriteLog($"Package directory not found for MSIx '{msix.Name}': {packageDir}", "PathResolution", Log.Severity.Warning);
                return;
            }

            string[] msixFiles = Directory.GetFiles(packageDir, "*.msix")
                .Concat(Directory.GetFiles(packageDir, "*.msixbundle"))
                .Concat(Directory.GetFiles(packageDir, "*.appx"))
                .Concat(Directory.GetFiles(packageDir, "*.appxbundle"))
                .ToArray();

            if (msixFiles.Length > 0)
            {
                msix.MSIxFile = msixFiles[0];
                _log.WriteLog($"Resolved MSIx file for '{msix.Name}': {msix.MSIxFile}", "PathResolution", Log.Severity.Info);
            }
        }

        private void ResolveEventPaths()
        {
            // Certificate events
            if (_suiteConfig.CertEvents != null && _action != SuiteAction.Removal)
            {
                foreach (CertExecEvent cert in _suiteConfig.CertEvents)
                {
                    if (cert.Action == CertAction.Add)
                    {
                        string certDir = Path.Combine(_suiteRootDir, "Certificate", cert.Id.ToString());
                        string certFile = FindFirstFileInDirectory(certDir);
                        cert.FilePath = certFile;
                        _log.WriteLog($"Resolved certificate file: {certFile}", "PathResolution", Log.Severity.Info);
                    }
                }
            }

            // Driver events (the whole driver package folder is bundled, so pick the .inf out of it)
            if (_suiteConfig.DriverEvents != null && _action != SuiteAction.Removal)
            {
                foreach (DriverExecEvent driver in _suiteConfig.DriverEvents)
                {
                    if (driver.Action == DriverAction.Install)
                    {
                        string driverDir = Path.Combine(_suiteRootDir, "Driver", driver.Id.ToString());
                        if (!Directory.Exists(driverDir))
                        {
                            throw new DirectoryNotFoundException($"Could not find driver directory: {driverDir}");
                        }
                        string[] infFiles = Directory.GetFiles(driverDir, "*.inf", SearchOption.AllDirectories);
                        string? infFile = infFiles.FirstOrDefault(f =>
                            !string.IsNullOrWhiteSpace(driver.InfName) &&
                            Path.GetFileName(f).Equals(driver.InfName, StringComparison.OrdinalIgnoreCase))
                            ?? infFiles.FirstOrDefault();
                        if (infFile == null)
                        {
                            throw new FileNotFoundException($"Could not find an .inf file within directory: {driverDir}");
                        }
                        driver.InfPath = infFile;
                        _log.WriteLog($"Resolved driver .inf file: {infFile}", "PathResolution", Log.Severity.Info);
                    }
                }
            }

            // Registry events. A non-permanent Import event gets reversed into a value removal on a Removal
            // run (see Registry.Reverse()/RegExecEvent.RemoveValuesFromImportedRegFile), which still needs
            // the original .reg file's contents - so resolve it then too, from the uninstall media's own
            // Registry/{id} folder (CopyUninstallRegistryFiles stages it there for exactly this). A permanent
            // Import never gets reversed and its file was never copied to uninstall media, so skip it.
            if (_suiteConfig.RegistryEvents != null)
            {
                foreach (RegExecEvent reg in _suiteConfig.RegistryEvents)
                {
                    if (reg.Action != RegAction.Import) continue;
                    if (_action == SuiteAction.Removal && reg.IsPermanent) continue;

                    string regDir = Path.Combine(_suiteRootDir, "Registry", reg.Id.ToString());
                    string regFile = FindFirstFileInDirectory(regDir);
                    reg.RegFilePath = new Uri(regFile);
                    _log.WriteLog($"Resolved registry file: {regFile}", "PathResolution", Log.Severity.Info);
                }
            }

            // PowerShell events
            if (_suiteConfig.PowerShellEvents != null)
            {
                foreach (PowerShellExecEvent ps in _suiteConfig.PowerShellEvents)
                {
                    string psDir = Path.Combine(_suiteRootDir, "PowerShell", ps.Id.ToString());

                    // A linked (non-embedded) script was copied directly into psDir at build time. An
                    // embedded script (typed into the editor) has no file here — only its own Support
                    // subfolder, if any — and travels with the config as ScriptDoc instead, so there is
                    // nothing to resolve on disk for it.
                    if (Directory.Exists(psDir) && Directory.GetFiles(psDir).Length > 0)
                    {
                        string psFile = FindFirstFileInDirectory(psDir);
                        ps.ScriptPath = psFile;
                        _log.WriteLog($"Resolved PowerShell script for '{ps.ScriptName}': {psFile}", "PathResolution", Log.Severity.Info);
                    }
                    else
                    {
                        _log.WriteLog($"No linked script file found for '{ps.ScriptName}'; using embedded script text.", "PathResolution", Log.Severity.Info);
                    }

                    string supportDir = Path.Combine(psDir, "Support");
                    if (Directory.Exists(supportDir) && Directory.GetFileSystemEntries(supportDir).Length > 0)
                    {
                        ps.SupportFilesDir = supportDir;
                        _log.WriteLog($"Resolved PowerShell support files directory for '{ps.ScriptName}': {supportDir}", "PathResolution", Log.Severity.Info);
                    }
                }
            }

            // File events (Deploy action only — source files are extracted to File/{id}/)
            if (_suiteConfig.FileEvents != null && _action != SuiteAction.Removal)
            {
                foreach (FileExecEvent file in _suiteConfig.FileEvents)
                {
                    if (file.Action == FileSysIOAction.Deploy)
                    {
                        string? path;
                        if (file.FileSysIOType == FileSysIOType.File)
                        {
                            string fileDir = Path.Combine(_suiteRootDir, "File", file.Id.ToString());
                            path = FindFirstFileInDirectory(fileDir);
                            file.SourcePath = new List<VariableText> { new LiteralText(path) };
                        }
                        else
                        {
                            path = Path.Combine(_suiteRootDir, "File", file.Id.ToString());
                            FindFirstFileInDirectory(path); // Just to verify the directory exists and has files, since for directories we need the path not a specific file
                            file.SourcePath = new List<VariableText> { new LiteralText(path) };
                        }
                        _log.WriteLog($"Resolved deploy source directory: {path}", "PathResolution", Log.Severity.Info);
                    }
                }
            }

            // Executable events
            if (_suiteConfig.ExecutableEvents != null)
            {
                foreach (ExecutableExecEvent exe in _suiteConfig.ExecutableEvents)
                {
                    string exeDir = Path.Combine(_suiteRootDir, "Executable", exe.Id.ToString());
                    if (Directory.Exists(exeDir))
                    {
                        IEnumerable<RelativeFileVar>? fileVars = exe.Command?.OfType<RelativeFileVar>();
                        if (fileVars != null && fileVars.Count() > 0)
                        {
                            foreach (RelativeFileVar fileVar in fileVars)
                            {
                                if (!string.IsNullOrEmpty(fileVar.RelativePath))
                                {
                                    fileVar.RelativePath = Path.Combine(exeDir, fileVar.RelativePath);
                                    _log.WriteLog($"Resolved executable file: {fileVar.RelativePath}", "PathResolution", Log.Severity.Info);
                                }   
                            }
                        }
                        
                    }
                }
            }

            // Browser extension events (local install only)
            if (_suiteConfig.ExtensionEvents != null && _action != SuiteAction.Removal)
            {
                foreach (BrowserExExecEvent ext in _suiteConfig.ExtensionEvents)
                {
                    if (ext.Action == ExtAction.Install && ext.Source == BrowserExtensionSource.Local)
                    {
                        string extDir = Path.Combine(_suiteRootDir, "BrowserExt", ext.Id.ToString());
                        if (Directory.Exists(extDir))
                        {
                            ext.ExtPath = extDir;
                            _log.WriteLog($"Resolved browser extension path: {extDir}", "PathResolution", Log.Severity.Info);
                        }
                    }
                }
            }
        }

        private string FindFirstFileInDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                string[] files = Directory.GetFiles(directory);
                if (files.Length > 0)
                {
                    return files[0];
                }
            }
            throw new FileNotFoundException($"Could not find any files within directory: {directory}");
        }
    }
}
