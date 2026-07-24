using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Environment = System.Environment;

namespace SuiteOperations.Events
{
    public partial class FileExecEvent : FileSysIO
    {
        private const int FileOperationMaxRetries = 10;
        private const int FileOperationRetryDelayMs = 10000;
        private Log _log;
        public FileExecEvent(Log log)
        {
            _log = log;
            ValidatePathsStructures();
        }

        public FileExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            switch (Action)
            {
                case FileSysIOAction.Copy:
                    Copy();
                    break;
                case FileSysIOAction.Delete:
                    Delete();
                    break;
                case FileSysIOAction.Deploy:
                    Deploy();
                    break;
                case FileSysIOAction.Move:
                    Move();
                    break;
                case FileSysIOAction.Rename:
                    Rename();
                    break;
                default:
                    throw new InvalidOperationException($"Unrecognised File event Action type: {Action}");
            }
        }

        public string ParseVarPath(List<VariableText> varPath)
        {
            if (varPath == null)
            {
                throw new ArgumentNullException(nameof(varPath), "Cannot parse a null File Event path");
            }
            ValidatePathStructure(varPath);

            SpecialDIR? specialDIR = varPath.OfType<SpecialDIR>().FirstOrDefault();
            if (specialDIR != null)
            {
                string parsedSpecial = Environment.GetFolderPath((Environment.SpecialFolder)specialDIR.Value);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (parsedSpecial.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase))
                {
                    // If the special directory is under the user profile, we need to get paths for all users

                }
            }

            List<string> converted = varPath
                .Select(v => v.GetValue())
                .Where(val => val != null)
                .ToList()!;
            if (converted.Count > 1)
            {
                // Add the seprator if the user hasn't already
                for (int i = 0; i < converted.Count - 1; i++)
                {
                    if (!converted[i].EndsWith('\\') && !converted[i + 1].StartsWith('\\'))
                    {
                        converted[i] += '\\';
                    }
                }
                return string.Join("", converted);
            }
            return converted.First();
        }

        private void Copy()
        {
            string sourcePath = ParseVarPath(SourcePath!);
            string destinationPath = ParseVarPath(DestinationPath!);

            ExecuteWithFileLockRetry(() =>
            {
                if (FileSysIOType == FileSysIOType.File)
                {
                    _log.WriteLog($"Copying file: {sourcePath} to {destinationPath}");
                    if (!Directory.Exists(destinationPath))
                    {
                        _log.WriteLog($"Destination Directory does not exist, creating now..");
                        Directory.CreateDirectory(destinationPath);
                    }
                    File.Copy(sourcePath, destinationPath, ReplaceExisting);
                }
                else
                {
                    _log.WriteLog($"Copying all the files from {sourcePath} to {destinationPath}");
                    Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                        .ToList()
                        .ForEach(file =>
                        {
                            string destFile = Path.Combine(destinationPath, Path.GetFileName(file));
                            _log.WriteLog($"Copying file: {file} to {destFile}");
                            if (!Directory.Exists(destinationPath))
                            {
                                _log.WriteLog($"Destination Directory does not exist, creating now..");
                                Directory.CreateDirectory(destinationPath);
                            }
                            File.Copy(file, destFile, ReplaceExisting);
                        });
                }
            }, $"copy '{sourcePath}' to '{destinationPath}'");
            ApplyPermissions(destinationPath);
        }

        private void Delete()
        {
            string deletionPath = ParseVarPath(SourcePath!);
            _log.WriteLog($"Deleting {(FileSysIOType == FileSysIOType.File ? "file" : "directory")}: {deletionPath}");

            ExecuteWithFileLockRetry(() =>
            {
                if (FileSysIOType == FileSysIOType.File)
                {
                    if (File.Exists(deletionPath))
                    {
                        SeizeAccessForDelete(deletionPath);
                        File.Delete(deletionPath);
                        string? parentDir = Path.GetDirectoryName(deletionPath);
                        if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir) && !Directory.EnumerateFileSystemEntries(parentDir).Any())
                        {
                            _log.WriteLog($"Directory is now empty, removing: {parentDir}");
                            SeizeAccessForDelete(parentDir);
                            Directory.Delete(parentDir);
                        }
                    }
                    else
                    {
                        _log.WriteLog($"File not found: {deletionPath}", "FileExecEvent", Log.Severity.Error);
                    }
                }
                else
                {
                    if (Directory.Exists(deletionPath))
                    {
                        SeizeAccessForDelete(deletionPath);
                        DeleteDirectoryRecursive(deletionPath);
                    }
                    else
                    {
                        _log.WriteLog($"Directory not found: {deletionPath}", "FileExecEvent", Log.Severity.Error);
                    }
                }
            }, $"delete '{deletionPath}'");
        }

        /// <summary>
        /// Recursively deletes a directory without delegating to Directory.Delete(path, recursive: true).
        /// The BCL's recursive delete has known cases where it throws "The parameter is incorrect" when it
        /// encounters a nested reparse point (junction/symlink) it can't cleanly recurse through, and the
        /// resulting exception only names the offending nested entry, not the directory that was actually
        /// requested for deletion. Walking the tree ourselves - unlinking reparse points directly instead of
        /// following into their target, exactly like SeizeAccessRecursive already does for the ACL/ownership
        /// pass - avoids that failure mode entirely.
        /// </summary>
        private static void DeleteDirectoryRecursive(string path)
        {
            if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(path, false);
                return;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (Directory.Exists(entry))
                {
                    DeleteDirectoryRecursive(entry);
                }
                else
                {
                    File.Delete(entry);
                }
            }

            Directory.Delete(path, false);
        }

        /// <summary>
        /// Forcibly takes ownership of the file/directory (and, for directories, every entry beneath it)
        /// and resets its DACL to grant only the current process identity FullControl. This guarantees the
        /// subsequent delete has access regardless of whatever ACL a prior event (e.g. ApplyPermissions) or
        /// external process left in place. Best-effort: failures here are logged and swallowed so the normal
        /// delete attempt (and its existing retry/error handling) still runs and surfaces the real error.
        /// </summary>
        private void SeizeAccessForDelete(string path)
        {
            NativeMethods.EnablePrivilege("SeTakeOwnershipPrivilege");
            NativeMethods.EnablePrivilege("SeRestorePrivilege");
            NativeMethods.EnablePrivilege("SeBackupPrivilege");

            SeizeAccessRecursive(path);
        }

        private void SeizeAccessRecursive(string path)
        {
            bool isDirectory = Directory.Exists(path);

            try
            {
                SeizeAccess(path, isDirectory);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Could not seize access to '{path}' before delete: {ex.Message}", "FileExecEvent", Log.Severity.Warning);
            }

            try
            {
                ClearReadOnlyAttribute(path, isDirectory);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Could not clear read-only attribute on '{path}' before delete: {ex.Message}", "FileExecEvent", Log.Severity.Warning);
            }

            if (!isDirectory)
            {
                return;
            }

            // Don't follow reparse points (symlinks/junctions) into whatever they target.
            if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return;
            }

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(path).ToList();
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Could not enumerate '{path}' while seizing access before delete: {ex.Message}", "FileExecEvent", Log.Severity.Warning);
                return;
            }

            foreach (string entry in entries)
            {
                SeizeAccessRecursive(entry);
            }
        }

        /// <summary>
        /// Win32's DeleteFile/RemoveDirectory reject the read-only attribute before it even checks the ACL,
        /// so a correct ACL/ownership isn't enough to delete a read-only item. Explorer clears this silently;
        /// File.Delete/Directory.Delete do not.
        /// </summary>
        private static void ClearReadOnlyAttribute(string path, bool isDirectory)
        {
            FileAttributes attributes = isDirectory ? new DirectoryInfo(path).Attributes : new FileInfo(path).Attributes;
            if (!attributes.HasFlag(FileAttributes.ReadOnly))
            {
                return;
            }

            attributes &= ~FileAttributes.ReadOnly;
            if (isDirectory)
            {
                new DirectoryInfo(path).Attributes = attributes;
            }
            else
            {
                new FileInfo(path).Attributes = attributes;
            }
        }

        private static void SeizeAccess(string path, bool isDirectory)
        {
            IdentityReference self = WindowsIdentity.GetCurrent().User!;

            if (isDirectory)
            {
                DirectorySecurity owner = new DirectorySecurity();
                owner.SetOwner(self);
                new DirectoryInfo(path).SetAccessControl(owner);

                DirectorySecurity acl = new DirectorySecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new FileSystemAccessRule(
                    self,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                new DirectoryInfo(path).SetAccessControl(acl);
            }
            else
            {
                FileSecurity owner = new FileSecurity();
                owner.SetOwner(self);
                new FileInfo(path).SetAccessControl(owner);

                FileSecurity acl = new FileSecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new FileSystemAccessRule(
                    self,
                    FileSystemRights.FullControl,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                new FileInfo(path).SetAccessControl(acl);
            }
        }

        private void Deploy()
        {
            if (SourcePath == null || SourcePath.Count == 0 || SourcePath[0] is not LiteralText literal)
                throw new InvalidOperationException("Deploy action requires SourcePath to be a single LiteralText");
            string sourcePath = literal.GetValue()!;
            string destinationPath = ParseVarPath(DestinationPath!);

            ExecuteWithFileLockRetry(() =>
            {
                if (FileSysIOType == FileSysIOType.File)
                {
                    _log.WriteLog($"Deploying file: {sourcePath} to {destinationPath}");
                    string fileName = Path.GetFileName(sourcePath);
                    if (!destinationPath.EndsWith(fileName))
                    {
                        destinationPath = Path.Combine(destinationPath, fileName);
                    }
                    string destDir = Path.GetDirectoryName(destinationPath)!;
                    if (!Directory.Exists(destDir))
                    {
                        _log.WriteLog($"Destination Directory does not exist, creating now..");
                        Directory.CreateDirectory(destDir);
                    }
                    File.Copy(sourcePath, destinationPath, ReplaceExisting);
                }
                else
                {
                    _log.WriteLog($"Deploying all the files from {sourcePath} to {destinationPath}");
                    Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                        .ToList()
                        .ForEach(file =>
                        {
                            string destFile = Path.Combine(destinationPath, Path.GetFileName(file));
                            _log.WriteLog($"Deploying file: {file} to {destFile}");
                            if (!Directory.Exists(destinationPath))
                            {
                                _log.WriteLog($"Destination Directory does not exist, creating now..");
                                Directory.CreateDirectory(destinationPath);
                            }
                            File.Copy(file, destFile, ReplaceExisting);
                        });
                }
            }, $"deploy '{sourcePath}' to '{destinationPath}'");
            ApplyPermissions(destinationPath);
        }

        private void Move()
        {
            string sourcePath = ParseVarPath(SourcePath!);
            string destinationPath = ParseVarPath(DestinationPath!);
            _log.WriteLog($"Moving {(FileSysIOType == FileSysIOType.File ? "file" : "directory")}: {sourcePath} to {destinationPath}");

            ExecuteWithFileLockRetry(() =>
            {
                if (FileSysIOType == FileSysIOType.File)
                {
                    if (!File.Exists(sourcePath))
                    {
                        _log.WriteLog($"File not found: {sourcePath}", "Application", Log.Severity.Error);
                        throw new FileNotFoundException($"File not found: {sourcePath}", sourcePath);
                    }
                    if (!Directory.Exists(Path.GetDirectoryName(destinationPath)!))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    }
                    File.Move(sourcePath, destinationPath, ReplaceExisting);
                }
                else
                {
                    if (!Directory.Exists(sourcePath))
                    {
                        _log.WriteLog($"Directory not found: {sourcePath}", "Application", Log.Severity.Error);
                        throw new DirectoryNotFoundException($"Directory not found: {sourcePath}");
                    }
                    if (!Directory.Exists(Path.GetDirectoryName(destinationPath)!))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    }
                    Directory.Move(sourcePath, destinationPath);
                }
            }, $"move '{sourcePath}' to '{destinationPath}'");
            ApplyPermissions(destinationPath);
        }

        private void Rename()
        {
            string sourcePath = ParseVarPath(SourcePath!);
            string destinationPath = ParseVarPath(DestinationPath!);
            _log.WriteLog($"Renaming {(FileSysIOType == FileSysIOType.File ? "file" : "directory")}: {sourcePath} to {destinationPath}");

            ExecuteWithFileLockRetry(() =>
            {
                if (FileSysIOType == FileSysIOType.File)
                {
                    if (!File.Exists(sourcePath))
                    {
                        _log.WriteLog($"File not found: {sourcePath}", "Application", Log.Severity.Error);
                        throw new FileNotFoundException($"File not found: {sourcePath}", sourcePath);
                    }
                    string destDir = Path.GetDirectoryName(sourcePath)!;
                    string destFile = Path.Combine(destDir, destinationPath);
                    File.Move(sourcePath, destFile, ReplaceExisting);
                }
                else
                {
                    if (!Directory.Exists(sourcePath))
                    {
                        _log.WriteLog($"Directory not found: {sourcePath}", "Application", Log.Severity.Error);
                        throw new DirectoryNotFoundException($"Directory not found: {sourcePath}");
                    }
                    string destDir = Path.GetDirectoryName(sourcePath)!;
                    string destFolder = Path.Combine(destDir, destinationPath);
                    Directory.Move(sourcePath, destFolder);
                }
            }, $"rename '{sourcePath}' to '{destinationPath}'");
        }

        private void ExecuteWithFileLockRetry(Action action, string operationDescription)
        {
            for (int attempt = 1; attempt <= FileOperationMaxRetries; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (attempt < FileOperationMaxRetries && IsAccessDeniedOrFileLockException(ex))
                {
                    string reason = ex is UnauthorizedAccessException ? "access is denied" : "the file is locked";
                    _log.WriteLog($"Unable to {operationDescription} because {reason}. Retrying in {FileOperationRetryDelayMs / 1000} seconds. Attempt {attempt + 1} of {FileOperationMaxRetries}.", "FileExecEvent", Log.Severity.Warning);
                    Thread.Sleep(FileOperationRetryDelayMs);
                }
                catch (Exception ex)
                {
                    throw new IOException($"Failed to {operationDescription}: {ex.Message}", ex);
                }
            }
        }

        private static bool IsAccessDeniedOrFileLockException(Exception ex)
        {
            return ex is UnauthorizedAccessException || (ex is IOException ioEx && IsFileLockException(ioEx));
        }

        private static bool IsFileLockException(IOException ex)
        {
            return ex.Message.Contains("because it is being used by another process", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyPermissions(string targetPath)
        {
            if (!OverridePermissions || Permissions == null || Permissions.Count == 0)
                return;

            try
            {
                if (FileSysIOType == FileSysIOType.File)
                {
                    FileSecurity acl = new FileSecurity(targetPath, AccessControlSections.Access);
                    acl.SetAccessRuleProtection(true, false);
                    foreach (Permission permission in Permissions)
                    {
                        if (permission.UserGroup?.SID == null || permission.PermissionType == null)
                            continue;
                        FileSystemRights rights = permission.PermissionRights.Aggregate(default(FileSystemRights), (acc, r) => acc | r);
                        acl.AddAccessRule(new FileSystemAccessRule(
                            permission.UserGroup.SID,
                            rights,
                            InheritanceFlags.None,
                            PropagationFlags.None,
                            (AccessControlType)permission.PermissionType));
                    }
                    FileInfo fi = new FileInfo(targetPath);
                    fi.SetAccessControl(acl);
                    _log.WriteLog($"Applied permissions to file: {targetPath}");
                }
                else
                {
                    DirectorySecurity acl = new DirectorySecurity(targetPath, AccessControlSections.Access);
                    acl.SetAccessRuleProtection(true, false);
                    foreach (Permission permission in Permissions)
                    {
                        if (permission.UserGroup?.SID == null || permission.PermissionType == null)
                            continue;
                        FileSystemRights rights = permission.PermissionRights.Aggregate(default(FileSystemRights), (acc, r) => acc | r);
                        acl.AddAccessRule(new FileSystemAccessRule(
                            permission.UserGroup.SID,
                            rights,
                            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                            PropagationFlags.None,
                            (AccessControlType)permission.PermissionType));
                    }
                    DirectoryInfo di = new DirectoryInfo(targetPath);
                    di.SetAccessControl(acl);
                    _log.WriteLog($"Applied permissions to directory: {targetPath}");
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to apply permissions to {targetPath}: {ex.Message}", "FileExecEvent", Log.Severity.Error);
                throw;
            }
        }

        /// <summary>
        /// Enables Windows token privileges on the current process. .NET has no managed API for this,
        /// so it's done via advapi32 directly. Required to take ownership of files/directories whose
        /// ACL doesn't already grant the running identity access (see SeizeAccessForDelete).
        /// </summary>
        private static class NativeMethods
        {
            private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
            private const uint TOKEN_QUERY = 0x0008;
            private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

            [StructLayout(LayoutKind.Sequential)]
            private struct LUID
            {
                public uint LowPart;
                public int HighPart;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct LUID_AND_ATTRIBUTES
            {
                public LUID Luid;
                public uint Attributes;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct TOKEN_PRIVILEGES
            {
                public uint PrivilegeCount;
                public LUID_AND_ATTRIBUTES Privilege;
            }

            [DllImport("kernel32.dll")]
            private static extern IntPtr GetCurrentProcess();

            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern bool AdjustTokenPrivileges(
                IntPtr tokenHandle,
                bool disableAllPrivileges,
                ref TOKEN_PRIVILEGES newState,
                uint bufferLengthInBytes,
                IntPtr previousState,
                IntPtr returnLengthInBytes);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr handle);

            public static void EnablePrivilege(string privilegeName)
            {
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
                {
                    return;
                }

                try
                {
                    if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                    {
                        return;
                    }

                    TOKEN_PRIVILEGES tokenPrivileges = new TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Privilege = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                    };

                    AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivileges, 0, IntPtr.Zero, IntPtr.Zero);
                }
                finally
                {
                    CloseHandle(tokenHandle);
                }
            }
        }
    }
}
