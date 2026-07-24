using SuiteTools;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace SuiteSfxStub;

internal static class Program
{
    private const int Magic = unchecked((int)0x53554658); // 'SUFX'
    private static readonly string _installedExecutorDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SuiteExecutor");

    // The suite always extracts to a fixed, admin-only location under %windir%. This is deliberately
    // NOT configurable: the cache directory is hardened (inheritance broken, non-admin access removed)
    // and everything the elevated SuiteExecutor runs is read from here, so it must live somewhere a
    // standard user cannot write to or pre-create. %windir% denies non-admins write access by default.
    private static readonly string _cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SuiteInstallerCache");

    private static int _suiteExecExitCode = 0;

    public static int Main(string[] args)
    {
        if (args.Length == 1 && (args[0] == "?" || args[0] == "-?" || args[0] == "/?"))
        {
            ShowHelp();
            return 0;
        }

        string[] effectiveArgs = args;
        if (args.Length == 0)
        {
            string? suiteAction = PromptForSuiteAction();
            if (string.IsNullOrWhiteSpace(suiteAction))
            {
                ShowHelp();
                return 1;
            }

            effectiveArgs = new[] { suiteAction };
        }

        var selfPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(selfPath) || !File.Exists(selfPath))
        {
            Console.Error.WriteLine("Error: Unable to determine self executable path.");
            return 1612;
        }

        string? extractRoot = null;
        bool keepCache = false;
        try
        {
            // Read the appended zip payload in place (no temp staging) so we can determine the suite
            // identity before committing anything to disk.
            using (FileStream exeFs = new FileStream(selfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                (long zipStart, long zipLength) = LocatePayload(exeFs);
                using BoundedStream payloadStream = new BoundedStream(exeFs, zipStart, zipLength);
                using ZipArchive archive = new ZipArchive(payloadStream, ZipArchiveMode.Read, leaveOpen: true);

                ReadBuildSettingsFromArchive(archive, out string? suiteGUID, out keepCache, out Version? incomingVersion, out int incomingRevision);
                if (string.IsNullOrWhiteSpace(suiteGUID))
                {
                    throw new InvalidDataException("UpgradeCode is missing in BuildSettings. Cannot determine the cache location.");
                }

                extractRoot = Path.Combine(_cacheRoot, suiteGUID);

                // A deferral reminder task re-runs the suite straight from this cache directory, so whatever
                // is sitting here when the reminder fires is what actually executes. Guard against a stale or
                // out-of-order re-run (e.g. an older package accidentally redeployed) clobbering a cache that
                // already holds a newer fix — only skip extraction if what's cached is strictly newer than
                // what we're about to extract; otherwise always overwrite so a fixed revision reliably replaces
                // whatever a pending deferral was going to run.
                if (IsCachedSuiteNewer(extractRoot, incomingVersion, incomingRevision, out Version? cachedVersion, out int cachedRevision))
                {
                    Console.WriteLine($"Cached suite (Version={cachedVersion}, Revision={cachedRevision}) is newer than or equal to the incoming payload (Version={incomingVersion}, Revision={incomingRevision}); keeping the cached copy.");
                }
                else
                {
                    Directory.CreateDirectory(extractRoot);
                    HardenSuiteDirectory(extractRoot);

                    archive.ExtractToDirectory(extractRoot, overwriteFiles: true);

                    // The Popup folder needs Users read/execute; apply it after extraction so the folder exists.
                    SetPopupPermissions(extractRoot);
                }
            }

            string? extractedSuiteExecutorPath = FindExtractedSuiteExecutorPath(extractRoot);
            if (extractedSuiteExecutorPath == null)
            {
                Console.Error.WriteLine("Error: SuiteExecutor.exe was not found in extracted payload.");
                return 1;
            }

            string? suiteConfigPath = FindSuiteConfigPath(extractRoot);
            string suiteRootDir = !string.IsNullOrWhiteSpace(suiteConfigPath)
                ? Path.GetDirectoryName(suiteConfigPath)!
                : extractRoot;

            Version? extractedVersion = null;
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(extractedSuiteExecutorPath);
                if (Version.TryParse(fvi.FileVersion, out var v))
                    extractedVersion = v;
            }
            catch
            {
                extractedVersion = null;
            }

            bool installedExists = TryGetNewestInstalledSuiteExecutor(out string newestInstalledPath, out Version newestInstalledVersion);
            bool needsUpdate;
            bool forceUpdate = false;

            if (!installedExists)
            {
                needsUpdate = true;
            }
            else if (extractedVersion != null && newestInstalledVersion < extractedVersion)
            {
                needsUpdate = true;
            }
            else if (extractedVersion != null && newestInstalledVersion == extractedVersion && IsExecutableMismatched(newestInstalledPath, extractedSuiteExecutorPath))
            {
                needsUpdate = true;
                forceUpdate = true;
            }
            else if (IsInstalledPopupOutdated(extractedSuiteExecutorPath))
            {
                needsUpdate = true;
                forceUpdate = true;
            }
            else
            {
                needsUpdate = false;
            }

            if (needsUpdate)
            {
                TryInstallOrUpdateSuiteExecutor(
                    extractRoot,
                    extractedSuiteExecutorPath,
                    installedExists ? newestInstalledVersion : null,
                    extractedVersion,
                    forceUpdate);

                // Re-discover the installed executor after install/update.
                installedExists = TryGetNewestInstalledSuiteExecutor(out newestInstalledPath, out newestInstalledVersion);
            }

            if (!installedExists)
            {
                Console.Error.WriteLine("Error: SuiteExecutor could not be installed or located. Cannot proceed.");
                return 1603;
            }

            string suiteExecutorPath = newestInstalledPath;

            ProcessStartInfo suiteExecStartInfo = new ProcessStartInfo
            {
                FileName = suiteExecutorPath,
                UseShellExecute = false,
                WorkingDirectory = suiteRootDir
            };

            foreach (string arg in effectiveArgs)
                suiteExecStartInfo.ArgumentList.Add(arg);

            if (!string.IsNullOrWhiteSpace(suiteConfigPath))
            {
                suiteExecStartInfo.ArgumentList.Add("--Config");
                suiteExecStartInfo.ArgumentList.Add(suiteConfigPath);
            }

            Console.Error.WriteLine("Launching SuiteExecutor...");
            using (Process proc = Process.Start(suiteExecStartInfo)!)
            {
                if (proc == null)
                {
                    Console.Error.WriteLine("Error: Failed to start SuiteExecutor.exe.");
                    return 1;
                }

                // Show progress dots while the process is running
                Console.Write("Running suite");
                int dotCount = 0;
                while (!proc.HasExited)
                {
                    Console.Write(".");
                    dotCount++;
                    Thread.Sleep(500);
                    if (dotCount % 10 == 0)
                    {
                        Console.Write("\b \b"); // Optional: prevent line from getting too long
                    }
                }
                // Clear the progress line
                Console.WriteLine();
                _suiteExecExitCode = proc.ExitCode;
                Console.WriteLine($"Suite ended with exit code: {_suiteExecExitCode}");
                return proc.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Suite stub failed, error: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.WriteLine($"Cleaning up files");
            int maxRetries = 5;
            int delayMs = 5000;

            // Exit code 1602 means the run deferred or was skipped by the user; keep the extracted cache so the
            // scheduled deferral reminder can re-run the suite from it, even when KeepCache is off.
            bool suiteDeferred = _suiteExecExitCode == 1602;
            if (!keepCache && !suiteDeferred && !string.IsNullOrWhiteSpace(extractRoot))
            {
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (Directory.Exists(extractRoot))
                        {
                            Directory.Delete(extractRoot, recursive: true);
                        }
                        break;
                    }
                    catch (IOException) when (attempt < maxRetries)
                    {
                        Thread.Sleep(delayMs);
                    }
                    catch
                    {
                        // Ignore cleanup failures.
                        break;
                    }
                }
            }
        }
    }

    // Payload format (appended to the end of the exe):
    // [zipBytes...][int32 zipLength][int32 magic 'SUFX']
    // Returns the byte range of the embedded zip within the exe so it can be read/extracted in place.
    private static (long zipStart, long zipLength) LocatePayload(FileStream fs)
    {
        if (fs.Length < 8)
            throw new InvalidDataException("Invalid SFX file.");

        fs.Seek(-8, SeekOrigin.End);
        using var br = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);
        int zipLength = br.ReadInt32();
        int magic = br.ReadInt32();
        if (magic != Magic)
            throw new InvalidDataException("Invalid SFX magic.");
        if (zipLength <= 0 || zipLength > fs.Length - 8)
            throw new InvalidDataException("Invalid payload length.");

        long zipStart = fs.Length - 8 - zipLength;
        return (zipStart, zipLength);
    }

    // Read-only, seekable view over a fixed byte window of an underlying stream. Lets a ZipArchive read
    // the appended payload directly from the exe without copying it to a (user-writable) temp file first.
    private sealed class BoundedStream : Stream
    {
        private readonly Stream _base;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public BoundedStream(Stream baseStream, long start, long length)
        {
            _base = baseStream;
            _start = start;
            _length = length;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length)
                return 0;

            long remaining = _length - _position;
            if (count > remaining)
                count = (int)remaining;

            _base.Seek(_start + _position, SeekOrigin.Begin);
            int read = _base.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (newPos < 0)
                throw new IOException("Attempted to seek before the beginning of the stream.");
            _position = newPos;
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string? FindExtractedSuiteExecutorPath(string extractRoot)
    {
        var direct = Path.Combine(extractRoot, "SuiteExecutor.exe");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(extractRoot, "SuiteExecutor.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static string? FindSuiteConfigPath(string extractRoot)
    {
        var direct = Path.Combine(extractRoot, "SuiteConfig.scfg");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(extractRoot, "*.scfg", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static void ReadBuildSettingsFromArchive(ZipArchive archive, out string? suiteGUID, out bool keepCache, out Version? version, out int revision)
    {
        suiteGUID = null;
        keepCache = false;
        version = null;
        revision = 0;

        ZipArchiveEntry? scfgEntry = archive.Entries
            .FirstOrDefault(e => e.Name.EndsWith(".scfg", StringComparison.OrdinalIgnoreCase));
        if (scfgEntry == null)
            return;

        using Stream entryStream = scfgEntry.Open();
        using JsonDocument doc = JsonDocument.Parse(entryStream);

        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("BuildSettings", out JsonElement buildSettings))
            return;

        if (buildSettings.TryGetProperty("UpgradeCode", out JsonElement suiteGUIDEl) &&
            suiteGUIDEl.ValueKind == JsonValueKind.String)
        {
            string? parsed = suiteGUIDEl.GetString();
            if (!string.IsNullOrWhiteSpace(parsed))
                suiteGUID = parsed;
        }

        if (buildSettings.TryGetProperty("KeepCache", out JsonElement keepCacheEl) &&
            keepCacheEl.ValueKind == JsonValueKind.True)
        {
            keepCache = true;
        }

        if (buildSettings.TryGetProperty("SuiteVersion", out JsonElement versionEl) &&
            versionEl.ValueKind == JsonValueKind.String)
        {
            Version.TryParse(versionEl.GetString(), out version);
        }

        if (buildSettings.TryGetProperty("Revision", out JsonElement revisionEl) &&
            revisionEl.ValueKind == JsonValueKind.Number)
        {
            revisionEl.TryGetInt32(out revision);
        }
    }

    // Reads the Version/Revision out of whatever suite is already sitting in the cache directory (if any) and
    // reports whether it is strictly newer than the incoming payload — same UpgradeCode is implied since
    // extractRoot is keyed by it. Returns false (i.e. "go ahead and overwrite") whenever the cache is empty,
    // unparsable, or the incoming payload is the same or newer.
    private static bool IsCachedSuiteNewer(string extractRoot, Version? incomingVersion, int incomingRevision, out Version? cachedVersion, out int cachedRevision)
    {
        cachedVersion = null;
        cachedRevision = 0;

        if (incomingVersion == null)
            return false;

        if (!Directory.Exists(extractRoot))
            return false;

        string? cachedScfgPath = FindSuiteConfigPath(extractRoot);
        if (cachedScfgPath == null || !File.Exists(cachedScfgPath))
            return false;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(cachedScfgPath));
            if (!doc.RootElement.TryGetProperty("BuildSettings", out JsonElement buildSettings))
                return false;

            if (buildSettings.TryGetProperty("SuiteVersion", out JsonElement versionEl) &&
                versionEl.ValueKind == JsonValueKind.String)
            {
                Version.TryParse(versionEl.GetString(), out cachedVersion);
            }

            if (buildSettings.TryGetProperty("Revision", out JsonElement revisionEl) &&
                revisionEl.ValueKind == JsonValueKind.Number)
            {
                revisionEl.TryGetInt32(out cachedRevision);
            }
        }
        catch
        {
            // Unreadable/corrupt cached config — treat as not newer so the incoming payload replaces it.
            return false;
        }

        if (cachedVersion == null)
            return false;

        if (cachedVersion > incomingVersion) return true;
        if (cachedVersion == incomingVersion && cachedRevision > incomingRevision) return true;
        return false;
    }

    private static bool TryGetNewestInstalledSuiteExecutor(out string path, out Version version)
    {
        path = string.Empty;
        version = default!;

        if (!Directory.Exists(_installedExecutorDir))
            return false;

        var found = false;
        Version? newest = null;
        string? newestPath = null;

        foreach (var file in Directory.EnumerateFiles(_installedExecutorDir, "*.exe", SearchOption.TopDirectoryOnly))
        {
            Version? v = null;
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(file);
                if (Version.TryParse(fvi.FileVersion, out var parsed))
                    v = parsed;
            }
            catch
            {
                v = null;
            }

            if (v == null)
            {
                continue;
            }

            if (newest == null || v > newest)
            {
                newest = v;
                newestPath = file;
                found = true;
            }
        }

        if (!found || newest == null || newestPath == null)
            return false;

        path = newestPath;
        version = newest;
        return true;
    }

    private static void TryInstallOrUpdateSuiteExecutor(
        string extractRoot,
        string extractedSuiteExecutorPath,
        Version? installedVersion,
        Version? extractedVersion,
        bool forceUpdate = false)
    {
        try
        {
            if (extractedVersion == null)
                return;

            if (!forceUpdate && installedVersion != null && installedVersion >= extractedVersion)
                return;

            string installedExecutorDir = _installedExecutorDir;
            Directory.CreateDirectory(installedExecutorDir);

            string sourceDir = Path.GetDirectoryName(extractedSuiteExecutorPath) ?? extractRoot;

            // Copy all top-level files from the extracted executor directory, plus the `Popup` folder.
            // Ignore other suite payload folders.
            HashSet<string> allowedSubDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "runtimes"
            };

            string[] excludeDirs = Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly)
                .Where(d => !allowedSubDirs.Contains(Path.GetFileName(d)))
                .ToArray();

            NativeTools.CopyWithRoboCopy(
                sourceDir,
                installedExecutorDir,
                copyDirectoryItself: false,
                excludeDirectories: excludeDirs,
                excludeFiles: new[] { "*.scfg" });

            // Copy Popup\SuiteUserPopup.exe if it exists
            string popupSourceExe = Path.Combine(sourceDir, "Popup", "SuiteUserPopup.exe");
            if (File.Exists(popupSourceExe))
            {
                string popupDestExe = Path.Combine(installedExecutorDir, "SuiteUserPopup.exe");
                File.Copy(popupSourceExe, popupDestExe, overwrite: true);
            }

            // Copy Popup\SuiteProgressPopup.exe if it exists
            string progressPopupSourceExe = Path.Combine(sourceDir, "Popup", "SuiteProgressPopup.exe");
            if (File.Exists(progressPopupSourceExe))
            {
                string progressPopupDestExe = Path.Combine(installedExecutorDir, "SuiteProgressPopup.exe");
                File.Copy(progressPopupSourceExe, progressPopupDestExe, overwrite: true);
            }
        }
        catch
        {
            // Best-effort only. Installation may fail (e.g., no elevation). Ignore.
        }
    }

    // SuiteExecutor.exe's version is used to decide whether an update is needed overall, but the popup
    // exes are versioned independently of SuiteExecutor.exe, so a package can ship a newer popup while
    // SuiteExecutor.exe itself is unchanged. Compare hashes so those out-of-band popup updates aren't missed.
    private static bool IsInstalledPopupOutdated(string extractedSuiteExecutorPath)
    {
        string sourceDir = Path.GetDirectoryName(extractedSuiteExecutorPath) ?? string.Empty;

        string popupSourceExe = Path.Combine(sourceDir, "Popup", "SuiteUserPopup.exe");
        if (File.Exists(popupSourceExe))
        {
            string popupInstalledExe = Path.Combine(_installedExecutorDir, "SuiteUserPopup.exe");
            if (!File.Exists(popupInstalledExe) || IsExecutableMismatched(popupInstalledExe, popupSourceExe))
                return true;
        }

        string progressPopupSourceExe = Path.Combine(sourceDir, "Popup", "SuiteProgressPopup.exe");
        if (File.Exists(progressPopupSourceExe))
        {
            string progressPopupInstalledExe = Path.Combine(_installedExecutorDir, "SuiteProgressPopup.exe");
            if (!File.Exists(progressPopupInstalledExe) || IsExecutableMismatched(progressPopupInstalledExe, progressPopupSourceExe))
                return true;
        }

        return false;
    }

    private static bool IsExecutableMismatched(string installedExePath, string referenceExePath)
    {
        try
        {
            byte[] installedHash = SHA256.HashData(File.ReadAllBytes(installedExePath));
            byte[] referenceHash = SHA256.HashData(File.ReadAllBytes(referenceExePath));
            return !installedHash.SequenceEqual(referenceHash);
        }
        catch
        {
            return true;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("SuiteExecutor Usage:\n");
        Console.WriteLine("  Suite.exe [deploy|remove|rollback]");
        Console.WriteLine();
        Console.WriteLine("  /?, -?, ? - Show this help message");
    }

    private static string? PromptForSuiteAction()
    {
        Console.WriteLine("No SuiteAction was provided.");
        Console.Write("Enter SuiteAction [deploy|remove|rollback]: ");

        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string action = input.Trim();
        if (!string.Equals(action, "deploy", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "remove", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "rollback", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Invalid SuiteAction. Allowed values: deploy, remove, rollback.");
            return null;
        }

        return action;
    }

    // Locks the extract directory to SYSTEM + current user only, with inheritance broken so nothing from
    // the parent chain can grant a standard user access. Called on the EMPTY directory before extraction
    // so the payload is written into an already-hardened location. Child objects created afterwards
    // (i.e. the extracted files) inherit these SYSTEM/current-user rules.
    private static void HardenSuiteDirectory(string directoryPath)
    {
        DirectoryInfo dirInfo = new DirectoryInfo(directoryPath);
        DirectorySecurity dirSecurity = dirInfo.GetAccessControl();

        // Remove all inherited and explicit rules
        dirSecurity.SetAccessRuleProtection(true, false);
        AuthorizationRuleCollection existingRules = dirSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in existingRules)
        {
            dirSecurity.RemoveAccessRule(rule);
        }

        // Add SYSTEM full control with inheritance for child objects
        SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        dirSecurity.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        // Add current user full control (under SYSTEM this is SYSTEM; matters when an admin runs it interactively)
        WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
        if (currentIdentity.User != null)
        {
            dirSecurity.AddAccessRule(new FileSystemAccessRule(
                currentIdentity.User,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        dirInfo.SetAccessControl(dirSecurity);
        Console.WriteLine($"Locked directory '{directoryPath}' to SYSTEM and current user access");
    }

    // Grants BUILTIN\Users read & execute on the extracted Popup folder (the user-facing popup exes must
    // be launchable in the user's session). Called after extraction, once the Popup folder exists.
    private static void SetPopupPermissions(string extractRoot)
    {
        string popupPath = Path.Combine(extractRoot, "Popup");
        if (!Directory.Exists(popupPath))
        {
            Console.WriteLine($"No popup dir at: '{popupPath}', in this suite, so skipping set permissions");
            return;
        }

        DirectoryInfo popupDirInfo = new DirectoryInfo(popupPath);
        DirectorySecurity popupDirSecurity = popupDirInfo.GetAccessControl();

        // Remove all inherited and explicit rules
        popupDirSecurity.SetAccessRuleProtection(true, false);
        AuthorizationRuleCollection popupExistingRules = popupDirSecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in popupExistingRules)
        {
            popupDirSecurity.RemoveAccessRule(rule);
        }

        // SYSTEM full control
        SecurityIdentifier systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        popupDirSecurity.AddAccessRule(new FileSystemAccessRule(
            systemSid,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        // Current user full control
        WindowsIdentity currentIdentity = WindowsIdentity.GetCurrent();
        if (currentIdentity.User != null)
        {
            popupDirSecurity.AddAccessRule(new FileSystemAccessRule(
                currentIdentity.User,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        // Users group read & execute
        SecurityIdentifier usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        popupDirSecurity.AddAccessRule(new FileSystemAccessRule(
            usersSid,
            FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory | FileSystemRights.Read,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        popupDirInfo.SetAccessControl(popupDirSecurity);
        Console.WriteLine($"Set read/execute for Users on '{popupPath}'");
    }
}