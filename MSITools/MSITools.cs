using Microsoft.Win32;
using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Deployment.WindowsInstaller;


namespace SuiteTools
{
    public class MSITools
    {
        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        private static extern int MsiEnumRelatedProducts(string lpUpgradeCode, int dwReserved, int iProductIndex, StringBuilder lpProductCode);

        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        private static extern int MsiEnumProducts(int iProductIndex, StringBuilder lpProductCode);

        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        private static extern int MsiGetProductInfoEx(
            string productCode,
            string? userSid,
            MsiInstallContext context,
            string property,
            StringBuilder valueBuf,
            ref int len);

        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        private static extern int MsiEnumProductsEx(
            string szProductCode,
            string szUserSid,
            int dwContext,
            int dwIndex,
            StringBuilder szInstalledProductCode,
            out int pdwInstalledContext,
            StringBuilder szSid,
            ref int pcchSid
        );

        [DllImport("msi.dll", CharSet = CharSet.Auto)]
        private static extern int MsiQueryProductState(string szProductCode);

        public class MSIResult
        {
            public string CommandRun { get; set; } = string.Empty;
            public bool Success { get; set; }
            public string? ErrorMessage { get; set; }
            public int ExitCode { get; set; }
        }

        public class MSIProp
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public MSIProp(string name, string value)
            {
                Name = name;
                Value = value;
            }
        }

        public class MSISummaryInfo
        {
            public string? Title { get; set; }
            public string? Subject { get; set; }
            public string? Author { get; set; }
            public string? Keywords { get; set; }
            public string? Comments { get; set; }
            public string? Template { get; set; }
            public bool Is64Bit()
            {
                return Template != null && Template.Contains("x64", StringComparison.OrdinalIgnoreCase);
            }
        }

        public class MSIProductInfo
        {
            public string? DisplayName { get; set; }
            public string? Publisher { get; set; }
            public Version? ProductVersion { get; set; }
            public string? ProductCode { get; set; }
            public Architecture? Architecture { get; set; }
            public MSIState? State { get; set; }
            public MsiInstallContext? Context { get; set; }
            public List<string>? SIDs { get; set; }
            public string? UninstallString { get; set; }
        }

        public enum MSIState
        {
            Advertised = 1,
            Installed = 5,
        }

        public static string BuildInstallArguments(
            string msiPath,
            string? transformsPath = null,
            bool? secureTransforms = false,
            string? patchPath = null,
            List<MSIProp>? properties = null,
            string? logPath = null
        )
        {
            var args = $"/i \"{msiPath}\" /q";
            if (!string.IsNullOrWhiteSpace(transformsPath))
            {
                args += secureTransforms != null && secureTransforms.Value
                    ? $" TRANSFORMS=\"{transformsPath}\" SECURE=1"
                    : $" TRANSFORMS=\"{transformsPath}\"";
            }
            if (!string.IsNullOrWhiteSpace(patchPath))
            {
                args += $" PATCH=\"{patchPath}\"";
            }
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                args += $" /l*v \"{logPath}\"";
            }
            if (properties != null && properties.Count > 0)
            {
                foreach (var prop in properties)
                {
                    if (!string.IsNullOrWhiteSpace(prop.Name) && !string.IsNullOrWhiteSpace(prop.Value))
                    {
                        args += $" {prop.Name}={prop.Value}";
                    }
                }
            }
            return args;
        }

        public static MSIResult InstallMSI(
            string msiPath,
            string? transformsPath = null,
            bool? secureTransforms = false,
            string? patchPath = null,
            List<MSIProp>? properties = null,
            string? logPath = null
        )
        {
            var result = new MSIResult();
            if (string.IsNullOrWhiteSpace(msiPath))
            {
                result.Success = false;
                result.ErrorMessage = "MSI file path is not specified.";
                return result;
            }
            try
            {
                var args = BuildInstallArguments(msiPath, transformsPath, secureTransforms, patchPath, properties, logPath);

                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = GetMSIExecPath(),
                        Arguments = args,
                        WorkingDirectory = Path.GetDirectoryName(msiPath),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                result.CommandRun = $"msiexec.exe {args}";
                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                result.ErrorMessage = process.ExitCode == 0 ? null :
                    $"MSI installation failed with exit code {process.ExitCode}. Error: {MSITools.GetMSIErrorDescription(process.ExitCode)}";

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Error installing MSI package: {ex.Message}";
            }
            return result;
        }

        public static MSIResult UninstallMSI(string removalCode, string? logPath, List<MSIProp>? properties = null, string? additionalParams = null)
        {
            var result = new MSIResult();
            if (string.IsNullOrWhiteSpace(removalCode))
            {
                result.Success = false;
                result.ErrorMessage = "MSI removal code was not specified, please specify a Product or Upgrade code";
                return result;
            }
            try
            {
                string formattedRemovalCode = removalCode.StartsWith('{') && removalCode.EndsWith('}')
                    ? removalCode
                    : $"{{{removalCode}}}";
                string args = $"/x {formattedRemovalCode} /q";
                if (properties != null && properties.Count > 0)
                {
                    foreach (MSIProp prop in properties)
                    {
                        if (!string.IsNullOrWhiteSpace(prop.Name) && !string.IsNullOrWhiteSpace(prop.Value))
                            args += $" {prop.Name}={prop.Value}";
                    }
                }
                if (!string.IsNullOrWhiteSpace(additionalParams))
                {
                    args += $" {additionalParams}";
                }
                if (!string.IsNullOrWhiteSpace(logPath))
                {
                    args += $" /l*v \"{logPath}\"";
                }
                result.CommandRun = $"msiexec.exe {args}";
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = GetMSIExecPath(),
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();
                result.ExitCode = process.ExitCode;
                if (process.ExitCode == 0 || process.ExitCode == 1605)
                {
                    result.Success = true;
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"MSI uninstallation failed with exit code {process.ExitCode}. Error: {MSITools.GetMSIErrorDescription(process.ExitCode)}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Error uninstalling MSI package: {ex.Message}";
            }
            return result;
        }

        public static MSIResult UninstallMSIFamily(string upgradeCode, string? logPath, List<MSIProp>? properties = null, string? additionalParams = null)
        {
            var result = new MSIResult();
            if (string.IsNullOrWhiteSpace(upgradeCode))
            {
                result.Success = false;
                result.ErrorMessage = "MSI removal code was not specified, please specify a Product or Upgrade code";
                return result;
            }
            try
            {
                List<MSIProductInfo>? familyProducts = GetRelatedMSIProducts(upgradeCode);
                if (familyProducts != null && familyProducts.Count > 0)
                {
                    foreach (MSIProductInfo product in familyProducts)
                    {
                        string code = product.ProductCode ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            result.Success = false;
                            result.ErrorMessage = $"Product with missing ProductCode found in upgrade family {upgradeCode}. Skipping.";
                            continue;
                        }
                        if (!code.StartsWith("{")) code = "{" + code;
                        if (!code.EndsWith("}")) code = code + "}";
                        string args = $"/x {code} /q";
                        if (properties != null && properties.Count > 0)
                        {
                            foreach (MSIProp prop in properties)
                            {
                                if (!string.IsNullOrWhiteSpace(prop.Name) && !string.IsNullOrWhiteSpace(prop.Value))
                                    args += $" {prop.Name}={prop.Value}";
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(additionalParams))
                        {
                            args += $" {additionalParams}";
                        }
                        if (!string.IsNullOrWhiteSpace(logPath))
                        {
                            args += $" /l*v \"{logPath}\"";
                        }
                        result.CommandRun += $"msiexec.exe {args}\n";
                        var process = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = GetMSIExecPath(),
                                Arguments = args,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        process.Start();
                        process.WaitForExit();
                        result.ExitCode = process.ExitCode;
                        if (process.ExitCode == 0 || process.ExitCode == 1605)
                        {
                            result.Success = true;
                        }
                        else
                        {
                            result.Success = false;
                            result.ErrorMessage = $"MSI uninstallation failed for product {product.ProductCode} with exit code {process.ExitCode}. Error: {MSITools.GetMSIErrorDescription(process.ExitCode)}";
                            break;
                        }
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "No related products found for the given upgrade code.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Error uninstalling MSI package: {ex.Message}";
            }
            return result;
        }

        public static void AdvertiseMSI(
            string msiPath,
            string? transformsPath = null
        )
        {
            if (string.IsNullOrWhiteSpace(msiPath))
                throw new ArgumentException("MSI file path is not specified.", nameof(msiPath));

            // Advertise via msiexec.exe as a separate process rather than calling MsiAdvertiseProduct
            // in-process. The in-process call corrupts this process's ability to do CreateProcessAsUser
            // for the remainder of its lifetime (just for certain exes for some reason? like msiexec) - RevertToSelf and
            // re-enabling SeAssignPrimaryTokenPrivilege/SeIncreaseQuotaPrivilege on the process token do
            // not fix it, so the damage goes deeper than thread impersonation or disabled privileges.
            string args = $"/jm \"{msiPath}\" /q";
            if (!string.IsNullOrWhiteSpace(transformsPath))
            {
                args += $" TRANSFORMS=\"{transformsPath}\"";
            }

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = GetMSIExecPath(),
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(msiPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new Exception($"Failed to advertise MSI for user. Error code: {process.ExitCode} ({GetMSIErrorDescription(process.ExitCode)})");
            }
        }

        public static string GetMSIExecPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
        }

        public static string GetMSIErrorDescription(int code)
        {
            return code switch
            {
                0 => "The action completed successfully.",
                13 => "The data is invalid.",
                87 => "One of the parameters was invalid.",
                120 => "Custom action attempted to call a function that can't be called from custom actions. ERROR_CALL_NOT_IMPLEMENTED.",
                1259 => "Product might be incompatible with the OS. User chose not to try installation.",
                1601 => "Windows Installer service couldn't be accessed. Contact support personnel.",
                1602 => "The user canceled installation.",
                1603 => "A fatal error occurred during installation.",
                1604 => "Installation suspended, incomplete.",
                1605 => "This action is only valid for products that are currently installed.",
                1606 => "The feature identifier isn't registered.",
                1607 => "The component identifier isn't registered.",
                1608 => "This is an unknown property.",
                1609 => "The handle is in an invalid state.",
                1610 => "The configuration data for this product is corrupt. Contact support personnel.",
                1611 => "The component qualifier not present.",
                1612 => "The installation source for this product isn't available.",
                1613 => "This installation package can't be installed by the Windows Installer service. Update Windows Installer.",
                1614 => "The product is uninstalled.",
                1615 => "The SQL query syntax is invalid or unsupported.",
                1616 => "The record field does not exist.",
                1618 => "Another installation is already in progress. Complete that installation first.",
                1619 => "This installation package couldn't be opened. Verify package exists and is accessible.",
                1620 => "This installation package couldn't be opened. Contact application vendor.",
                1621 => "Error starting Windows Installer service UI. Contact support personnel.",
                1622 => "Error opening installation log file. Verify log file location exists and is writable.",
                1623 => "Language of this installation package isn't supported by your system.",
                1624 => "Error applying transforms. Verify transform paths are valid.",
                1625 => "Installation is forbidden by system policy. Contact system administrator.",
                1626 => "The function couldn't be executed.",
                1627 => "The function failed during execution.",
                1628 => "An invalid or unknown table was specified.",
                1629 => "The data supplied is the wrong type.",
                1630 => "Data of this type isn't supported.",
                1631 => "Windows Installer service failed to start. Contact support personnel.",
                1632 => "Temp folder is either full or inaccessible. Verify Temp folder exists and is writable.",
                1633 => "Installation package isn't supported on this platform. Contact application vendor.",
                1634 => "Component isn't used on this machine.",
                1635 => "Patch package couldn't be opened. Verify patch package exists and is accessible.",
                1636 => "Patch package couldn't be opened. Contact application vendor.",
                1637 => "Patch package can't be processed by Windows Installer service. Update Windows Installer.",
                1638 => "Another version of this product is already installed. Use Add/Remove Programs to configure or remove.",
                1639 => "Invalid command line argument. Consult Windows Installer SDK for help.",
                1640 => "Current user isn't permitted to perform installations from a client session of a server running Terminal Server role.",
                1641 => "Installer has initiated a restart. Indicates success.",
                1642 => "Installer can't install the upgrade patch. Program may be missing or patch updates a different version.",
                1643 => "Patch package isn't permitted by system policy.",
                1644 => "One or more customizations aren't permitted by system policy.",
                1645 => "Windows Installer doesn't permit installation from a Remote Desktop Connection.",
                1646 => "Patch package isn't a removable patch package.",
                1647 => "Patch isn't applied to this product.",
                1648 => "No valid sequence could be found for the set of patches.",
                1649 => "Patch removal was disallowed by policy.",
                1650 => "XML patch data is invalid.",
                1651 => "Administrative user failed to apply patch for a per-user managed or per-machine application in advertised state.",
                1652 => "Windows Installer isn't accessible in Safe Mode. Exit Safe Mode and try again or use system restore.",
                1653 => "Couldn't perform a multiple-package transaction because rollback has been disabled. Available from Windows Installer 4.5.",
                1654 => "App isn't supported on this version of Windows. Unsigned package can't be installed on ARM computer.",
                3010 => "A restart is required to complete the install. Indicates success.",
                _ => "Unknown error code."
            };
        }

        public static Hashtable GetMSIProperties(string msiPath)
        {
            Hashtable msiProps = new Hashtable();
            using (Database db = new Database(msiPath, DatabaseOpenMode.ReadOnly))
            {
                using (View view = db.OpenView("SELECT `Property`, `Value` FROM `Property`"))
                {
                    view.Execute();
                    foreach (Record record in view)
                    {
                        msiProps[record.GetString(1)] = record.GetString(2);
                    }
                }
            }
            return msiProps;
        }

        public static MSISummaryInfo GetMSISummaryInfoFromMSI(string msiPath)
        {
            using (Database db = new Database(msiPath, DatabaseOpenMode.ReadOnly))
            {
                SummaryInfo summary = db.SummaryInfo;
                return new MSISummaryInfo
                {
                    Title = summary.Title,
                    Subject = summary.Subject,
                    Author = summary.Author,
                    Keywords = summary.Keywords,
                    Comments = summary.Comments,
                    Template = summary.Template
                };
            }
        }

        public static List<MSIProductInfo> GetRelatedMSIProducts(string upgradeCode)
        {
            if (string.IsNullOrWhiteSpace(upgradeCode))
                throw new ArgumentException("Upgrade code must be specified.", nameof(upgradeCode));

            var relatedProducts = new List<MSIProductInfo>();
            int index = 0;
            const int GUID_LEN = 39; // Including null terminator
            StringBuilder productCode = new StringBuilder(GUID_LEN);
            int result;
            do
            {
                productCode.Clear();
                result = MsiEnumRelatedProducts(upgradeCode, 0, index, productCode);
                if (result == 0) // ERROR_SUCCESS
                {
                    string code = productCode.ToString();
                    MsiProductInfoResult nameResult = GetMsiProductInfoString(code, "InstalledProductName");
                    MsiProductInfoResult manufacturerResult = GetMsiProductInfoString(code, "Publisher");
                    MsiProductInfoResult versionResult = GetMsiProductInfoString(code, "VersionString");
                    MsiProductInfoResult stateResult = GetMsiProductInfoString(code, "State");

                    // Only add if all required property queries succeeded
                    if (nameResult.ErrorCode == 0 && manufacturerResult.ErrorCode == 0 && versionResult.ErrorCode == 0 && stateResult.ErrorCode == 0)
                    {
                        // Determine architecture by registry location
                        Architecture? arch = null;
                        string uninstallKeyX64 = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{code}";
                        string uninstallKeyX86 = $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{code}";
                        using (var keyX64 = Registry.LocalMachine.OpenSubKey(uninstallKeyX64))
                        {
                            if (keyX64 != null)
                                arch = Architecture.X64;
                        }
                        if (arch == null)
                        {
                            using (var keyX86 = Registry.LocalMachine.OpenSubKey(uninstallKeyX86))
                            {
                                if (keyX86 != null)
                                    arch = Architecture.X86;
                            }
                        }
                        Version? parsedVersion = null;
                        if (Version.TryParse(versionResult.Value, out Version? v))
                            parsedVersion = v;
                        relatedProducts.Add(new MSIProductInfo
                        {
                            ProductCode = code,
                            DisplayName = nameResult.Value,
                            Publisher = manufacturerResult.Value,
                            ProductVersion = parsedVersion,
                            Architecture = arch,
                            State = stateResult.Value == "5" ? MSIState.Installed : stateResult.Value == "1" ? MSIState.Advertised : null,
                        });
                    }
                    // else: skip this product if any required property failed
                    index++;
                }
            } while (result == 0);
            return relatedProducts;
        }

        public static IEnumerable<MSIProductInfo> FindAllInstalledProductsByRegex(string manufacturerPattern, string productNamePattern)
        {
            var regexManufacturer = new Regex(manufacturerPattern, RegexOptions.IgnoreCase);
            var regexProduct = new Regex(productNamePattern, RegexOptions.IgnoreCase);
            var installedProducts = GetInstalledMSIProductsEx();

            return installedProducts.Where(p =>
                (!string.IsNullOrWhiteSpace(p.DisplayName) && regexProduct.IsMatch(p.DisplayName)) &&
                (!string.IsNullOrWhiteSpace(p.Publisher) && regexManufacturer.IsMatch(p.Publisher))
            );
        }

        public class MsiProductInfoResult
        {
            public string Value { get; set; } = string.Empty;
            public int ErrorCode { get; set; }
            public string ErrorDescription { get; set; } = string.Empty;
        }

        public enum MsiInstallContext : int
        {
            UserManaged = 1,
            UserUnmanaged = 2,
            Machine = 4,
            All = (UserManaged | UserUnmanaged | Machine)
        }

        public static MsiProductInfoResult GetMsiProductInfoString(string productCode, string property)
        {
            int len = 256;
            StringBuilder valueBuf = new StringBuilder(len);
            int res = MsiGetProductInfoEx(productCode, null, MsiInstallContext.Machine, property, valueBuf, ref len);
            if (res == 0)
            {
                return new MsiProductInfoResult
                {
                    Value = valueBuf.ToString(),
                    ErrorCode = 0,
                    ErrorDescription = "Success"
                };
            }
            else
            {
                return new MsiProductInfoResult
                {
                    Value = string.Empty,
                    ErrorCode = res,
                    ErrorDescription = GetMSIGetProductInfoExErrorDescription(res)
                };
            }
        }

        public static bool IsMSIProductInstalledForAllUsers(string productCode, Architecture arch)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new ArgumentNullException(nameof(productCode), "No ProductCode was provided to each if the MSI is installed");

            if (!Guid.TryParse(productCode, out Guid productGuid))
                throw new ArgumentException($"ProductCode '{productCode}' is not a valid GUID.", nameof(productCode));

            List<MSIProductInfo>? installedProducts = GetInstalledMSIProductsEx();

            // Compare parsed GUIDs rather than raw strings so differences in curly-brace
            // formatting or casing between the caller's value and what Windows Installer
            // reports (via GetInstalledMSIProductsEx) don't cause a false "not installed" result.
            MSIProductInfo? product = installedProducts?.FirstOrDefault(p =>
                p.Architecture == arch &&
                p.ProductCode != null &&
                Guid.TryParse(p.ProductCode, out Guid installedGuid) &&
                installedGuid == productGuid);
            if (product == null)
            {
                return false;
            }

            // Per-machine installs apply to every user by definition; the SIDs list only
            // tracks per-user (UserManaged/UserUnmanaged) registrations, so the SID check
            // below is only meaningful for those contexts.
            if (product.Context == MsiInstallContext.Machine)
            {
                return true;
            }

            List<UserTools.UserExtensions.UserSessionInfo>? sessions = UserTools.UserExtensions.GetUserSessions();
            List<string>? userSIDs = sessions?.Where(s => s.UserSid != null).Select(s => s.UserSid.Value).ToList();
            if (userSIDs == null || userSIDs.Count < 1) return true; // If no user, then its not applicable, so say its detected
            if (product.SIDs == null || product.SIDs.Count < 1) return false; // there is user sessions, but the msi has no sids listed as installed
            return userSIDs.All(s => product.SIDs.Contains(s));
        }

        public static string GetMSIGetProductInfoExErrorDescription(int code)
        {
            return code switch
            {
                0 => "ERROR_SUCCESS: The function completed successfully.",
                5 => "ERROR_ACCESS_DENIED: Administrative privileges required for other users.",
                87 => "ERROR_INVALID_PARAMETER: An invalid parameter was passed.",
                122 => "ERROR_MORE_DATA: Buffer too small for requested data.",
                1602 => "ERROR_BAD_CONFIGURATION: Configuration data is corrupt.",
                1605 => "ERROR_UNKNOWN_PRODUCT: Product is unadvertised or uninstalled.",
                1608 => "ERROR_UNKNOWN_PROPERTY: Property is unrecognized.",
                1627 => "ERROR_FUNCTION_FAILED: Unexpected internal failure.",
                _ => "Unknown error code."
            };
        }

        public static List<MSIProductInfo> GetInstalledMSIProductsEx()
        {
            var productDict = new Dictionary<string, MSIProductInfo>(StringComparer.OrdinalIgnoreCase);
            const int GUID_LEN = 39;
            int index = 0;
            int res;
            StringBuilder productCode = new StringBuilder(GUID_LEN);
            StringBuilder sidBuf = new StringBuilder(256);
            int sidLen = sidBuf.Capacity;
            int context;

            do
            {
                productCode.Clear();
                sidBuf.Clear();
                sidLen = sidBuf.Capacity;
                res = MsiEnumProductsEx(
                    null, // Enumerate all products
                    null, // "s-1-1-0" (all users) fails with ERROR_ACCESS_DENIED even when elevated; NULL enumerates
                          // all machine-context products plus the calling account's own per-user installs.
                    7,    // MSIINSTALLCONTEXT_ALL (1|2|4: user managed, user unmanaged, machine)
                    index,
                    productCode,
                    out context,
                    sidBuf,
                    ref sidLen
                );
                if (res == 0) // ERROR_SUCCESS
                {
                    string code = productCode.ToString();
                    int state = MsiQueryProductState(code);
                    if (state == (int)MSIState.Installed)
                    {
                        string sid = sidBuf.ToString();
                        if (!productDict.TryGetValue(code, out var info))
                        {
                            MsiProductInfoResult nameResult = GetMsiProductInfoString(code, "ProductName");
                            MsiProductInfoResult publisherResult = GetMsiProductInfoString(code, "Publisher");
                            MsiProductInfoResult versionResult = GetMsiProductInfoString(code, "VersionString");
                            Version? version = null;
                            if (Version.TryParse(versionResult.Value, out var v))
                                version = v;

                            Architecture? arch = null;
                            string uninstallKeyX64 = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{code}";
                            string uninstallKeyX86 = $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{code}";
                            using (var keyX64 = Registry.LocalMachine.OpenSubKey(uninstallKeyX64))
                            {
                                if (keyX64 != null)
                                    arch = Architecture.X64;
                            }
                            if (arch == null)
                            {
                                using (var keyX86 = Registry.LocalMachine.OpenSubKey(uninstallKeyX86))
                                {
                                    if (keyX86 != null)
                                        arch = Architecture.X86;
                                }
                            }

                            info = new MSIProductInfo
                            {
                                ProductCode = code,
                                DisplayName = nameResult.Value,
                                Publisher = publisherResult.Value,
                                ProductVersion = version,
                                Architecture = arch,
                                State = MSIState.Installed,
                                Context = (MsiInstallContext)context,
                                SIDs = new List<string>()
                            };
                            productDict[code] = info;
                        }
                        if (!string.IsNullOrWhiteSpace(sid) && !info.SIDs.Contains(sid))
                        {
                            info.SIDs.Add(sid);
                        }
                    }
                    index++;
                }
            } while (res == 0);

            return productDict.Values.ToList();
        }
    }
}
