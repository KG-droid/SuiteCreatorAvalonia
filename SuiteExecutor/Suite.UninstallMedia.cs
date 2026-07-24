using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using Logger;
using SuiteOperations;
using SuiteOperations.Events;
using SuiteOperations.Package;
using SuiteCreatorAvalonia.Models.Events;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private static string _uninstallMediaPath;

        private void CreateUninstallMedia()
        {
            _uninstallMediaPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "SuiteExecutor", "UninstallMedia", _suiteConfig.BuildSettings.UpgradeCode.ToString());
            _log.WriteLog($"Creating uninstall media at: {_uninstallMediaPath}", "Execution", Log.Severity.Info);

            Directory.CreateDirectory(_uninstallMediaPath);
            CopyUninstallPackageFiles();
            CopyUninstallRegistryFiles();
            CopyPopupFiles();

            SuiteExecConfig uninstallSuiteConfig = CreateUninstallSuiteConfig();
            string uninstallSuiteConfigPath = Path.Combine(_uninstallMediaPath, "SuiteConfig.scfg");
            uninstallSuiteConfig.ToJson(uninstallSuiteConfigPath);

            _log.WriteLog($"Created uninstall suite config at: {uninstallSuiteConfigPath}", "Execution", Log.Severity.Info);
        }

        private void CopyUninstallPackageFiles()
        {
            string uninstallPackagesPath = Path.Combine(_uninstallMediaPath, "Packages");

            foreach (PackageBase package in _suiteConfig.Packages)
            {
                if (package is not OtherExec otherPackage)
                {
                    continue;
                }

                List<string> relativeFilePaths = GetUninstallRelativeFilePaths(otherPackage);
                if (relativeFilePaths.Count == 0)
                {
                    continue;
                }

                string sourcePackagePath = GetExistingPackageDirectory(package.Id);
                if (string.IsNullOrWhiteSpace(sourcePackagePath))
                {
                    _log.WriteLog($"Skipping uninstall file copy for package '{package.Name}' because package source folder was not found.", "Execution", Log.Severity.Warning);
                    continue;
                }

                string destinationPackagePath = Path.Combine(uninstallPackagesPath, package.Id.ToString());
                Directory.CreateDirectory(destinationPackagePath);

                foreach (string relativeFilePath in relativeFilePaths)
                {
                    CopyPackageFileForUninstall(sourcePackagePath, destinationPackagePath, package, relativeFilePath);
                }
            }
        }

        // A non-permanent Registry Import event gets reversed into a value removal on the later Removal run
        // (see Registry.Reverse()), which needs the original .reg file's contents to know what to undo. That
        // run treats this uninstall media folder as its own _suiteRootDir, so stage the file at the same
        // Registry/{id} layout ResolveEventPaths() already resolves Import files from during a normal run.
        private void CopyUninstallRegistryFiles()
        {
            if (_suiteConfig.RegistryEvents == null) return;

            string uninstallRegistryPath = Path.Combine(_uninstallMediaPath, "Registry");
            foreach (RegExecEvent reg in _suiteConfig.RegistryEvents)
            {
                if (reg.Action != RegAction.Import || reg.IsPermanent) continue;

                if (reg.RegFilePath == null || !reg.RegFilePath.IsFile || !File.Exists(reg.RegFilePath.LocalPath))
                {
                    _log.WriteLog($"Skipping uninstall registry file copy for event '{reg.Id}' because the source .reg file was not found.", "Execution", Log.Severity.Warning);
                    continue;
                }

                string destinationDir = Path.Combine(uninstallRegistryPath, reg.Id.ToString());
                Directory.CreateDirectory(destinationDir);
                string destinationFile = Path.Combine(destinationDir, Path.GetFileName(reg.RegFilePath.LocalPath));
                File.Copy(reg.RegFilePath.LocalPath, destinationFile, true);
                _log.WriteLog($"Copied uninstall registry file for event '{reg.Id}': {destinationFile}", "Execution", Log.Severity.Info);
            }
        }

        private void CopyPopupFiles()
        {
            // A blocked exe launch attempt shows a notice via SuiteUserPopup.exe too (see
            // ProcClosureExecEvent.BuildDebuggerValue), which reads its branding from this suite's own
            // Popup cache folder (_suiteRootDir/Popup) — same as the normal warning popup. A later
            // Removal run treats this uninstall media folder as its own _suiteRootDir, so that notice
            // needs the Popup files copied in here too, even when the full warning/progress popup isn't
            // enabled for this suite.
            if (!_suiteConfig.PopupSettings.ShowPopupWarning && !_suiteConfig.PopupSettings.ShowProgress && !HasBlockingProcClosures) return;
            string popSourceDir = Path.Combine(_suiteRootDir, "Popup");
            if (!Directory.Exists(popSourceDir))
            {
                _log.WriteLog($"Popup source directory not found, skipping popup file copy for uninstall media: {popSourceDir}", "Execution", Log.Severity.Warning);
                return;
            }
            string popDestDir = Path.Combine(_uninstallMediaPath, "Popup");
            Directory.CreateDirectory(popDestDir);
            foreach (string file in Directory.GetFiles(popSourceDir, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Preserve any subfolder structure rather than flattening (which could collide on filename).
                string relativePath = Path.GetRelativePath(popSourceDir, file);
                string destFile = Path.Combine(popDestDir, relativePath);
                string? destSubDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destSubDir))
                {
                    Directory.CreateDirectory(destSubDir);
                }
                File.Copy(file, destFile, true);
            }
        }

        private SuiteExecConfig CreateUninstallSuiteConfig()
        {
            SuiteExecConfig uninstallSuiteConfig = _suiteConfig.Clone();
            HashSet<Guid> removedPackageIds = uninstallSuiteConfig.Packages
                .Where(IsRemovalPackage)
                .Select(package => package.Id)
                .ToHashSet();

            uninstallSuiteConfig.Packages = uninstallSuiteConfig.Packages
                .Where(package => !IsRemovalPackage(package))
                .ToList();

            if (uninstallSuiteConfig.Stages != null)
            {
                uninstallSuiteConfig.Stages = uninstallSuiteConfig.Stages
                    .Where(stage => !removedPackageIds.Contains(stage.Id))
                    .ToList();
            }

            HashSet<Guid> validStageIds = GetValidUninstallStageIds(uninstallSuiteConfig);
            int removedScheduleCount = 0;
            int removedEventCount = 0;

            uninstallSuiteConfig.CertEvents = FilterEventsForUninstall(uninstallSuiteConfig.CertEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.DriverEvents = FilterEventsForUninstall(uninstallSuiteConfig.DriverEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.ProcClosureEvents = FilterEventsForUninstall(uninstallSuiteConfig.ProcClosureEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.EnvironmentEvents = FilterEventsForUninstall(uninstallSuiteConfig.EnvironmentEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.ExecutableEvents = FilterEventsForUninstall(uninstallSuiteConfig.ExecutableEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.ExtensionEvents = FilterEventsForUninstall(uninstallSuiteConfig.ExtensionEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.FileEvents = FilterEventsForUninstall(uninstallSuiteConfig.FileEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.PowerShellEvents = FilterEventsForUninstall(uninstallSuiteConfig.PowerShellEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.RegistryEvents = FilterEventsForUninstall(uninstallSuiteConfig.RegistryEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.ServiceClosureEvents = FilterEventsForUninstall(uninstallSuiteConfig.ServiceClosureEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);
            uninstallSuiteConfig.ShortcutEvents = FilterEventsForUninstall(uninstallSuiteConfig.ShortcutEvents, validStageIds, ref removedScheduleCount, ref removedEventCount);

            _log.WriteLog($"Prepared uninstall suite config. Removed {removedPackageIds.Count} removal package(s), {removedScheduleCount} unapplicable event schedules, and {removedEventCount} unapplicatable events.", "Execution", Log.Severity.Info);

            return uninstallSuiteConfig;
        }

        // Removal can be launched two ways: via the UninstallString (Add/Remove Programs), where --Config
        // points at the uninstall media's own SuiteConfig.scfg so _suiteRootDir *is* the media folder; or by
        // running "remove" directly against the original suite package, where _suiteRootDir is wherever that
        // package lives instead. Either way the stale media folder for this UpgradeCode needs to go, so
        // recompute its path from UpgradeCode rather than assuming it's _suiteRootDir.
        private void RemoveUninstallMedia()
        {
            string uninstallMediaPath = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "SuiteExecutor", "UninstallMedia", _suiteConfig.BuildSettings.UpgradeCode.ToString());

            if (!Directory.Exists(uninstallMediaPath))
            {
                _log.WriteLog($"No uninstall media found at: {uninstallMediaPath}", "Execution", Log.Severity.Info);
                return;
            }

            try
            {
                // If this run's suite root is the media folder itself (the Add/Remove Programs case), the
                // constructor pinned the process's current directory there so relative package paths resolve
                // correctly — move off it first, or deleting the folder fails while it's the CWD.
                bool suiteRootIsMediaFolder = string.Equals(Path.GetFullPath(_suiteRootDir).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(uninstallMediaPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
                if (suiteRootIsMediaFolder)
                {
                    Directory.SetCurrentDirectory(Path.GetTempPath());
                }

                Directory.Delete(uninstallMediaPath, recursive: true);
                _log.WriteLog($"Removed uninstall media at: {uninstallMediaPath}", "Execution", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to remove uninstall media at '{uninstallMediaPath}': {ex.Message}", "Execution", Log.Severity.Warning);
            }
        }

        private static bool IsRemovalPackage(PackageBase package)
        {
            return package is MSIRemovalExec or MSIxRemovalExec or OtherRemovalExec;
        }

        private static List<string> GetUninstallRelativeFilePaths(OtherExec otherPackage)
        {
            List<string> relativeFilePaths = new List<string>();

            if (otherPackage.RemovalType == OtherRemovalType.PowerShell)
            {
                if (!string.IsNullOrWhiteSpace(otherPackage.RemovePowerShellScriptPath) &&
                    !Path.IsPathFullyQualified(otherPackage.RemovePowerShellScriptPath))
                {
                    relativeFilePaths.Add(otherPackage.RemovePowerShellScriptPath);
                }
                return relativeFilePaths;
            }

            if (otherPackage.Removal is not OtherCMDRemoval cmdRemoval || cmdRemoval.RemoveCommands == null)
            {
                return relativeFilePaths;
            }

            foreach (List<VariableText> command in cmdRemoval.RemoveCommands)
            {
                foreach (VariableText part in command)
                {
                    if (part is RelativeFileVar fileVar && !string.IsNullOrWhiteSpace(fileVar.RelativePath))
                    {
                        if (!relativeFilePaths.Contains(fileVar.RelativePath, StringComparer.OrdinalIgnoreCase))
                        {
                            relativeFilePaths.Add(fileVar.RelativePath);
                        }
                    }
                }
            }

            return relativeFilePaths;
        }

        private string GetExistingPackageDirectory(Guid packageId)
        {
            string singularPackagePath = Path.Combine(_suiteRootDir, "Package", packageId.ToString());
            if (Directory.Exists(singularPackagePath))
            {
                return singularPackagePath;
            }

            string pluralPackagePath = Path.Combine(_suiteRootDir, "Packages", packageId.ToString());
            if (Directory.Exists(pluralPackagePath))
            {
                return pluralPackagePath;
            }

            return string.Empty;
        }

        private void CopyPackageFileForUninstall(string sourcePackagePath, string destinationPackagePath, PackageBase package, string relativeFilePath)
        {
            string sourceFilePath = Path.GetFullPath(Path.Combine(sourcePackagePath, relativeFilePath));
            string sourcePackageRootPath = Path.GetFullPath(sourcePackagePath + Path.DirectorySeparatorChar);
            if (!sourceFilePath.StartsWith(sourcePackageRootPath, StringComparison.OrdinalIgnoreCase))
            {
                _log.WriteLog($"Skipping package file '{relativeFilePath}' for '{package.Name}' because it resolves outside the package folder.", "Execution", Log.Severity.Warning);
                return;
            }

            if (!File.Exists(sourceFilePath))
            {
                _log.WriteLog($"Skipping package file '{relativeFilePath}' for '{package.Name}' because it was not found at '{sourceFilePath}'.", "Execution", Log.Severity.Warning);
                return;
            }

            string destinationFilePath = Path.Combine(destinationPackagePath, relativeFilePath);
            string? destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(destinationDirectoryPath))
            {
                Directory.CreateDirectory(destinationDirectoryPath);
            }

            File.Copy(sourceFilePath, destinationFilePath, true);
            _log.WriteLog($"Copied uninstall package file for '{package.Name}': {relativeFilePath}", "Execution", Log.Severity.Info);
        }

        private static HashSet<Guid> GetValidUninstallStageIds(SuiteExecConfig suiteConfig)
        {
            if (suiteConfig.Stages != null && suiteConfig.Stages.Count > 0)
            {
                return suiteConfig.Stages
                    .Select(stage => stage.Id)
                    .ToHashSet();
            }

            return suiteConfig.Packages
                .Select(package => package.Id)
                .ToHashSet();
        }

        private static List<TEvent> FilterEventsForUninstall<TEvent>(List<TEvent>? events, HashSet<Guid> validStageIds, ref int removedScheduleCount, ref int removedEventCount)
            where TEvent : EventCore
        {
            if (events == null)
            {
                return new List<TEvent>();
            }

            List<TEvent> filteredEvents = new List<TEvent>();

            foreach (TEvent evt in events)
            {
                int originalScheduleCount = evt.Schedules.Count;
                evt.Schedules = evt.Schedules
                    .Where(schedule => ShouldKeepScheduleForUninstall(evt, schedule, validStageIds))
                    .ToList();

                removedScheduleCount += originalScheduleCount - evt.Schedules.Count;

                if (evt.Schedules.Count > 0)
                {
                    filteredEvents.Add(evt);
                }
                else
                {
                    removedEventCount++;
                }
            }

            return filteredEvents;
        }

        private static bool ShouldKeepScheduleForUninstall(EventCore evt, Schedule schedule, HashSet<Guid> validStageIds)
        {
            if (schedule.EventStageId == null || !validStageIds.Contains(schedule.EventStageId.Value))
            {
                return false;
            }

            if (evt is EventCoreWithPermanence eventCoreWithPermanence && !eventCoreWithPermanence.IsPermanent)
            {
                return true;
            }

            return schedule.StageSequence != Sequence.DuringInstallBeforeStage &&
                   schedule.StageSequence != Sequence.DuringInstallAfterStage;
        }
    }
}
