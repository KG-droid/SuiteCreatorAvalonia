using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Package;
using SuiteOperations.Package;
using System.Security.AccessControl;
using System.Security.Principal;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        internal void SetUserPackagePermissions()
        {
            foreach (PackageBase package in _suiteConfig.Packages)
            {
                string? sourcePath = null;
                if (package is OtherExec otherExec && otherExec.Context == Contexts.User)
                {
                    _log.WriteLog($"Package: {package.Name}, is a user context package, setting user read/execute permissions", "Startup", Log.Severity.Info);
                    sourcePath = otherExec.SourceDir;
                }
                else if (package is OtherRemovalExec otherRemovalExec && otherRemovalExec.Context == Contexts.User)
                {
                    _log.WriteLog($"Package: {package.Name}, is a user context package, setting user read/execute permissions", "Startup", Log.Severity.Info);
                    sourcePath = otherRemovalExec.SourceDir;
                }
                else if (package is MSIExec msiExec && msiExec.Context == Contexts.User && _action == SuiteAction.Deployment )
                {
                    _log.WriteLog($"Package: {package.Name}, is a user context package, setting user read/execute permissions", "Startup", Log.Severity.Info);
                    sourcePath = Path.GetDirectoryName(msiExec.MSIFile);
                }
                else continue; // Not a user-context package — skip it and keep checking the rest.
                if (string.IsNullOrWhiteSpace(sourcePath)) throw new Exception($"Package: {package.Name}, is a user context package, but the source directory is not set. Cannot set permissions.");
                SetUserReadExecutePermissions(sourcePath);
            }

            if (_suiteConfig.PowerShellEvents != null)
            {
                foreach (SuiteOperations.Events.PowerShellExecEvent ps in _suiteConfig.PowerShellEvents)
                {
                    if (ps.Context != Contexts.User) continue;

                    _log.WriteLog($"PowerShell event: {ps.ScriptName}, runs in user context, setting user read/execute permissions", "Startup", Log.Severity.Info);

                    if (!string.IsNullOrWhiteSpace(ps.ScriptPath))
                    {
                        string? scriptDir = Path.GetDirectoryName(ps.ScriptPath);
                        if (!string.IsNullOrWhiteSpace(scriptDir))
                            SetUserReadExecutePermissions(scriptDir);
                    }

                    if (!string.IsNullOrWhiteSpace(ps.SupportFilesDir))
                        SetUserReadExecutePermissions(ps.SupportFilesDir);
                }
            }
        }

        internal void SetPopupConfigPermissionsIfNeeded()
        {
            if (!HasBlockingProcClosures)
                return;

            string popupConfigDir = Path.Combine(_suiteRootDir, "Popup");
            if (!Directory.Exists(popupConfigDir))
                return;

            SetUserReadExecutePermissions(popupConfigDir);

            // The folder-level rule above only governs files created from here on — popconfig.json was
            // already extracted before this runs, so its own existing ACL needs granting directly too.
            string popupConfigFile = Path.Combine(popupConfigDir, "popconfig.json");
            if (File.Exists(popupConfigFile))
                SetUserReadPermissionsOnFile(popupConfigFile);
        }

        private void SetUserReadPermissionsOnFile(string filePath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(filePath);
                FileSecurity fileSecurity = fileInfo.GetAccessControl();

                SecurityIdentifier usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                FileSystemAccessRule usersRule = new FileSystemAccessRule(
                    usersSid,
                    FileSystemRights.ReadAndExecute | FileSystemRights.Read,
                    AccessControlType.Allow);
                fileSecurity.AddAccessRule(usersRule);

                fileInfo.SetAccessControl(fileSecurity);
                _log.WriteLog($"Successfully granted Users read access to: {filePath}", "SetUserReadPermissionsOnFile", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                // Best-effort — worst case the blocked-process notice just can't read its branding.
                _log.WriteLog($"Failed to grant Users read access to: {filePath}. Exception: {ex.Message}", "SetUserReadPermissionsOnFile", Log.Severity.Warning);
            }
        }

        private void SetUserReadExecutePermissions(string directoryPath)
        {
            try
            {
                DirectoryInfo pkgDIRInfo = new DirectoryInfo(directoryPath);
                DirectorySecurity pkgDIRSec = pkgDIRInfo.GetAccessControl();

                // Break inheritance from the parent directory so a deny (explicit or by omission)
                // on the parent cannot affect this package directory or its contents.
                pkgDIRSec.SetAccessRuleProtection(true, false);

                // Retain administrative access so the suite executor can still manage/clean up this directory later
                SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                pkgDIRSec.AddAccessRule(new FileSystemAccessRule(
                    systemSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                SecurityIdentifier adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                pkgDIRSec.AddAccessRule(new FileSystemAccessRule(
                    adminsSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                // Users group read & execute
                SecurityIdentifier usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
                FileSystemAccessRule usersRule = new FileSystemAccessRule(
                    usersSid,
                    FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory | FileSystemRights.Read,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow);
                pkgDIRSec.AddAccessRule(usersRule);

                pkgDIRInfo.SetAccessControl(pkgDIRSec);
                _log.WriteLog($"Successfully set read/execute permissions for Users on directory: {directoryPath}", "SetUserReadExecutePermissions", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to set read/execute permissions for Users on Pkg directory: {directoryPath}. Exception: {ex.Message}");
            }
        }
    }
}
