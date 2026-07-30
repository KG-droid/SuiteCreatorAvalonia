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

            List<string> converted = varPath
                .Select(v => v.GetValue())
                .Where(val => val != null)
                .ToList()!;
            return JoinPathParts(converted);
        }

        private static string JoinPathParts(List<string> converted)
        {
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

        // A SpecialDIR (e.g. %appdata%) that resolves under the current user's profile only means something
        // for the account SuiteExecutor happens to be running as (typically SYSTEM or the invoking admin) -
        // it says nothing about where real users' profiles keep the same folder. Detecting that case lets
        // Copy/Delete/Deploy/Move/Rename fan the relevant side of the operation out across every human user's
        // profile instead, mirroring RegExecEvent.ApplyAcrossUserHives for HKCU.
        private static SpecialDIR? GetUserProfileRelativeSpecialDir(List<VariableText>? varPath)
        {
            SpecialDIR? specialDIR = varPath?.OfType<SpecialDIR>().FirstOrDefault();
            if (specialDIR == null) return null;

            string parsedSpecial = Environment.GetFolderPath((Environment.SpecialFolder)specialDIR.Value);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return parsedSpecial.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase) ? specialDIR : null;
        }

        // Resolves varPath the same way ParseVarPath does, except the given SpecialDIR is substituted with
        // its equivalent path under a specific user's profile instead of the running process's own profile.
        private static string ParseVarPathForProfile(List<VariableText> varPath, SpecialDIR specialDIR, SuiteTools.UserTools.UserExtensions.UserProfile profile)
        {
            string resolvedSpecialDir = profile.GetSpecialFolder((Environment.SpecialFolder)specialDIR.Value);
            List<string> converted = varPath
                .Select(v => ReferenceEquals(v, specialDIR) ? resolvedSpecialDir : v.GetValue())
                .Where(val => val != null)
                .ToList()!;
            return JoinPathParts(converted);
        }

        // Gets the Default profile's own local path (not its NTUSER.DAT - see RegExecEvent's equivalent for
        // that), so a per-profile-fanned-out deploy can also seed new users who log on for the first time
        // after this runs.
        private static string GetDefaultProfileLocalPath()
        {
            using Microsoft.Win32.RegistryKey? profileListKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
            string profilesDirectory = profileListKey?.GetValue("ProfilesDirectory") as string ?? @"%SystemDrive%\Users";
            profilesDirectory = Environment.ExpandEnvironmentVariables(profilesDirectory);
            return Path.Combine(profilesDirectory, "Default");
        }

        // Runs action once per human user's resolved path (source or destination - whichever side of the
        // operation carries the user-profile SpecialDIR), plus once for the Default profile, so a
        // user-profile-relative path (e.g. %appdata%\MyApp) reaches every real user instead of just whichever
        // account SuiteExecutor happens to be running as. A failure for one user doesn't stop the others; the
        // whole event only fails if every user failed. The Default profile is always best-effort.
        private void ApplyAcrossUserProfiles(List<VariableText> varPath, SpecialDIR specialDIR, Action<string> action, string context)
        {
            List<SuiteTools.UserTools.UserExtensions.UserProfile> profiles =
                new SuiteTools.UserTools.UserExtensions().GetHumanUserAccountInfo() ?? new();

            List<(string Label, bool Success, string? Error)> userResults = new();
            foreach (SuiteTools.UserTools.UserExtensions.UserProfile profile in profiles)
            {
                try
                {
                    action(ParseVarPathForProfile(varPath, specialDIR, profile));
                    userResults.Add((profile.Sid, true, null));
                    _log.WriteLog($"{context} succeeded for user {profile.Sid}");
                }
                catch (Exception ex)
                {
                    userResults.Add((profile.Sid, false, ex.Message));
                    _log.WriteLog($"{context} failed for user {profile.Sid}: {ex.Message}", "FileExecEvent", Log.Severity.Warning);
                }
            }

            if (userResults.Count > 0 && userResults.All(r => !r.Success))
            {
                throw new InvalidOperationException($"{context} failed for every user profile: {string.Join("; ", userResults.Select(r => $"{r.Label}: {r.Error}"))}");
            }

            try
            {
                SuiteTools.UserTools.UserExtensions.UserProfile defaultProfile = new()
                {
                    Sid = "Default",
                    LocalPath = GetDefaultProfileLocalPath()
                };
                action(ParseVarPathForProfile(varPath, specialDIR, defaultProfile));
                _log.WriteLog($"{context} succeeded for Default profile");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"{context} failed for Default profile (new users won't inherit this change): {ex.Message}", "FileExecEvent", Log.Severity.Warning);
            }
        }

        private void Copy()
        {
            string sourcePath = ParseVarPath(SourcePath!);

            SpecialDIR? userSpecialDir = GetUserProfileRelativeSpecialDir(DestinationPath);
            if (userSpecialDir != null)
            {
                ApplyAcrossUserProfiles(DestinationPath!, userSpecialDir, destinationPath => CopyTo(sourcePath, destinationPath), $"Copy '{sourcePath}'");
                return;
            }

            CopyTo(sourcePath, ParseVarPath(DestinationPath!));
        }

        private void CopyTo(string sourcePath, string destinationPath)
        {
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
            SpecialDIR? userSpecialDir = GetUserProfileRelativeSpecialDir(SourcePath);
            if (userSpecialDir != null)
            {
                ApplyAcrossUserProfiles(SourcePath!, userSpecialDir, DeleteFrom, $"Delete {(FileSysIOType == FileSysIOType.File ? "file" : "directory")} from user profile");
                return;
            }

            DeleteFrom(ParseVarPath(SourcePath!));
        }

        private void DeleteFrom(string deletionPath)
        {
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

            SpecialDIR? userSpecialDir = GetUserProfileRelativeSpecialDir(DestinationPath);
            if (userSpecialDir != null)
            {
                ApplyAcrossUserProfiles(DestinationPath!, userSpecialDir, destinationPath => DeployTo(sourcePath, destinationPath), $"Deploy '{sourcePath}'");
                return;
            }

            DeployTo(sourcePath, ParseVarPath(DestinationPath!));
        }

        private void DeployTo(string sourcePath, string destinationPath)
        {
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

            SpecialDIR? userSpecialDir = GetUserProfileRelativeSpecialDir(DestinationPath);
            if (userSpecialDir != null)
            {
                MoveAcrossUserProfiles(sourcePath, DestinationPath!, userSpecialDir);
                return;
            }

            MoveTo(sourcePath, ParseVarPath(DestinationPath!));
        }

        private void MoveTo(string sourcePath, string destinationPath)
        {
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

        // A Move whose destination is user-profile-relative can't literally "move" a single source into N
        // different profiles - only one destination could ever really take ownership of the file. Instead
        // this copies the source into every human user's profile plus Default (via CopyTo, so it gets the
        // same directory-creation and permission handling as a normal Copy), and only deletes the original
        // source once every one of those copies has succeeded - including the Default profile, unlike the
        // lenient best-effort handling ApplyAcrossUserProfiles gives Default elsewhere, because deleting the
        // only copy of the source on a partial failure would be data loss, not just a missed nice-to-have.
        private void MoveAcrossUserProfiles(string sourcePath, List<VariableText> destPath, SpecialDIR specialDIR)
        {
            bool isFile = FileSysIOType == FileSysIOType.File;
            if (isFile ? !File.Exists(sourcePath) : !Directory.Exists(sourcePath))
            {
                string message = $"{(isFile ? "File" : "Directory")} not found: {sourcePath}";
                _log.WriteLog(message, "Application", Log.Severity.Error);
                throw isFile ? new FileNotFoundException(message, sourcePath) : new DirectoryNotFoundException(message);
            }

            List<SuiteTools.UserTools.UserExtensions.UserProfile> profiles =
                new SuiteTools.UserTools.UserExtensions().GetHumanUserAccountInfo() ?? new();
            profiles.Add(new SuiteTools.UserTools.UserExtensions.UserProfile
            {
                Sid = "Default",
                LocalPath = GetDefaultProfileLocalPath()
            });

            List<(string Label, string DestinationPath, Exception? Error)> results = new();
            foreach (SuiteTools.UserTools.UserExtensions.UserProfile profile in profiles)
            {
                string destinationPath = ParseVarPathForProfile(destPath, specialDIR, profile);
                try
                {
                    CopyTo(sourcePath, destinationPath);
                    results.Add((profile.Sid, destinationPath, null));
                    _log.WriteLog($"Move (copy phase) '{sourcePath}' to '{destinationPath}' succeeded for profile {profile.Sid}");
                }
                catch (Exception ex)
                {
                    results.Add((profile.Sid, destinationPath, ex));
                    _log.WriteLog($"Move (copy phase) '{sourcePath}' to '{destinationPath}' failed for profile {profile.Sid}: {ex.Message}", "FileExecEvent", Log.Severity.Error);
                }
            }

            if (results.Any(r => r.Error != null))
            {
                throw new InvalidOperationException(
                    $"Move '{sourcePath}' across user profiles only partially succeeded, so the original was left in place: " +
                    string.Join("; ", results.Where(r => r.Error != null).Select(r => $"{r.Label}: {r.Error!.Message}")));
            }

            ExecuteWithFileLockRetry(() =>
            {
                SeizeAccessForDelete(sourcePath);
                if (isFile)
                {
                    File.Delete(sourcePath);
                }
                else
                {
                    DeleteDirectoryRecursive(sourcePath);
                }
            }, $"delete original '{sourcePath}' after copying it to every user profile");
        }

        private void Rename()
        {
            string destinationName = ParseVarPath(DestinationPath!);

            SpecialDIR? userSpecialDir = GetUserProfileRelativeSpecialDir(SourcePath);
            if (userSpecialDir != null)
            {
                ApplyAcrossUserProfiles(SourcePath!, userSpecialDir, sourcePath => RenameFrom(sourcePath, destinationName), $"Rename to '{destinationName}'");
                return;
            }

            RenameFrom(ParseVarPath(SourcePath!), destinationName);
        }

        private void RenameFrom(string sourcePath, string destinationName)
        {
            _log.WriteLog($"Renaming {(FileSysIOType == FileSysIOType.File ? "file" : "directory")}: {sourcePath} to {destinationName}");

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
                    string destFile = Path.Combine(destDir, destinationName);
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
                    string destFolder = Path.Combine(destDir, destinationName);
                    Directory.Move(sourcePath, destFolder);
                }
            }, $"rename '{sourcePath}' to '{destinationName}'");
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
