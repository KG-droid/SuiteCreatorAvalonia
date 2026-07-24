using System.Diagnostics.Eventing.Reader;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;
using Windows.Management.Deployment;

namespace SuiteTools
{
    public class MSIx : IDisposable
    {
        public class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            public static extern uint PackageFamilyNameFromId([In] ref NativeHelpers.PACKAGE_ID packageId, ref uint packageFamilyNameLength, [Out] StringBuilder packageFamilyName);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            public static extern uint PackageFullNameFromId([In] ref NativeHelpers.PACKAGE_ID packageId, ref uint packageFullNameLength, [Out] StringBuilder packageFullName);
        }
        public class NativeHelpers
        {
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
            public struct PACKAGE_ID
            {
                public uint reserved;
                public uint processorArchitecture;
                public PACKAGE_VERSION version;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string name;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string publisher;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string resourceId;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string publisherId;
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct PACKAGE_VERSION
            {
                [FieldOffset(0)]
                public ulong Version;
                [FieldOffset(0)]
                public ushort Revision;
                [FieldOffset(2)]
                public ushort Build;
                [FieldOffset(4)]
                public ushort Minor;
                [FieldOffset(6)]
                public ushort Major;
            }
        }
        public enum ProcessorArchitecture
        {
            Arm = 5,
            Arm64 = 12,
            Neutral = 11,
            Unknown = 65535,
            X64 = 9,
            X86 = 0,
            X86OnArm64 = 14
        }

        // Resource management fields
        private ZipArchive? _streamArchive;
        private MemoryStream? _msixStream;
        public XmlDocument appManifest = new XmlDocument();
        public XmlNamespaceManager? nsmgr;
        private bool _disposed = false;

        // Not thread-safe. If used from multiple threads, synchronize access.

        public MSIx(string msixPath)
        {
            Open(msixPath);
        }
        public void Dispose()
        {
            if (_disposed) return;
            if (_streamArchive != null)
            {
                _streamArchive.Dispose();
                _streamArchive = null;
            }
            if (_msixStream != null)
            {
                _msixStream.Dispose();
                _msixStream = null;
            }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
        private void Open(string msixPath)
        {
            if (!File.Exists(msixPath))
            {
                throw new Exception("The file path specified does not exist.");
            }
            if (!Path.GetExtension(msixPath).Equals(".msix", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("The file path specified is not an MSIx.");
            }
            _msixStream = new MemoryStream(File.ReadAllBytes(msixPath));
            _streamArchive = new ZipArchive(_msixStream, ZipArchiveMode.Read);
            ZipArchiveEntry? appManifestStreamEntry = _streamArchive.GetEntry("AppxManifest.xml");
            if (appManifestStreamEntry == null)
            {
                throw new Exception("Unable to find an AppxManifest.xml in the provided MSIx. Please check it is not corrupted.");
            }
            using (Stream stream = appManifestStreamEntry.Open())
            {
                appManifest.Load(stream);
                nsmgr = new XmlNamespaceManager(appManifest.NameTable);
                nsmgr.AddNamespace("default", appManifest.DocumentElement.NamespaceURI);
            }
        }
        private XmlNode GetIdentityNode()
        {
            var node = appManifest.SelectSingleNode("//default:Identity", nsmgr);
            if (node == null)
                throw new Exception("Cannot find the Identity node in the MSIx AppxManifest. Please check it is not corrupted.");
            return node;
        }
        private string GetIdentityAttribute(string attributeName)
        {
            var node = GetIdentityNode();
            var attr = node.Attributes?[attributeName];
            if (attr == null || string.IsNullOrEmpty(attr.Value))
                throw new Exception($"Cannot find a '{attributeName}' attribute within the MSIx AppxManifest Identity node. Please check it is not corrupted.");
            return attr.Value;
        }
        public string Name => GetIdentityAttribute("Name");
        public string VersionString => GetIdentityAttribute("Version");
        public string Architecture => GetIdentityAttribute("ProcessorArchitecture");
        public string Publisher => GetIdentityAttribute("Publisher");
        public string PackageFamilyName
        {
            get
            {
                string publisher = Publisher;
                string name = Name;
                if (string.IsNullOrEmpty(publisher) || string.IsNullOrEmpty(name))
                {
                    throw new Exception("Cannot find either the Publisher or Name attribute within the MSIx AppxManifest Identity node. Please check it is not corrupted.");
                }
                string? packageFamilyName = null;
                var packageId = new NativeHelpers.PACKAGE_ID
                {
                    name = name,
                    publisher = publisher,
                    resourceId = null!,
                    publisherId = null!,
                    reserved = 0,
                    processorArchitecture = 0,
                    version = new NativeHelpers.PACKAGE_VERSION()
                };
                uint packageFamilyNameLength = 0;
                // First get the length of the Package Name -> Pass null as Output Buffer
                uint result = NativeMethods.PackageFamilyNameFromId(ref packageId, ref packageFamilyNameLength, null!);
                if (result == 122 && packageFamilyNameLength > 0) // ERROR_INSUFFICIENT_BUFFER
                {
                    StringBuilder packageFamilyNameBuilder = new StringBuilder((int)packageFamilyNameLength);
                    result = NativeMethods.PackageFamilyNameFromId(ref packageId, ref packageFamilyNameLength, packageFamilyNameBuilder);
                    if (result == 0)
                    {
                        packageFamilyName = packageFamilyNameBuilder.ToString();
                    }
                    else
                    {
                        throw new Exception($"Failed to get PackageFamilyName from native method. Error code: {result}");
                    }
                }
                else
                {
                    throw new Exception($"Failed to get PackageFamilyName buffer size from native method. Error code: {result}");
                }
                return packageFamilyName!;
            }
        }
        public string PackageFullName
        {
            get
            {
                string publisher = Publisher;
                string name = Name;
                string versionStr = VersionString;
                string archStr = Architecture;
                if (string.IsNullOrEmpty(publisher) || string.IsNullOrEmpty(name))
                {
                    throw new Exception("Cannot find either the Publisher or Name attribute within the MSIx AppxManifest Identity node. Please check it is not corrupted.");
                }
                Version version = new Version(versionStr);
                NativeHelpers.PACKAGE_VERSION packageVersion = new NativeHelpers.PACKAGE_VERSION
                {
                    Major = (ushort)version.Major,
                    Minor = (ushort)version.Minor,
                    Build = (ushort)version.Build,
                    Revision = (ushort)version.Revision
                };
                ProcessorArchitecture parsedArch = (ProcessorArchitecture)Enum.Parse(typeof(ProcessorArchitecture), archStr, true);
                uint architecture = (uint)parsedArch;
                string? packageFullName = null;
                var packageId = new NativeHelpers.PACKAGE_ID
                {
                    name = name,
                    publisher = publisher,
                    version = packageVersion,
                    processorArchitecture = architecture,
                    resourceId = null!,
                    publisherId = null!,
                    reserved = 0
                };
                uint packageFullNameLength = 0;
                uint result = NativeMethods.PackageFullNameFromId(ref packageId, ref packageFullNameLength, null!);
                if (result == 122 && packageFullNameLength > 0) // ERROR_INSUFFICIENT_BUFFER
                {
                    StringBuilder packageFullNameBuilder = new StringBuilder((int)packageFullNameLength);
                    result = NativeMethods.PackageFullNameFromId(ref packageId, ref packageFullNameLength, packageFullNameBuilder);
                    if (result == 0)
                    {
                        packageFullName = packageFullNameBuilder.ToString();
                    }
                    else
                    {
                        throw new Exception($"Failed to get PackageFullName from native method. Error code: {result}");
                    }
                }
                else
                {
                    throw new Exception($"Failed to get PackageFullName buffer size from native method. Error code: {result}");
                }
                return packageFullName!;
            }
        }
    }

    public class MSIxInstallOptions
    {
        public bool ForceAppShutdown { get; set; }
        public bool ForceUpdateFromAnyVersion { get; set; }
        public bool DeferRegistrationWhenPackagesAreInUse { get; set; }
    }

    public static class MSIxTools
    {
        // HRESULT_FROM_WIN32 values for the AppX/MSIX deployment error range (winerror.h ERROR_INSTALL_*/ERROR_PACKAGE_*).
        // WinRT deployment failures often surface as a COMException with an empty Message, leaving only the HRESULT -
        // this turns that HRESULT into the symbolic name so the log is self-describing without an Event Viewer trip.
        private static readonly Dictionary<int, string> AppxHResultNames = new()
        {
            { unchecked((int)0x80073CF0), "ERROR_INSTALL_OPEN_PACKAGE_FAILED" },
            { unchecked((int)0x80073CF1), "ERROR_INSTALL_PACKAGE_NOT_FOUND" },
            { unchecked((int)0x80073CF2), "ERROR_INSTALL_INVALID_PACKAGE" },
            { unchecked((int)0x80073CF3), "ERROR_INSTALL_RESOLVE_DEPENDENCY_FAILED" },
            { unchecked((int)0x80073CF4), "ERROR_INSTALL_OUT_OF_DISK_SPACE" },
            { unchecked((int)0x80073CF5), "ERROR_INSTALL_NETWORK_FAILURE" },
            { unchecked((int)0x80073CF6), "ERROR_INSTALL_REGISTRATION_FAILURE" },
            { unchecked((int)0x80073CF7), "ERROR_INSTALL_DEREGISTRATION_FAILURE" },
            { unchecked((int)0x80073CF8), "ERROR_INSTALL_CANCEL" },
            { unchecked((int)0x80073CF9), "ERROR_INSTALL_FAILED" },
            { unchecked((int)0x80073CFA), "ERROR_REMOVE_FAILED" },
            { unchecked((int)0x80073CFB), "ERROR_PACKAGE_ALREADY_EXISTS" },
            { unchecked((int)0x80073CFC), "ERROR_NEEDS_REMEDIATION" },
            { unchecked((int)0x80073CFD), "ERROR_INSTALL_PREREQUISITE_FAILED" },
            { unchecked((int)0x80073CFE), "ERROR_PACKAGE_REPOSITORY_CORRUPTED" },
            { unchecked((int)0x80073CFF), "ERROR_INSTALL_POLICY_FAILURE" },
            { unchecked((int)0x80073D00), "ERROR_PACKAGE_UPDATING" },
            { unchecked((int)0x80073D01), "ERROR_DEPLOYMENT_BLOCKED_BY_POLICY" },
            { unchecked((int)0x80073D02), "ERROR_PACKAGES_IN_USE" },
            { unchecked((int)0x80073D03), "ERROR_RECOVERY_FILE_CORRUPT" },
            { unchecked((int)0x80073D04), "ERROR_INSUFFICIENT_APPLICATION_RESOURCES" },
            { unchecked((int)0x80073D05), "ERROR_APPLICATION_NOT_LICENSED" },
            { unchecked((int)0x80073D06), "ERROR_INSTALL_FIREWALL_SERVICE_NOT_RUNNING" },
            { unchecked((int)0x80073D07), "ERROR_PACKAGE_NOT_SUPPORTED_ON_FILESYSTEM" },
            { unchecked((int)0x80073D08), "ERROR_PACKAGE_MOVE_FAILED" },
            { unchecked((int)0x80073D09), "ERROR_INSTALL_VOLUME_NOT_EMPTY" },
            { unchecked((int)0x80073D0A), "ERROR_INSTALL_VOLUME_OFFLINE" },
            { unchecked((int)0x80073D0B), "ERROR_INSTALL_VOLUME_CORRUPT" },
            { unchecked((int)0x80073D0C), "ERROR_NEEDS_REGISTRATION" },
            { unchecked((int)0x80073D0D), "ERROR_INSTALL_WRONG_PROCESSOR_ARCHITECTURE" },
            { unchecked((int)0x80073D0E), "ERROR_DEV_SIDELOAD_LIMIT_EXCEEDED" },
            { unchecked((int)0x80073D0F), "ERROR_INSTALL_OPTIONAL_PACKAGE_APPLICATIONID_NOT_UNIQUE" },
        };

        private static string DescribeHResult(int hResult)
        {
            string hex = $"0x{hResult:X8}";
            return AppxHResultNames.TryGetValue(hResult, out string? name) ? $"{name} ({hex})" : hex;
        }

        // AppX deployment failures write the real, human-readable reason to these operational logs -
        // the WinRT COMException thrown back to the caller usually doesn't carry it.
        private static readonly string[] AppxEventLogNames =
        {
            "Microsoft-Windows-AppXDeploymentServer/Operational",
            "Microsoft-Windows-AppXDeployment/Operational"
        };

        // A package family name is "Name_PublisherId" but deployment event text embeds the full name
        // "Name_Version_Arch_Resource__PublisherId" - the family name is never a contiguous substring of
        // that, so match on the Name and PublisherId pieces independently instead of the whole string.
        private static bool DescriptionMatchesPackage(string description, string packageIdentifier)
        {
            if (description.Contains(packageIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int lastUnderscore = packageIdentifier.LastIndexOf('_');
            if (lastUnderscore <= 0 || lastUnderscore >= packageIdentifier.Length - 1)
            {
                return false;
            }

            string namePart = packageIdentifier[..lastUnderscore];
            string publisherIdPart = packageIdentifier[(lastUnderscore + 1)..];
            return description.Contains(namePart, StringComparison.OrdinalIgnoreCase)
                && description.Contains(publisherIdPart, StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryFindAppxDeploymentEventMessage(string packageIdentifier, DateTime notBeforeUtc)
        {
            const int maxEventsToExamine = 10;
            foreach (string logName in AppxEventLogNames)
            {
                try
                {
                    string queryString = $"*[System[(Level=2 or Level=3) and TimeCreated[@SystemTime>='{notBeforeUtc:yyyy-MM-ddTHH:mm:ss.fffZ}']]]";
                    EventLogQuery query = new(logName, PathType.LogName, queryString) { ReverseDirection = true };
                    using EventLogReader reader = new(query);

                    // Deployments are rarely concurrent, so if nothing in the window names our package
                    // (e.g. a dependency-resolution failure names the missing dependency, not our package),
                    // the most recent error in the same log right after our call failed is still almost
                    // certainly the real cause - fall back to it rather than reporting nothing.
                    string? mostRecentUnmatched = null;
                    EventRecord? record;
                    int examined = 0;
                    while (examined < maxEventsToExamine && (record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            examined++;
                            string description;
                            try
                            {
                                description = record.FormatDescription() ?? string.Empty;
                            }
                            catch
                            {
                                // Some providers require message resources that aren't resolvable locally.
                                continue;
                            }

                            if (string.IsNullOrEmpty(description))
                            {
                                continue;
                            }

                            string formatted = $"{logName}: {description.Trim()}";
                            if (DescriptionMatchesPackage(description, packageIdentifier))
                            {
                                return formatted;
                            }
                            mostRecentUnmatched ??= formatted;
                        }
                    }

                    if (mostRecentUnmatched != null)
                    {
                        return mostRecentUnmatched;
                    }
                }
                catch
                {
                    // Log may not exist, be disabled, or be inaccessible to this process - fall through silently,
                    // the HRESULT-based message is still reported to the caller either way.
                }
            }
            return null;
        }

        private static async Task<string?> TryGetAppxDeploymentEventDetailAsync(string packageIdentifier, DateTime notBeforeUtc)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string? detail = TryFindAppxDeploymentEventMessage(packageIdentifier, notBeforeUtc);
                if (detail != null) return detail;
                await Task.Delay(750);
            }
            return null;
        }

        private static string? TryGetAppxDeploymentEventDetailSync(string packageIdentifier, DateTime notBeforeUtc)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string? detail = TryFindAppxDeploymentEventMessage(packageIdentifier, notBeforeUtc);
                if (detail != null) return detail;
                Thread.Sleep(750);
            }
            return null;
        }

        // Stash a useful description (operation + package + HRESULT + matching event log entry, if any) in
        // ex.Data["ErrorText"] so callers further up the stack (see Suite.Execution.Package.cs) have something
        // to log, even when the WinRT COMException itself carries an empty Message.
        private static async Task RunDeploymentOperationAsync(Func<Task> operation, string operationName, string packageIdentifier)
        {
            DateTime startUtc = DateTime.UtcNow;
            try
            {
                await operation();
            }
            catch (COMException ex)
            {
                string baseMessage = string.IsNullOrWhiteSpace(ex.Message) ? DescribeHResult(ex.HResult) : $"{ex.Message} ({DescribeHResult(ex.HResult)})";
                string? eventDetail = await TryGetAppxDeploymentEventDetailAsync(packageIdentifier, startUtc);
                string detail = eventDetail != null ? $"{baseMessage} -- {eventDetail}" : baseMessage;
                ex.Data["ErrorText"] = $"{operationName} failed for package '{packageIdentifier}': {detail}";
                throw;
            }
        }

        private static void RunDeploymentOperationSync(Action operation, string operationName, string packageIdentifier)
        {
            DateTime startUtc = DateTime.UtcNow;
            try
            {
                operation();
            }
            catch (COMException ex)
            {
                string baseMessage = string.IsNullOrWhiteSpace(ex.Message) ? DescribeHResult(ex.HResult) : $"{ex.Message} ({DescribeHResult(ex.HResult)})";
                string? eventDetail = TryGetAppxDeploymentEventDetailSync(packageIdentifier, startUtc);
                string detail = eventDetail != null ? $"{baseMessage} -- {eventDetail}" : baseMessage;
                ex.Data["ErrorText"] = $"{operationName} failed for package '{packageIdentifier}': {detail}";
                throw;
            }
        }

        private static List<string> GetActiveUserSids()
        {
            List<UserTools.UserExtensions.UserSessionInfo>? sessions = UserTools.UserExtensions.GetUserSessions();
            if (sessions == null || sessions.Count == 0)
            {
                return new List<string>();
            }

            HashSet<string> userSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UserTools.UserExtensions.UserSessionInfo session in sessions)
            {
                if (string.IsNullOrWhiteSpace(session.UserName) || session.State != UserTools.NativeHelpers.WTS_CONNECTSTATE_CLASS.WTSActive)
                {
                    continue;
                }

                string? sid = session.UserSid?.ToString();
                if (!string.IsNullOrWhiteSpace(sid))
                {
                    userSids.Add(sid);
                }
            }

            return userSids.ToList();
        }

        private static bool IsPackageInstalledForUser(PackageManager packageManager, string userSid, string packageIdentifier)
        {
            try
            {
                Windows.ApplicationModel.Package? fullNameMatch = packageManager.FindPackageForUser(userSid, packageIdentifier);
                if (fullNameMatch != null)
                {
                    return true;
                }
            }
            catch
            {
                // Fall through to family-name lookup.
            }

            IEnumerable<Windows.ApplicationModel.Package> familyMatches = packageManager.FindPackagesForUser(userSid, packageIdentifier);
            foreach (Windows.ApplicationModel.Package _ in familyMatches)
            {
                return true;
            }

            return false;
        }

        public static async Task InstallMSIxPackageAsync(string msixPath, MSIxInstallOptions? installOptions, List<string>? dependentsPaths)
        {
            if (string.IsNullOrWhiteSpace(msixPath) || !Path.Exists(msixPath))
            {
                throw new FileNotFoundException($"The MSIx path specified does not exist", msixPath);
            }

            List<string> resolvedDependentPaths = dependentsPaths ?? new List<string>();
            foreach (string dependent in resolvedDependentPaths)
            {
                if (string.IsNullOrWhiteSpace(dependent) || !Path.Exists(dependent))
                {
                    throw new FileNotFoundException($"One of the dependent MSIx paths specified does not exist", dependent);
                }
            }

            string? packageFamilyName = null;
            using (MSIx mSIx = new MSIx(msixPath))
            {
                packageFamilyName = mSIx.PackageFamilyName;
            }
            if (string.IsNullOrWhiteSpace(packageFamilyName))
            {
                throw new Exception("Failed to determine the PackageFamilyName from the provided MSIx file. Please check the AppxManifest in this MSIx is not corrupted.");
            }

            DeploymentOptions deploymentOptions = DeploymentOptions.None;
            if (installOptions != null)
            {
                if (installOptions.ForceAppShutdown)
                    deploymentOptions |= DeploymentOptions.ForceApplicationShutdown;
                if (installOptions.ForceUpdateFromAnyVersion)
                    deploymentOptions |= DeploymentOptions.ForceUpdateFromAnyVersion;
            }

            PackageManager packageManager = new PackageManager();
            Uri[]? dependentUris = resolvedDependentPaths.Count > 0
                ? resolvedDependentPaths.ConvertAll(p => new Uri(Path.GetFullPath(p))).ToArray()
                : null;

            // AddPackageAsync is a per-user "add" operation and Windows explicitly refuses to run it as
            // Local System ("Local System account is not allowed to perform this operation"). Stage +
            // ProvisionPackageForAllUsersAsync is the SYSTEM-safe, machine-wide equivalent - it's the same
            // mechanism DISM/MDM tooling uses - and it also covers any user created after this point, which
            // AddPackageAsync never did anyway.
            // Stage only copies package files into the repo - it doesn't register or replace anything for
            // any user yet - so ForceApplicationShutdown/ForceUpdateFromAnyVersion aren't valid options
            // here ("Invalid deployment options were provided to the function"); they belong on Register,
            // which is where a package actually gets activated/replaced for a user.
            await RunDeploymentOperationAsync(
                () => packageManager.StagePackageAsync(new Uri(Path.GetFullPath(msixPath)), dependentUris, DeploymentOptions.None).AsTask(),
                "StagePackageAsync",
                packageFamilyName);
            await RunDeploymentOperationAsync(
                () => packageManager.ProvisionPackageForAllUsersAsync(packageFamilyName).AsTask(),
                "ProvisionPackageForAllUsersAsync",
                packageFamilyName);

            // Ensure it is registered for all active users so it's usable immediately, not just at their
            // next sign-in. Provisioning above already guarantees it for everyone (including future users)
            // regardless of whether this step runs, so skipping it when nobody is signed in yet - e.g.
            // during unattended imaging/OOBE - is not fatal; RunAsAllUsersImpersonated throws if there are
            // no active sessions, so check first rather than treat that as an install failure.
            // RunAsAllUsersImpersonated requires SeTcbPrivilege (SYSTEM only); if the caller is a
            // plain admin without that right (e.g. interactive testing), register for the current user only.
            if (UserTools.ProcessExtensions.HasSeTcbPrivilege())
            {
                if (GetActiveUserSids().Count > 0)
                {
                    UserTools.ProcessExtensions.RunAsAllUsersImpersonated(() =>
                    {
                        RunDeploymentOperationSync(
                            () => packageManager.RegisterPackageByFamilyNameAsync(
                                packageFamilyName,
                                null,
                                deploymentOptions,
                                null,
                                null).AsTask().GetAwaiter().GetResult(),
                            "RegisterPackageByFamilyNameAsync",
                            packageFamilyName);
                        return true;
                    });
                }
            }
            else
            {
                await RunDeploymentOperationAsync(
                    () => packageManager.RegisterPackageByFamilyNameAsync(
                        packageFamilyName,
                        null,
                        deploymentOptions,
                        null,
                        null).AsTask(),
                    "RegisterPackageByFamilyNameAsync",
                    packageFamilyName);
            }
        }

        public static async Task UninstallMSIxPackageAsync(string pfn, bool isFullNameRemoval)
        {
            if (string.IsNullOrWhiteSpace(pfn)) throw new ArgumentException("Package family name or full name must be provided.", nameof(pfn));
            PackageManager packageManager = new PackageManager();
            string familyName = isFullNameRemoval ? GetFamilyNameFromFullName(pfn) : pfn;

            if (isFullNameRemoval)
            {
                await RunDeploymentOperationAsync(() => packageManager.RemovePackageAsync(pfn).AsTask(), "RemovePackageAsync", pfn);
                foreach (Windows.ApplicationModel.Package provisioned in packageManager.FindProvisionedPackages().Where(p => p.Id.FullName.Equals(pfn, StringComparison.OrdinalIgnoreCase)))
                {
                    await RunDeploymentOperationAsync(() => packageManager.DeprovisionPackageForAllUsersAsync(provisioned.Id.FamilyName).AsTask(), "DeprovisionPackageForAllUsersAsync", provisioned.Id.FamilyName);
                }
            }
            else
            {
                IEnumerable<Windows.ApplicationModel.Package> packages = packageManager.FindPackages(familyName);
                foreach (Windows.ApplicationModel.Package pkg in packages)
                {
                    await RunDeploymentOperationAsync(() => packageManager.RemovePackageAsync(pkg.Id.FullName, RemovalOptions.RemoveForAllUsers).AsTask(), "RemovePackageAsync", pkg.Id.FullName);
                    foreach (Windows.ApplicationModel.Package provisioned in packageManager.FindProvisionedPackages().Where(p => p.Id.FullName.Equals(pfn, StringComparison.OrdinalIgnoreCase)))
                    {
                        await RunDeploymentOperationAsync(() => packageManager.DeprovisionPackageForAllUsersAsync(provisioned.Id.FamilyName).AsTask(), "DeprovisionPackageForAllUsersAsync", provisioned.Id.FamilyName);
                    }
                }
            }
        }


        public static bool IsMSIxReadyForAllUsers(string? packageIdentifier)
        {
            if (string.IsNullOrWhiteSpace(packageIdentifier))
            {
                return false;
            }

            List<string> userSids = GetActiveUserSids();
            if (userSids.Count == 0)
            {
                return false;
            }

            PackageManager packageManager = new PackageManager();
            foreach (string userSid in userSids)
            {
                bool isInstalledForUser = IsPackageInstalledForUser(packageManager, userSid, packageIdentifier);
                if (!isInstalledForUser)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsMSIReadyForAnyUser(string? packageIdentifier)
        {
            if (string.IsNullOrWhiteSpace(packageIdentifier))
            {
                return false;
            }

            List<string> userSids = GetActiveUserSids();
            if (userSids.Count == 0)
            {
                return false;
            }

            PackageManager packageManager = new PackageManager();
            foreach (string userSid in userSids)
            {
                bool isInstalledForUser = IsPackageInstalledForUser(packageManager, userSid, packageIdentifier);
                if (isInstalledForUser)
                {
                    return true;
                }
            }

            return false;
        }

        public static IEnumerable<Windows.ApplicationModel.Package> GetInstalledMSIxPackagesByFamilyName(string packageFamilyName)
        {
            if (string.IsNullOrWhiteSpace(packageFamilyName))
                throw new ArgumentException("PackageFamilyName must not be null or empty.", nameof(packageFamilyName));

            PackageManager packageManager = new PackageManager();
            return packageManager.FindPackages(packageFamilyName);
        }

        private static string GetFamilyNameFromFullName(string packageFullName)
        {
            uint length = 0;
            uint sizeResult = NativeMethods.PackageFamilyNameFromFullName(packageFullName, ref length, null!);
            if (sizeResult != 122 || length == 0) // ERROR_INSUFFICIENT_BUFFER expected
                throw new Exception($"Failed to get PackageFamilyName buffer size from PackageFullName. Error code: {sizeResult}");
            StringBuilder sb = new StringBuilder((int)length);
            uint result = NativeMethods.PackageFamilyNameFromFullName(packageFullName, ref length, sb);
            if (result != 0)
                throw new Exception($"Failed to get PackageFamilyName from PackageFullName. Error code: {result}");
            return sb.ToString();
        }

        private class NativeMethods
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            public static extern uint PackageFamilyNameFromFullName([MarshalAs(UnmanagedType.LPWStr)] string packageFullName, ref uint packageFamilyNameLength, [Out] StringBuilder packageFamilyName);
        }
    }
}
