using Logger;
using Microsoft.Win32;
using SuiteCreatorAvalonia.Enums;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SuiteOperations.Events
{
    public partial class RegExecEvent : SuiteCreatorAvalonia.Models.Events.Registry
    {
        private const int RegistryOperationMaxRetries = 10;
        private const int RegistryOperationRetryDelayMs = 10000;

        private Log _log;

        public RegExecEvent(Log log)
        {
            _log = log;
        }

        public RegExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        private static readonly Regex SectionHeaderRegex = new(
            @"^\[(?<del>-)?(?<hive>HKEY_CURRENT_USER|HKCU|HKEY_LOCAL_MACHINE|HKLM|HKEY_CLASSES_ROOT|HKCR|HKEY_USERS|HKU|HKEY_CURRENT_CONFIG|HKCC)(\\(?<sub>.*))?\]\s*$",
            RegexOptions.IgnoreCase);

        private sealed record RegFileBlock(bool IsDeletion, string SubKeyPath, List<string> BodyLines);

        // A single [Hive\SubKey] section from an imported .reg file, reduced to just what's needed to undo
        // it: which values it declared (so they can be individually removed) and where it lives.
        private sealed record ImportedRegSection(RegistryKey? BaseKey, bool IsUserHive, string SubKeyPath, List<string> ValueNames);

        private static readonly Regex RegValueNameRegex = new(@"^(?<name>@|""(?:[^""\\]|\\.)*"")\s*=");

        private static (RegistryKey BaseKey, string SubKeyPath) ParseRegistryKeyPath(string keyPath, bool requireSubKey = false)
        {
            string normalizedKeyPath = keyPath.Trim();

            if (normalizedKeyPath.StartsWith("Computer\\", StringComparison.OrdinalIgnoreCase))
            {
                normalizedKeyPath = normalizedKeyPath["Computer\\".Length..];
            }

            string[] keyParts = normalizedKeyPath.Split(new[] { '\\' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (keyParts.Length == 0)
            {
                throw new ArgumentException("KeyPath is missing.", nameof(keyPath));
            }

            RegistryKey baseKey = keyParts[0].ToUpperInvariant() switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                "HKU" or "HKEY_USERS" => Registry.Users,
                "HKCC" or "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
                _ => throw new ArgumentException($"Unsupported registry hive in KeyPath: {keyPath}", nameof(keyPath))
            };

            string subKeyPath = keyParts.Length > 1 ? keyParts[1] : string.Empty;
            if (requireSubKey && string.IsNullOrWhiteSpace(subKeyPath))
            {
                throw new ArgumentException("KeyPath must include a registry hive and subkey path.", nameof(keyPath));
            }

            return (baseKey, subKeyPath);
        }

        // Splits a .reg file into its HKEY_CURRENT_USER sections (handled per-user, see ApplyAcrossUserHives)
        // and everything else (HKLM/HKCR/HKU/HKCC), which is imported once as SYSTEM exactly as before.
        private static (string? Header, List<RegFileBlock> UserBlocks, List<string> OtherBlocks) SplitRegFileByHive(string[] lines)
        {
            int i = 0;
            List<string> headerLines = new();
            while (i < lines.Length && !SectionHeaderRegex.IsMatch(lines[i].TrimEnd()))
            {
                headerLines.Add(lines[i]);
                i++;
            }
            string? header = headerLines.Count > 0 ? string.Join(Environment.NewLine, headerLines).TrimEnd() : null;

            List<RegFileBlock> userBlocks = new();
            List<string> otherBlocks = new();

            while (i < lines.Length)
            {
                Match match = SectionHeaderRegex.Match(lines[i].TrimEnd());
                bool isDeletion = match.Groups["del"].Success;
                string hiveToken = match.Groups["hive"].Value;
                string subKeyPath = match.Groups["sub"].Success ? match.Groups["sub"].Value : string.Empty;
                i++;

                List<string> bodyLines = new();
                while (i < lines.Length && !SectionHeaderRegex.IsMatch(lines[i].TrimEnd()))
                {
                    bodyLines.Add(lines[i]);
                    i++;
                }

                bool isUserHive = hiveToken.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
                    || hiveToken.Equals("HKCU", StringComparison.OrdinalIgnoreCase);

                if (isUserHive)
                {
                    userBlocks.Add(new RegFileBlock(isDeletion, subKeyPath, bodyLines));
                }
                else
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append('[').Append(isDeletion ? "-" : "").Append(hiveToken);
                    if (!string.IsNullOrEmpty(subKeyPath)) sb.Append('\\').Append(subKeyPath);
                    sb.Append(']');
                    if (bodyLines.Count > 0) sb.Append(Environment.NewLine).Append(string.Join(Environment.NewLine, bodyLines));
                    otherBlocks.Add(sb.ToString());
                }
            }

            return (header, userBlocks, otherBlocks);
        }

        // Reduces an imported .reg file to the set of values (and the keys that held them) it declared, so
        // an Import event can be undone value-by-value on Removal instead of deleting whole keys outright -
        // see RemoveValuesFromImportedRegFile/PruneEmptyKeysAfterRemoval. A [-Hive\Sub] deletion section is
        // skipped: it already deleted that key at import time, so there's nothing here to undo.
        private static List<ImportedRegSection> ParseImportedRegSections(string[] lines)
        {
            List<ImportedRegSection> sections = new();
            int i = 0;
            while (i < lines.Length && !SectionHeaderRegex.IsMatch(lines[i].TrimEnd())) i++;

            while (i < lines.Length)
            {
                Match match = SectionHeaderRegex.Match(lines[i].TrimEnd());
                bool isDeletion = match.Groups["del"].Success;
                string hiveToken = match.Groups["hive"].Value;
                string subKeyPath = match.Groups["sub"].Success ? match.Groups["sub"].Value : string.Empty;
                i++;

                List<string> valueNames = new();
                while (i < lines.Length && !SectionHeaderRegex.IsMatch(lines[i].TrimEnd()))
                {
                    Match valueMatch = RegValueNameRegex.Match(lines[i]);
                    if (valueMatch.Success)
                    {
                        string rawName = valueMatch.Groups["name"].Value;
                        valueNames.Add(rawName == "@" ? string.Empty : UnescapeRegString(rawName[1..^1]));
                    }
                    i++;
                }

                if (!isDeletion && !string.IsNullOrEmpty(subKeyPath))
                {
                    bool isUserHive = hiveToken.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
                        || hiveToken.Equals("HKCU", StringComparison.OrdinalIgnoreCase);
                    RegistryKey? baseKey = isUserHive ? null : ParseRegistryKeyPath($@"{hiveToken}\{subKeyPath}").BaseKey;
                    sections.Add(new ImportedRegSection(baseKey, isUserHive, subKeyPath, valueNames));
                }
            }

            return sections;
        }

        private static string UnescapeRegString(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

        private static string BuildRegFileContent(string? header, List<string> blocks)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(header) ? "Windows Registry Editor Version 5.00" : header);
            foreach (string block in blocks)
            {
                sb.AppendLine();
                sb.AppendLine(block);
            }
            return sb.ToString();
        }

        private static string BuildUserRegFileContent(string? header, List<RegFileBlock> userBlocks, string hiveRootPath)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(header) ? "Windows Registry Editor Version 5.00" : header);
            foreach (RegFileBlock block in userBlocks)
            {
                sb.AppendLine();
                sb.Append('[').Append(block.IsDeletion ? "-" : "").Append(hiveRootPath);
                if (!string.IsNullOrEmpty(block.SubKeyPath)) sb.Append('\\').Append(block.SubKeyPath);
                sb.AppendLine("]");
                foreach (string line in block.BodyLines) sb.AppendLine(line);
            }
            return sb.ToString();
        }

        private static void RunRegExeAndThrowOnFailure(string arguments, Log? log = null)
        {
            for (int attempt = 1; attempt <= RegistryOperationMaxRetries; attempt++)
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using Process? proc = Process.Start(psi);
                if (proc == null)
                {
                    throw new InvalidOperationException("Failed to start reg.exe.");
                }
                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    return;
                }

                string details = $"{error}{output}";
                if (attempt < RegistryOperationMaxRetries && IsAccessDeniedOrLockedMessage(details))
                {
                    log?.WriteLog($"reg.exe {arguments} failed because access is denied or a file is locked. Retrying in {RegistryOperationRetryDelayMs / 1000} seconds. Attempt {attempt + 1} of {RegistryOperationMaxRetries}. Detail: {details}", "RegExecEvent", Log.Severity.Warning);
                    Thread.Sleep(RegistryOperationRetryDelayMs);
                    continue;
                }

                throw new InvalidOperationException($"reg.exe {arguments} failed (exit {proc.ExitCode}): {details}");
            }
        }

        private static bool IsAccessDeniedOrLockedMessage(string message)
        {
            return message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
                || message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                || message.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAccessDeniedOrFileLockException(Exception ex)
        {
            return ex is UnauthorizedAccessException
                || (ex is IOException && ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase));
        }

        private void ExecuteWithRegistryRetry(Action action, string operationDescription)
        {
            for (int attempt = 1; attempt <= RegistryOperationMaxRetries; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (attempt < RegistryOperationMaxRetries && IsAccessDeniedOrFileLockException(ex))
                {
                    string reason = ex is UnauthorizedAccessException ? "access is denied" : "the file is locked";
                    _log.WriteLog($"Unable to {operationDescription} because {reason}. Retrying in {RegistryOperationRetryDelayMs / 1000} seconds. Attempt {attempt + 1} of {RegistryOperationMaxRetries}.", "RegExecEvent", Log.Severity.Warning);
                    Thread.Sleep(RegistryOperationRetryDelayMs);
                }
            }
        }

        // Mounts a specific user's NTUSER.DAT (or reuses it if already live-mounted because the user is
        // logged on) so HKCU-targeted operations apply to that user regardless of whether they're logged
        // on. SYSTEM has full access to any loaded/live hive, so no impersonation is required.
        private sealed class UserHiveContext : IDisposable
        {
            public RegistryKey Root { get; }
            public string RegFileRootName { get; }
            private readonly string? _tempHiveName;
            private readonly Log? _log;

            private UserHiveContext(RegistryKey root, string regFileRootName, string? tempHiveName, Log? log)
            {
                Root = root;
                RegFileRootName = regFileRootName;
                _tempHiveName = tempHiveName;
                _log = log;
            }

            public static UserHiveContext OpenForUser(string sid, string profileLocalPath, Log? log = null)
            {
                RegistryKey? live = Registry.Users.OpenSubKey(sid, writable: true);
                if (live != null)
                {
                    // User is currently logged on - their hive is already mounted live under HKEY_USERS.
                    return new UserHiveContext(live, $@"HKEY_USERS\{sid}", null, log);
                }

                string ntUserDatPath = Path.Combine(profileLocalPath, "NTUSER.DAT");
                if (!File.Exists(ntUserDatPath))
                {
                    throw new FileNotFoundException($"NTUSER.DAT not found for profile: {profileLocalPath}", ntUserDatPath);
                }

                string tempHiveName = $"SuiteExecutor_{Guid.NewGuid():N}";
                RunRegExeAndThrowOnFailure($"load HKU\\{tempHiveName} \"{ntUserDatPath}\"", log);
                RegistryKey? loaded = Registry.Users.OpenSubKey(tempHiveName, writable: true);
                if (loaded == null)
                {
                    TryUnload(tempHiveName, log);
                    throw new InvalidOperationException($"Failed to open freshly loaded hive for profile: {profileLocalPath}");
                }
                return new UserHiveContext(loaded, $@"HKEY_USERS\{tempHiveName}", tempHiveName, log);
            }

            public static UserHiveContext OpenDefaultProfile(Log? log = null)
            {
                string defaultNtUserDat = GetDefaultProfileNtUserDatPath();
                if (!File.Exists(defaultNtUserDat))
                {
                    throw new FileNotFoundException("Default profile NTUSER.DAT not found.", defaultNtUserDat);
                }

                string tempHiveName = $"SuiteExecutor_Default_{Guid.NewGuid():N}";
                RunRegExeAndThrowOnFailure($"load HKU\\{tempHiveName} \"{defaultNtUserDat}\"", log);
                RegistryKey? loaded = Registry.Users.OpenSubKey(tempHiveName, writable: true);
                if (loaded == null)
                {
                    TryUnload(tempHiveName, log);
                    throw new InvalidOperationException("Failed to open freshly loaded Default profile hive.");
                }
                return new UserHiveContext(loaded, $@"HKEY_USERS\{tempHiveName}", tempHiveName, log);
            }

            private static string GetDefaultProfileNtUserDatPath()
            {
                using RegistryKey? profileListKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
                string profilesDirectory = profileListKey?.GetValue("ProfilesDirectory") as string ?? @"%SystemDrive%\Users";
                profilesDirectory = Environment.ExpandEnvironmentVariables(profilesDirectory);
                return Path.Combine(profilesDirectory, "Default", "NTUSER.DAT");
            }

            private static void TryUnload(string tempHiveName, Log? log = null)
            {
                try { RunRegExeAndThrowOnFailure($"unload HKU\\{tempHiveName}", log); }
                catch { /* best-effort cleanup */ }
            }

            public void Dispose()
            {
                Root.Dispose();
                if (_tempHiveName != null)
                {
                    // reg.exe unload fails while any handle into the hive is still open, including ones this
                    // process already Dispose()'d - force pending finalizers so the underlying safe handles
                    // are actually released before we attempt the unload.
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    TryUnload(_tempHiveName, _log);
                }
            }
        }

        // Applies action to every human user's HKCU hive (loading it temporarily if that user isn't logged
        // on) plus the Default profile hive, so the change applies regardless of who's logged on and to
        // users who log on for the first time after this runs. A failure for one user doesn't stop the
        // others; the whole event only fails if every user's HKCU hive failed. The Default profile is
        // always best-effort and never fails the event on its own.
        private void ApplyAcrossUserHives(Action<RegistryKey> action, string context)
        {
            List<SuiteTools.UserTools.UserExtensions.UserProfile> profiles =
                new SuiteTools.UserTools.UserExtensions().GetHumanUserAccountInfo() ?? new();

            List<(string Label, bool Success, string? Error)> userResults = new();
            foreach (SuiteTools.UserTools.UserExtensions.UserProfile profile in profiles)
            {
                try
                {
                    using UserHiveContext ctx = UserHiveContext.OpenForUser(profile.Sid, profile.LocalPath, _log);
                    action(ctx.Root);
                    userResults.Add((profile.Sid, true, null));
                    _log.WriteLog($"{context} succeeded for user {profile.Sid}");
                }
                catch (Exception ex)
                {
                    userResults.Add((profile.Sid, false, ex.Message));
                    _log.WriteLog($"{context} failed for user {profile.Sid}: {ex.Message}", "RegExecEvent", Log.Severity.Warning);
                }
            }

            if (userResults.Count > 0 && userResults.All(r => !r.Success))
            {
                throw new InvalidOperationException($"{context} failed for every user profile: {string.Join("; ", userResults.Select(r => $"{r.Label}: {r.Error}"))}");
            }

            try
            {
                using UserHiveContext defaultCtx = UserHiveContext.OpenDefaultProfile(_log);
                action(defaultCtx.Root);
                _log.WriteLog($"{context} succeeded for Default profile");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"{context} failed for Default profile (new users won't inherit this change): {ex.Message}", "RegExecEvent", Log.Severity.Warning);
            }
        }

        private void ImportAcrossUserHives(string? header, List<RegFileBlock> userBlocks)
        {
            List<SuiteTools.UserTools.UserExtensions.UserProfile> profiles =
                new SuiteTools.UserTools.UserExtensions().GetHumanUserAccountInfo() ?? new();

            List<(string Label, bool Success, string? Error)> userResults = new();
            foreach (SuiteTools.UserTools.UserExtensions.UserProfile profile in profiles)
            {
                try
                {
                    using UserHiveContext ctx = UserHiveContext.OpenForUser(profile.Sid, profile.LocalPath, _log);
                    ImportBlocksIntoHive(header, userBlocks, ctx, _log);
                    userResults.Add((profile.Sid, true, null));
                    _log.WriteLog($"HKCU registry import succeeded for user {profile.Sid}");
                }
                catch (Exception ex)
                {
                    userResults.Add((profile.Sid, false, ex.Message));
                    _log.WriteLog($"HKCU registry import failed for user {profile.Sid}: {ex.Message}", "RegExecEvent", Log.Severity.Warning);
                }
            }

            if (userResults.Count > 0 && userResults.All(r => !r.Success))
            {
                throw new InvalidOperationException($"HKCU registry import failed for every user profile: {string.Join("; ", userResults.Select(r => $"{r.Label}: {r.Error}"))}");
            }

            try
            {
                using UserHiveContext defaultCtx = UserHiveContext.OpenDefaultProfile(_log);
                ImportBlocksIntoHive(header, userBlocks, defaultCtx, _log);
                _log.WriteLog("HKCU registry import succeeded for Default profile");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"HKCU registry import failed for Default profile (new users won't inherit this change): {ex.Message}", "RegExecEvent", Log.Severity.Warning);
            }
        }

        private static void ImportBlocksIntoHive(string? header, List<RegFileBlock> userBlocks, UserHiveContext ctx, Log? log = null)
        {
            string content = BuildUserRegFileContent(header, userBlocks, ctx.RegFileRootName);
            string tempFile = Path.Combine(Path.GetTempPath(), $"SuiteExecutor_HkcuImport_{Guid.NewGuid():N}.reg");
            try
            {
                File.WriteAllText(tempFile, content);
                RunRegExeAndThrowOnFailure($"import \"{tempFile}\"", log);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { /* best-effort cleanup */ }
            }
        }

        private void AddAmendValue(RegistryKey hiveRoot, string subKeyPath)
        {
            // Record whether this key already existed before the suite touched it (across every hive this
            // runs against, e.g. one per user profile for HKCU) - PruneEmptyKeysAfterRemoval later needs this
            // to know it's safe to delete the key once it's empty, rather than deleting a key Windows itself
            // creates by default that just happens to hold nothing right now. Sticky: once true, stays true.
            bool existedBefore = hiveRoot.OpenSubKey(subKeyPath) != null;
            KeyAlreadyExisted = KeyAlreadyExisted == true || existedBefore;

            using RegistryKey? key = hiveRoot.CreateSubKey(subKeyPath);
            if (key == null)
            {
                throw new Exception($"Failed to open or create registry key: {KeyPath}");
            }
            if (!Overwrite && key.GetValue(PropertyName) != null)
            {
                _log.WriteLog($"Registry value already exists and Overwrite is false, skipping: {KeyPath} [{PropertyName}]");
                return;
            }
            RegistryValueKind kind = PropertyType switch
            {
                RegistryPropertyType.String => RegistryValueKind.String,
                RegistryPropertyType.ExpandString => RegistryValueKind.ExpandString,
                RegistryPropertyType.Binary => RegistryValueKind.Binary,
                RegistryPropertyType.DWord => RegistryValueKind.DWord,
                RegistryPropertyType.QWord => RegistryValueKind.QWord,
                RegistryPropertyType.MultiString => RegistryValueKind.MultiString,
                _ => RegistryValueKind.String
            };
            object value = kind switch
            {
                RegistryValueKind.DWord => int.TryParse(PropertyValue, out int d) ? d : 0,
                RegistryValueKind.QWord => long.TryParse(PropertyValue, out long q) ? q : 0L,
                RegistryValueKind.MultiString => PropertyValue?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
                RegistryValueKind.Binary => !string.IsNullOrEmpty(PropertyValue) ? Convert.FromBase64String(PropertyValue) : Array.Empty<byte>(),
                _ => PropertyValue ?? string.Empty
            };
            key.SetValue(PropertyName, value, kind);
            _log.WriteLog($"Set registry value: {KeyPath} [{PropertyName}] = {PropertyValue} ({kind})");
        }

        private void RemoveKeyOrValue(RegistryKey hiveRoot, string subKeyPath)
        {
            if (string.IsNullOrWhiteSpace(PropertyName))
            {
                hiveRoot.DeleteSubKeyTree(subKeyPath, false);
                _log.WriteLog($"Deleted registry key: {KeyPath}");
            }
            else
            {
                using RegistryKey? key = hiveRoot.OpenSubKey(subKeyPath, writable: true);
                if (key == null)
                {
                    // Key already gone means the value is already gone too - nothing left to do.
                    _log.WriteLog($"Registry key not found, nothing to remove: {KeyPath} [{PropertyName}]");
                    return;
                }
                key.DeleteValue(PropertyName, false);
                _log.WriteLog($"Deleted registry value: {KeyPath} [{PropertyName}]");
            }
        }

        // Undoes an Import event on Removal by deleting just the values it declared, not the keys they live
        // in - a key may be shared with other values the suite didn't add, or with other events that still
        // need to remove their own values from it later in this same Removal run. Keys are only ever
        // deleted afterwards, once empty, by PruneEmptyKeysAfterRemoval (called once the whole suite has
        // finished removing everything it placed).
        private void RemoveValuesFromImportedRegFile()
        {
            if (RegFilePath == null || !File.Exists(RegFilePath.LocalPath))
            {
                _log.WriteLog($"Registry import undo skipped, .reg file not found: {RegFilePath}", "RegExecEvent", Log.Severity.Warning);
                return;
            }

            string[] lines = File.ReadAllLines(RegFilePath.LocalPath);
            foreach (ImportedRegSection section in ParseImportedRegSections(lines))
            {
                if (section.ValueNames.Count == 0) continue;

                if (section.IsUserHive)
                {
                    ApplyAcrossUserHives(
                        hiveRoot => ExecuteWithRegistryRetry(() => RemoveValues(hiveRoot, section.SubKeyPath, section.ValueNames), $"remove imported registry values under '{section.SubKeyPath}'"),
                        $"Registry Import-undo values '{section.SubKeyPath}'");
                }
                else
                {
                    ExecuteWithRegistryRetry(() => RemoveValues(section.BaseKey!, section.SubKeyPath, section.ValueNames), $"remove imported registry values under '{section.SubKeyPath}'");
                }
            }
        }

        private void RemoveValues(RegistryKey hiveRoot, string subKeyPath, List<string> valueNames)
        {
            using RegistryKey? key = hiveRoot.OpenSubKey(subKeyPath, writable: true);
            if (key == null)
            {
                _log.WriteLog($"Registry key not found while undoing import, skipping: {subKeyPath}");
                return;
            }
            foreach (string valueName in valueNames)
            {
                if (key.GetValue(valueName) != null)
                {
                    key.DeleteValue(valueName, false);
                    _log.WriteLog($"Deleted imported registry value: {subKeyPath} [{valueName}]");
                }
            }
        }

        // Called once, after every registry-removal event in the suite has run (see Suite.Execution.cs),
        // so a key already emptied out by this event doesn't get pruned out from under a sibling event that
        // still has a value of its own to remove from the same key later in the same run.
        public void PruneEmptyKeysAfterRemoval()
        {
            if (Action != RegAction.Remove) return;
            try
            {
                if (!string.IsNullOrWhiteSpace(KeyPath))
                {
                    // Empty PropertyName means this was a user-authored "remove the whole key" Remove event,
                    // which already deleted the key outright in RemoveKeyOrValue - nothing left to prune.
                    if (string.IsNullOrWhiteSpace(PropertyName)) return;

                    // Only prune a key this suite actually created. If it already existed before the suite's
                    // AddAmend ran (or that was never recorded, e.g. the value was removed without the Add
                    // ever having executed in this install), leave it - it may be a key Windows creates by
                    // default that's simply empty right now, not something safe to delete.
                    if (KeyAlreadyExisted != false)
                    {
                        _log.WriteLog($"Skipping prune of '{KeyPath}': key existed before the suite created/amended it, or that isn't known.");
                        return;
                    }

                    (RegistryKey baseKey, string subKeyPath) = ParseRegistryKeyPath(KeyPath, requireSubKey: true);
                    if (baseKey == Registry.CurrentUser)
                        ApplyAcrossUserHives(hiveRoot => PruneKeyIfEmpty(hiveRoot, subKeyPath), $"Registry key prune '{KeyPath}'");
                    else
                        PruneKeyIfEmpty(baseKey, subKeyPath);
                }
                else if (RegFilePath != null && File.Exists(RegFilePath.LocalPath))
                {
                    string[] lines = File.ReadAllLines(RegFilePath.LocalPath);
                    Dictionary<string, bool> preExisted = LoadSectionPreExistence(RegFilePath.LocalPath);
                    foreach (ImportedRegSection section in ParseImportedRegSections(lines))
                    {
                        // Only prune a key this Import actually created. If it already existed before the
                        // import ran, or that was never recorded (e.g. an older suite build), leave it -
                        // it may be a key Windows creates by default that's simply empty right now.
                        if (!preExisted.TryGetValue(section.SubKeyPath, out bool existedBeforeImport) || existedBeforeImport)
                        {
                            _log.WriteLog($"Skipping prune of '{section.SubKeyPath}': key existed before the suite imported into it, or that isn't known.");
                            continue;
                        }

                        if (section.IsUserHive)
                            ApplyAcrossUserHives(hiveRoot => PruneKeyIfEmpty(hiveRoot, section.SubKeyPath), $"Registry key prune '{section.SubKeyPath}'");
                        else
                            PruneKeyIfEmpty(section.BaseKey!, section.SubKeyPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Registry key pruning failed: {ex.Message}", "RegExecEvent", Log.Severity.Warning);
            }
        }

        private void PruneKeyIfEmpty(RegistryKey hiveRoot, string subKeyPath)
        {
            using (RegistryKey? key = hiveRoot.OpenSubKey(subKeyPath, writable: false))
            {
                if (key == null) return;
                if (key.SubKeyCount > 0 || key.ValueCount > 0) return;
            }
            hiveRoot.DeleteSubKeyTree(subKeyPath, false);
            _log.WriteLog($"Removed now-empty registry key: {subKeyPath}");
        }

        // Public so Suite.UninstallMedia.CopyUninstallRegistryFiles can stage this alongside the .reg file
        // it copies into the uninstall media, using the same naming convention as here.
        public static string GetPreExistenceSidecarPath(string regFilePath) => regFilePath + ".precreated.json";

        // Records, per section of an imported .reg file, whether its key already existed before this Import
        // ran - so a later Removal (PruneEmptyKeysAfterRemoval) only deletes a key it's undoing if the import
        // itself is what created it, not one Windows (or something else) already had in place that just
        // happens to be empty now. Persisted next to the .reg file so Suite.UninstallMedia.
        // CopyUninstallRegistryFiles can carry it into the uninstall media alongside the .reg file itself,
        // for the later Removal run - which reads a different copy of this same file - to read back.
        private void RecordSectionPreExistence(string[] regFileLines, string regFilePath)
        {
            try
            {
                List<ImportedRegSection> sections = ParseImportedRegSections(regFileLines);
                if (sections.Count == 0) return;

                Dictionary<string, bool> preExisted = new(StringComparer.OrdinalIgnoreCase);

                foreach (ImportedRegSection section in sections.Where(s => !s.IsUserHive))
                {
                    using RegistryKey? key = section.BaseKey!.OpenSubKey(section.SubKeyPath);
                    preExisted[section.SubKeyPath] = key != null;
                }

                List<string> userSubKeyPaths = sections.Where(s => s.IsUserHive)
                    .Select(s => s.SubKeyPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (userSubKeyPaths.Count > 0)
                {
                    foreach (string subKeyPath in userSubKeyPaths) preExisted[subKeyPath] = false;
                    try
                    {
                        ApplyAcrossUserHives(hiveRoot =>
                        {
                            foreach (string subKeyPath in userSubKeyPaths)
                            {
                                if (preExisted[subKeyPath]) continue;
                                using RegistryKey? key = hiveRoot.OpenSubKey(subKeyPath);
                                if (key != null) preExisted[subKeyPath] = true;
                            }
                        }, "Registry Import pre-existence check");
                    }
                    catch (Exception ex)
                    {
                        // Couldn't confirm one way or the other for any profile - default those keys to
                        // "already existed" so Removal errs on the side of leaving them rather than risking
                        // deleting one it shouldn't.
                        _log.WriteLog($"Could not determine pre-existence of imported HKCU keys, treating them as pre-existing so they won't be pruned: {ex.Message}", "RegExecEvent", Log.Severity.Warning);
                        foreach (string subKeyPath in userSubKeyPaths) preExisted[subKeyPath] = true;
                    }
                }

                File.WriteAllText(GetPreExistenceSidecarPath(regFilePath), JsonSerializer.Serialize(preExisted));
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to record registry key pre-existence for '{regFilePath}': {ex.Message}", "RegExecEvent", Log.Severity.Warning);
            }
        }

        private static Dictionary<string, bool> LoadSectionPreExistence(string regFilePath)
        {
            string sidecarPath = GetPreExistenceSidecarPath(regFilePath);
            if (!File.Exists(sidecarPath)) return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Dictionary<string, bool>? raw = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(sidecarPath));
                return raw != null ? new Dictionary<string, bool>(raw, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void ExecuteEvent()
        {
            if (Action == null)
            {
                _log.WriteLog("No registry action specified.");
                throw new InvalidOperationException("No registry action specified.");
            }
            try
            {
                switch (Action)
                {
                    case RegAction.Import:
                        if (RegFilePath == null || string.IsNullOrWhiteSpace(RegFilePath.LocalPath) || !File.Exists(RegFilePath.LocalPath))
                        {
                            _log.WriteLog($"Registry file not found: {RegFilePath}", "RegExecEvent", Log.Severity.Error);
                            throw new FileNotFoundException($"Registry file not found: {RegFilePath}", RegFilePath?.LocalPath);
                        }

                        string[] regFileLines = File.ReadAllLines(RegFilePath.LocalPath);
                        (string? regHeader, List<RegFileBlock> hkcuBlocks, List<string> otherHiveBlocks) = SplitRegFileByHive(regFileLines);

                        RecordSectionPreExistence(regFileLines, RegFilePath.LocalPath);

                        if (hkcuBlocks.Count == 0)
                        {
                            // No HKCU sections - behave exactly as before, importing the file directly as SYSTEM.
                            RunRegExeAndThrowOnFailure($"import \"{RegFilePath.LocalPath}\"", _log);
                            _log.WriteLog($"Registry import succeeded: {RegFilePath.LocalPath}");
                        }
                        else
                        {
                            if (otherHiveBlocks.Count > 0)
                            {
                                string systemTempFile = Path.Combine(Path.GetTempPath(), $"SuiteExecutor_SystemImport_{Guid.NewGuid():N}.reg");
                                try
                                {
                                    File.WriteAllText(systemTempFile, BuildRegFileContent(regHeader, otherHiveBlocks));
                                    RunRegExeAndThrowOnFailure($"import \"{systemTempFile}\"", _log);
                                    _log.WriteLog($"Registry import (non-HKCU sections) succeeded: {RegFilePath.LocalPath}");
                                }
                                finally
                                {
                                    try { File.Delete(systemTempFile); } catch { /* best-effort cleanup */ }
                                }
                            }
                            ImportAcrossUserHives(regHeader, hkcuBlocks);
                        }
                        break;
                    case RegAction.AddAmend:
                        if (string.IsNullOrWhiteSpace(KeyPath) || string.IsNullOrWhiteSpace(PropertyName))
                        {
                            throw new ArgumentException("KeyPath or PropertyName is missing.");
                        }
                        (RegistryKey addBaseKey, string addSubKeyPath) = ParseRegistryKeyPath(KeyPath, requireSubKey: true);
                        if (addBaseKey == Registry.CurrentUser)
                            ApplyAcrossUserHives(hiveRoot => ExecuteWithRegistryRetry(() => AddAmendValue(hiveRoot, addSubKeyPath), $"add/amend registry value '{KeyPath}'"), $"Registry AddAmend '{KeyPath}'");
                        else
                            ExecuteWithRegistryRetry(() => AddAmendValue(addBaseKey, addSubKeyPath), $"add/amend registry value '{KeyPath}'");
                        break;
                    case RegAction.Remove:
                        if (string.IsNullOrWhiteSpace(KeyPath))
                        {
                            // A reversed Import event: it never had a KeyPath, only the .reg file it imported.
                            // Undo it value-by-value from that file instead of failing outright.
                            if (RegFilePath == null)
                            {
                                _log.WriteLog("KeyPath is missing.", "RegExecEvent", Log.Severity.Error);
                                throw new InvalidOperationException("KeyPath is missing.");
                            }
                            RemoveValuesFromImportedRegFile();
                            break;
                        }
                        (RegistryKey removeBaseKey, string removeSubKeyPath) = ParseRegistryKeyPath(KeyPath, requireSubKey: true);
                        if (removeBaseKey == Registry.CurrentUser)
                            ApplyAcrossUserHives(hiveRoot => ExecuteWithRegistryRetry(() => RemoveKeyOrValue(hiveRoot, removeSubKeyPath), $"remove registry key/value '{KeyPath}'"), $"Registry Remove '{KeyPath}'");
                        else
                            ExecuteWithRegistryRetry(() => RemoveKeyOrValue(removeBaseKey, removeSubKeyPath), $"remove registry key/value '{KeyPath}'");
                        break;
                    default:
                        _log.WriteLog($"Unknown registry action: {Action}");
                        throw new InvalidOperationException($"Unknown registry action: {Action}");
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Registry event failed: {ex.Message}", "RegExecEvent", Log.Severity.Error);
                throw;
            }
        }
    }
}
