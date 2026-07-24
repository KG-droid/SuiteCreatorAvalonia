using Logger;
using SuiteTools;

namespace SuiteOperations.Package
{
    public class MSIxExec : SuiteCreatorAvalonia.Models.Package.MSIx
    {
        private Log _log;
        public string? PackageFamilyName { get; set; }
        public string? PackageFullName { get; set; }

        public MSIxExec(Log log)
        {
            _log = log;
        }

        public MSIxExec() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteInstall()
        {
            Validate();
            _log.WriteLog($"Running MSIx package install for: {MSIxFile}");
            _log.WriteLog($"Install options: ForceAppShutdown - {IsForceCloseFamily}, ForceUpdateFromAnyVersion - {IsForceThisVersion}, DeferRegistrationWhenPackagesAreInUse - {IsDeferInUse}");
            SuiteTools.MSIxInstallOptions? options = null;
            if (IsForceCloseFamily || IsForceThisVersion || IsDeferInUse)
            {
                options = new SuiteTools.MSIxInstallOptions
                {
                    ForceAppShutdown = IsForceCloseFamily,
                    ForceUpdateFromAnyVersion = IsForceThisVersion,
                    DeferRegistrationWhenPackagesAreInUse = IsDeferInUse
                };
            }
            MSIxTools.InstallMSIxPackageAsync(
                MSIxFile,
                options,
                Dependents != null && Dependents.Count > 0 ? Dependents : null
            ).GetAwaiter().GetResult();
            _log.WriteLog($"Install complete");
        }

        public void ExecuteRollback()
        {
            if (Rollback == null)
            {
                _log.WriteLog($"No rollback package configured for: {MSIxFile}");
                return;
            }
            _log.WriteLog($"Running MSIx rollback install for: {Rollback.MSIxFile}");
            MSIxExec rollbackExec = new(_log)
            {
                MSIxFile = Rollback.MSIxFile,
                IsForceCloseFamily = Rollback.IsForceCloseFamily,
                IsForceThisVersion = Rollback.IsForceThisVersion,
                IsDeferInUse = Rollback.IsDeferInUse,
                HasDependents = Rollback.HasDependents,
                Dependents = Rollback.Dependents
            };
            rollbackExec.ExecuteInstall();
            _log.WriteLog($"Rollback complete");
        }

        public void ExecuteUninstall()
        {
            _log.WriteLog($"Running MSIx package uninstall for: {PackageFamilyName}");
            MSIxTools.UninstallMSIxPackageAsync(PackageFamilyName, false).GetAwaiter().GetResult();
            _log.WriteLog($"Uninstall complete");
        }

        public new void Validate()
        {
            if (string.IsNullOrEmpty(MSIxFile))
            {
                throw new ArgumentNullException("MSIxFile must be set before executing.", nameof(MSIxFile));
            }
            if (!File.Exists(MSIxFile))
            {
                throw new FileNotFoundException("The specified MSIx file does not exist.", MSIxFile);
            }
        }

        public PackageExecDetectionResult IsDetected()
        {
            PackageExecDetectionResult result = new();
            if (IsForceThisVersion)
            {
                result.Result = MSIxTools.IsMSIxReadyForAllUsers(PackageFullName);
                result.Summary = $"Checking for specific package full name: {PackageFullName}";
            }
            else 
            {
                result.Result = MSIxTools.IsMSIxReadyForAllUsers(PackageFamilyName);
                result.Summary = $"Checking for packages in the family name: {PackageFamilyName}";
            }
            return result;
        }
    }
}
