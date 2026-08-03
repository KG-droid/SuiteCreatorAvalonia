using AvaloniaEdit.Document;
using Microsoft.Win32;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorModels.Enums;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Management.Automation;
using SuiteTools;

namespace SuiteCreatorAvalonia.Models.Rules
{
    public enum DetectionTypes
    {
        Exists,
        NotExists,
        String,
        Number,
        Binary,
        Installed,
        NotInstalled,
        Version,
        FileVersion,
        ProductVersion,
        Boolean,
        StringVersion,
    }

    public class RuleResult
    {
        public bool IsMet { get; set; }
        public string? Summary { get; set; }
    }

    public class Rule : RuleBase
    {
        public string? Base { get; set; }
        // Rich, variable-aware path used for File rules (e.g. a literal segment followed by a SpecialDIR
        // token like ApplicationData). Takes precedence over Base when it has content; Base is kept for
        // backward compatibility with existing saved rules and for the other rule types.
        public List<VariableText>? BasePath { get; set; }
        public string? ScriptName { get; set; }
        public TextDocument? Script { get; set; }
        public Architecture? Architecture { get; set; }
        public Contexts? Context { get; set; }
        public DetectionTypes DetectionType { get; set; }
        public ComparatorPlus? Comparator { get; set; }
        public string? ComparatorProperty { get; set; }
        public string? ComparatorValue { get; set; }

        public override RuleBase Clone()
        {
            return new Rule
            {
                Id = Id,
                Type = Type,
                Base = Base,
                BasePath = BasePath?.Select(v => v.Clone()).ToList(),
                Script = string.IsNullOrWhiteSpace(Script?.Text) ? new TextDocument() : new TextDocument(Script?.Text),
                Architecture = Architecture,
                DetectionType = DetectionType,
                Comparator = Comparator,
                ComparatorProperty = ComparatorProperty,
                ComparatorValue = ComparatorValue,
                Context = Context,
                ScriptName = ScriptName
            };
        }

        public override void UpdateFrom(RuleBase other)
        {
            if (other is not Rule rule) return;
            Id = rule.Id;
            Type = rule.Type;
            Base = rule.Base;
            BasePath = rule.BasePath?.Select(v => v.Clone()).ToList();
            Script = new TextDocument(rule.Script?.Text);
            Architecture = rule.Architecture;
            DetectionType = rule.DetectionType;
            Comparator = rule.Comparator;
            ComparatorProperty = rule.ComparatorProperty;
            ComparatorValue = rule.ComparatorValue;
            Context = rule.Context;
            ScriptName = rule.ScriptName;
        }

        public string? Validate()
        {
            string? effectiveBase = Type == RuleType.File ? ResolveFileBasePath() : Base;
            if (string.IsNullOrWhiteSpace(effectiveBase))
            {
                return "A rule must have a base string, like a product code, reg key, file path etc.";
            }
            if (Type == RuleType.File && !Path.IsPathFullyQualified(effectiveBase))
            {
                return "This rule must have a Fully Qualified path defined";
            }
            if (Type == RuleType.Registry && (!Base.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase) && !Base.StartsWith("Computer\\HKEY_", StringComparison.OrdinalIgnoreCase)))
            {
                return "This rule key path must start with Computer\\HKEY_ or HKEY_";
            }
            if (
                Type == RuleType.Registry &&
                DetectionType != DetectionTypes.Exists &&
                DetectionType != DetectionTypes.NotExists &&
                string.IsNullOrWhiteSpace(ComparatorProperty)
            )
            {
                return "A Registry rule with this Detection type must have a Registry property/value name specified.";
            }
            if (Type == RuleType.PowerShell && string.IsNullOrWhiteSpace(ScriptName) && string.IsNullOrWhiteSpace(Script?.Text))
            {
                return "A powershell rule must have either a script file and embedded script";
            }
            if (Type != RuleType.PowerShell && !Enum.IsDefined(typeof(Architecture), Architecture))
            {
                return "This rule must have an Architecture defined";
            }
            if ((Type == RuleType.MSI || Type == RuleType.MSIx) && !Enum.IsDefined(typeof(Contexts), Context))
            {
                return "This rule must have a context defined";
            }
            if (!Enum.IsDefined(DetectionType))
            {
                return "A rule must have a Detection type defined";
            }
            if (
                (
                    DetectionType == DetectionTypes.Version ||
                    DetectionType == DetectionTypes.FileVersion ||
                    DetectionType == DetectionTypes.ProductVersion ||
                    DetectionType == DetectionTypes.StringVersion
                ) && !Version.TryParse(ComparatorValue, out _)
            )
            {
                return "A version based rule detection must have a valid System.Version like 1.0.0";
            }
            if (DetectionType == DetectionTypes.Number && !double.TryParse(ComparatorValue, out _))
            {
                return "A number-based rule detection must have a valid numeric value.";
            }
            if (Comparator == ComparatorPlus.Matches || Comparator == ComparatorPlus.DoesNotMatch)
            {
                if (string.IsNullOrEmpty(ComparatorValue))
                {
                    return "A RegEx match detection must have a regular expression pattern specified.";
                }
                try
                {
                    _ = new Regex(ComparatorValue);
                }
                catch (ArgumentException)
                {
                    return $"'{ComparatorValue}' is not a valid regular expression.";
                }
            }
            if (DetectionType == DetectionTypes.Binary)
            {
                if (!string.IsNullOrWhiteSpace(ComparatorValue))
                {
                    var hex = ComparatorValue.Replace(" ", "");
                    if (hex.Length % 2 != 0 || !hex.All(c => Uri.IsHexDigit(c)))
                    {
                        return "A Binary based rule detection must have a valid registry binary value (hex string as seen in the Registry Data column, like: '01 0A FF'), or an empty string.";
                    }
                }
            }
            if (Type == RuleType.MSI)
            {
                if (string.IsNullOrWhiteSpace(Base) || !Guid.TryParse(Base.Trim('{', '}'), out _))
                {
                    return "MSI product or upgrade code must be a valid GUID string format.";
                }
            }
            if (Type == RuleType.MSIx)
            {
                if (string.IsNullOrWhiteSpace(Base))
                    return "MSIx rule must have a Package Family Name or Package Full Name.";
                var pfnPattern = @"_[A-Za-z0-9]{13}$";

                if (!Regex.IsMatch(Base, pfnPattern))
                    return "An MSIx PFN must have a 13 character publisher hash on the end";
            }
            return null;
        }

        public RuleResult IsMet()
        {
            RuleResult ruleResult = new();
            switch (Type)
            {
                case RuleType.File:
                    return IsFileDetected();
                case RuleType.MSI:
                    return IsMSIDetected();
                case RuleType.MSIx:
                    return IsMSIxDetected();
                case RuleType.PowerShell:
                    return IsPowerShellDetected();
                case RuleType.Registry:
                    return IsRegistryDetected();
                default:
                    throw new Exception($"Unknown rule type: {Type}");
            }
        }

        private RuleResult IsFileDetected()
        {
            // See ComparatorMapping.cs for valid DetectionTypes and Comparators for File rules.
            string? resolvedPath = ResolveFileBasePath();
            bool fileExists = !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath);
            RuleResult ruleResult = new();
            switch (DetectionType)
            {
                case DetectionTypes.Exists:
                    ruleResult.IsMet = fileExists;
                    ruleResult.Summary = fileExists ? $"File exists at: {resolvedPath}, detection met" : $"File not found at: {resolvedPath}, detection not met";
                    return ruleResult;
                case DetectionTypes.NotExists:
                    ruleResult.IsMet = !fileExists;
                    ruleResult.Summary = fileExists ? $"File exists at: {resolvedPath}, detection not met" : $"File not found at: {resolvedPath}, detection met";
                    return ruleResult;
                case DetectionTypes.FileVersion:
                case DetectionTypes.ProductVersion:
                    if (string.IsNullOrWhiteSpace(ComparatorValue))
                        throw new ArgumentException(nameof(ComparatorValue), "File based version detection must have a ComparatorValue");
                    if (!fileExists)
                    {
                        ruleResult.IsMet = fileExists;
                        ruleResult.Summary = $"File does not exist at: {resolvedPath}, so no version to check, detection not met.";
                        return ruleResult;
                    }
                    FileVersionInfo? versionInfo = FileVersionInfo.GetVersionInfo(resolvedPath);
                    string? actualVersion;
                    if (DetectionType is DetectionTypes.ProductVersion)
                        actualVersion = versionInfo.ProductVersion;
                    else
                        actualVersion = versionInfo.FileVersion;
                    ruleResult.IsMet = ProcessComparator((ComparatorPlus)Comparator, actualVersion, ComparatorValue);
                    ruleResult.Summary = $"Actual version: {actualVersion}, is {Comparator}, reference: {ComparatorValue}";
                    return ruleResult;
                default:
                    throw new Exception($"Detection type: {DetectionType} is not supported for File rule.");
            }
        }

        private RuleResult IsMSIDetected()
        {
            var ruleResult = new RuleResult();

            if (string.IsNullOrWhiteSpace(Base) || !Guid.TryParse(Base.Trim('{', '}'), out Guid codeGuid))
            {
                throw new Exception("MSI product code must be a valid GUID string format.");
            }
            string productCode = codeGuid.ToString("B").ToUpperInvariant();

            switch (DetectionType)
            {
                case DetectionTypes.Installed:
                    // Only ProductCode detection
                    bool isInstalled = MSITools.IsMSIProductInstalledForAllUsers(
                        productCode, 
                        Architecture == Enums.Architecture.x64 ? System.Runtime.InteropServices.Architecture.X64 : System.Runtime.InteropServices.Architecture.X86);
                    ruleResult.IsMet = isInstalled;
                    ruleResult.Summary = isInstalled
                        ? $"MSI product with ProductCode {productCode} is installed."
                        : $"MSI product with ProductCode {productCode} is not installed.";
                    return ruleResult;

                case DetectionTypes.NotInstalled:
                    // Only ProductCode detection
                    bool notInstalled = !MSITools.IsMSIProductInstalledForAllUsers(
                        productCode,
                        Architecture == Enums.Architecture.x64 ? System.Runtime.InteropServices.Architecture.X64 : System.Runtime.InteropServices.Architecture.X86);
                    ruleResult.IsMet = notInstalled;
                    ruleResult.Summary = notInstalled
                        ? $"MSI product with ProductCode {productCode} is not installed."
                        : $"MSI product with ProductCode {productCode} is installed.";
                    return ruleResult;

                case DetectionTypes.Version:
                    // Family detection (UpgradeCode)
                    if (string.IsNullOrWhiteSpace(ComparatorValue))
                        throw new ArgumentException(nameof(ComparatorValue), "Version detection must have a ComparatorValue.");
                    var products = MSITools.GetRelatedMSIProducts(productCode);
                    if (products == null || products.Count == 0)
                    {
                        ruleResult.IsMet = false;
                        ruleResult.Summary = $"No MSI products found for UpgradeCode {productCode}, so cannot check version.";
                        return ruleResult;
                    }
                    bool anyMet = false;
                    string? actualVersion = null;
                    foreach (var prod in products)
                    {
                        actualVersion = prod.ProductVersion?.ToString();
                        if (actualVersion != null && Comparator != null)
                        {
                            if (ProcessComparator((ComparatorPlus)Comparator, actualVersion, ComparatorValue))
                            {
                                anyMet = true;
                                break;
                            }
                        }
                    }
                    ruleResult.IsMet = anyMet;
                    ruleResult.Summary = anyMet
                        ? $"At least one MSI product for UpgradeCode {productCode} matches version {ComparatorValue}."
                        : $"No MSI products for UpgradeCode {productCode} match version {ComparatorValue}.";
                    return ruleResult;

                default:
                    throw new Exception($"Detection type: {DetectionType} is not supported for MSI rule.");
            }
        }

        private RuleResult IsMSIxDetected()
        {
            if (string.IsNullOrWhiteSpace(Base))
                throw new ArgumentNullException(nameof(Base), "MSIx rule must have a Package Family Name or Package Full Name.");

            var ruleResult = new RuleResult();

            switch (DetectionType)
            {
                case DetectionTypes.Installed:
                    {
                        bool isInstalled = MSIxTools.IsMSIReadyForAnyUser(Base);
                        ruleResult.IsMet = isInstalled;
                        ruleResult.Summary = isInstalled
                            ? $"MSIx package '{Base}' is installed for at least one user."
                            : $"MSIx package '{Base}' is not installed for any user.";
                        return ruleResult;
                    }
                case DetectionTypes.NotInstalled:
                    {
                        bool notInstalled = !MSIxTools.IsMSIReadyForAnyUser(Base);
                        ruleResult.IsMet = notInstalled;
                        ruleResult.Summary = notInstalled
                            ? $"MSIx package '{Base}' is not installed for any user."
                            : $"MSIx package '{Base}' is installed for at least one user.";
                        return ruleResult;
                    }
                case DetectionTypes.Version:
                    {
                        if (string.IsNullOrWhiteSpace(ComparatorValue))
                            throw new ArgumentException("Version detection must have a ComparatorValue.", nameof(ComparatorValue));
                        if (Comparator == null)
                            throw new ArgumentNullException(nameof(Comparator), "Comparator must be defined for version detection.");

                        var familyPkgs = MSIxTools.GetInstalledMSIxPackagesByFamilyName(Base);
                        if (familyPkgs == null || familyPkgs.Count() == 0)
                        {
                            ruleResult.IsMet = false;
                            ruleResult.Summary = $"MSIx package '{Base}' is not installed, cannot check version.";
                            return ruleResult;
                        }
                        foreach (var pkg in familyPkgs)
                        {
                            string actualVersion = pkg.Id.Version.ToString();
                            if (ProcessComparator((ComparatorPlus)Comparator, actualVersion, ComparatorValue))
                            {
                                if (MSIxTools.IsMSIxReadyForAllUsers(pkg.Id.FullName))
                                {
                                    ruleResult.IsMet = true;
                                    ruleResult.Summary = $"Found a family product: {pkg.Id.FullName}, which is {Comparator} to {ComparatorValue}.";
                                    return ruleResult;
                                }
                                else 
                                {
                                    ruleResult.IsMet = false;
                                    ruleResult.Summary = $"Found a family product: {pkg.Id.FullName}, however its not ready for all users.";
                                    return ruleResult;
                                }
                            }
                        }
                        ruleResult.IsMet = false;
                        ruleResult.Summary = $"No MSIx package in family '{Base}' is {Comparator} to {ComparatorValue}.";
                        return ruleResult;
                    }
                default:
                    throw new Exception($"Detection type: {DetectionType} is not supported for MSIx rule.");
            }
        }

        private RuleResult IsPowerShellDetected()
        {
            var ruleResult = new RuleResult();

            if (string.IsNullOrWhiteSpace(Script?.Text))
            {
                throw new ArgumentNullException(nameof(Script), "A PowerShell rule must have an embedded script.");
            }

            string output;
            using var ps = PowerShell.Create();
            ps.AddScript(Script?.Text);
            var results = ps.Invoke();
            output = string.Join(Environment.NewLine, results.Select(r => r?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

            switch (DetectionType)
            {
                case DetectionTypes.String:
                    if (Comparator == null || string.IsNullOrWhiteSpace(ComparatorValue))
                        throw new ArgumentNullException(nameof(Comparator), "Comparator and ComparatorValue must be defined for String detection.");
                    ruleResult.IsMet = EvaluateStringComparator((ComparatorPlus)Comparator, output, ComparatorValue);
                    ruleResult.Summary = ruleResult.IsMet ?
                        $"PowerShell output: '{output}', is {Comparator}, compared to '{ComparatorValue}'" :
                        $"PowerShell output: '{output}', is not {Comparator}, compared to '{ComparatorValue}'";
                    return ruleResult;

                case DetectionTypes.Boolean:
                    bool isTrue = output.Equals("True", StringComparison.OrdinalIgnoreCase) || output == "1";
                    ruleResult.IsMet = Comparator == ComparatorPlus.True ? isTrue : !isTrue;
                    ruleResult.Summary = $"PowerShell output: '{output}', expected: {Comparator}";
                    return ruleResult;

                default:
                    throw new Exception($"Detection type: {DetectionType} is not supported for PowerShell rule.");
            }
        }

        private RuleResult IsRegistryDetected()
        {
            var ruleResult = new RuleResult();
            if (string.IsNullOrWhiteSpace(Base))
            {
                ruleResult.IsMet = false;
                ruleResult.Summary = "Registry base path is not defined.";
                return ruleResult;
            }

            // Strip lead Computer\ if present
            if (Base.StartsWith("Computer\\", StringComparison.OrdinalIgnoreCase))
            {
                Base = Base.Substring("Computer\\".Length);
            }

            // Parse registry root and subkey
            string[] baseParts = Base.Split('\\', 2);
            if (baseParts.Length < 2)
            {
                ruleResult.IsMet = false;
                ruleResult.Summary = $"Invalid registry path: {Base}";
                return ruleResult;
            }

            string rootName = baseParts[0].ToUpperInvariant();
            string subKeyPath = baseParts[1];

            RegistryKey? rootKey;
            if (rootName == "HKEY_CURRENT_USER")
            {
                // Registry.CurrentUser reflects the account this process is running under (e.g. SYSTEM
                // when run as a service), not the user actually logged on interactively. Resolve the
                // logged-on user's session SID and read their hive via HKEY_USERS\<SID> instead.
                SecurityIdentifier? currentUserSid = GetCurrentLoggedOnUserSid();
                if (currentUserSid == null)
                {
                    ruleResult.IsMet = false;
                    ruleResult.Summary = "Unable to determine the current logged-on user to evaluate HKEY_CURRENT_USER.";
                    return ruleResult;
                }
                rootKey = Registry.Users;
                subKeyPath = $@"{currentUserSid}\{subKeyPath}";
            }
            else
            {
                rootKey = rootName switch
                {
                    "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                    "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
                    "HKEY_USERS" => Registry.Users,
                    "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
                    _ => null
                };
            }

            if (rootKey == null)
            {
                ruleResult.IsMet = false;
                ruleResult.Summary = $"Unknown registry root: {rootName}";
                return ruleResult;
            }

            using var subKey = rootKey.OpenSubKey(subKeyPath);
            bool keyExists = subKey != null;

            switch (DetectionType)
            {
                case DetectionTypes.Exists:
                    ruleResult.IsMet = keyExists;
                    ruleResult.Summary = keyExists
                        ? $"Registry key exists: {Base}"
                        : $"Registry key not found: {Base}";
                    return ruleResult;

                case DetectionTypes.NotExists:
                    ruleResult.IsMet = !keyExists;
                    ruleResult.Summary = keyExists
                        ? $"Registry key exists: {Base}, detection not met"
                        : $"Registry key not found: {Base}, detection met";
                    return ruleResult;

                case DetectionTypes.String:
                case DetectionTypes.Number:
                case DetectionTypes.StringVersion:
                case DetectionTypes.Binary:
                    if (!keyExists)
                    {
                        ruleResult.IsMet = false;
                        ruleResult.Summary = $"Registry key not found: {Base}";
                        return ruleResult;
                    }
                    if (string.IsNullOrWhiteSpace(ComparatorProperty))
                    {
                        throw new ArgumentNullException(nameof(ComparatorProperty), "ComparatorProperty must be defined on a String, Number, StringVersion, Or Binary detection");
                    }
                    if (string.IsNullOrWhiteSpace(ComparatorValue))
                    {
                        throw new ArgumentNullException(nameof(ComparatorProperty), "ComparatorValue must be defined on a String, Number, StringVersion, Or Binary detection");
                    }
                    if (Comparator == null)
                    {
                        throw new ArgumentNullException(nameof(ComparatorProperty), "A Comparator must be defined on a String, Number, StringVersion, Or Binary detection");
                    }
                    object? value = subKey.GetValue(ComparatorProperty);
                    if (value == null)
                    {
                        ruleResult.IsMet = false;
                        ruleResult.Summary = $"Registry value name: '{ComparatorProperty}', not found in key: {Base}";
                        return ruleResult;
                    }
                    string actualString = value.ToString() ?? string.Empty;
                    switch (DetectionType)
                    {
                        case DetectionTypes.String:
                            ruleResult.IsMet = EvaluateStringComparator((ComparatorPlus)Comparator, actualString, ComparatorValue);
                            ruleResult.Summary = $"Registry value '{ComparatorProperty}' is '{actualString}', compared to '{ComparatorValue}'";
                            return ruleResult;

                        case DetectionTypes.Number:
                            if (!double.TryParse(ComparatorValue, out _))
                            {
                                throw new ArgumentOutOfRangeException(nameof(ComparatorValue), $"Registry reference: '{ComparatorValue}' is not a number.");
                            }
                            ruleResult.IsMet = ProcessComparator((ComparatorPlus)Comparator, actualString, ComparatorValue);
                            ruleResult.Summary = $"Registry value name: '{ComparatorProperty}' is '{ComparatorValue}', compared to '{ComparatorValue}'";
                            return ruleResult;

                        case DetectionTypes.StringVersion:
                            if (!Version.TryParse(ComparatorValue, out _))
                            {
                                throw new ArgumentOutOfRangeException(nameof(ComparatorValue), $"Registry reference: '{ComparatorValue}' is not a valid version.");
                            }
                            if (!Version.TryParse(actualString, out _))
                            {
                                ruleResult.IsMet = false;
                                ruleResult.Summary = $"Registry value: '{ComparatorProperty}' is '{actualString}', which is not a valid version string, detection not met.";
                                return ruleResult;
                            }
                            ruleResult.IsMet = ProcessComparator((ComparatorPlus)Comparator, actualString, ComparatorValue);
                            ruleResult.Summary = $"Registry value '{ComparatorProperty}' version is '{actualString}', is {Comparator}, compared to '{ComparatorValue}'";
                            return ruleResult;

                        case DetectionTypes.Binary:
                            if (value is byte[] actualBinary)
                            {
                                string actualHex = BitConverter.ToString(actualBinary).Replace("-", " ");
                                ruleResult.IsMet = Comparator == ComparatorPlus.Equal ?
                                    ByteArraysEqual(ParseRegistryBinary(ComparatorValue), actualBinary) : !ByteArraysEqual(ParseRegistryBinary(ComparatorValue), actualBinary);
                                ruleResult.Summary = $"Registry value: '{ComparatorProperty}' is '{actualHex}', compared to '{ComparatorValue}'";
                                return ruleResult;
                            }
                            ruleResult.IsMet = false;
                            ruleResult.Summary = $"Registry value: '{ComparatorProperty}' is not binary.";
                            return ruleResult;
                    }
                    break;
                default:
                    throw new Exception($"Detection type: {DetectionType} is not supported for Registry rule.");
            }
            ruleResult.IsMet = false;
            ruleResult.Summary = "Unhandled registry detection case.";
            return ruleResult;
        }

        public string? ResolveFileBasePath()
        {
            if (BasePath != null && BasePath.Any(v => v is not LiteralText literal || !string.IsNullOrWhiteSpace(literal.Value)))
            {
                return ResolveVariablePath(BasePath);
            }
            return Base;
        }

        private static string ResolveVariablePath(List<VariableText> variablePath)
        {
            var resolved = new System.Text.StringBuilder();
            foreach (VariableText part in variablePath)
            {
                string? value = part is SpecialDIR specialDir ? ResolveSpecialFolder(specialDir.Value) : part.GetValue();
                if (!string.IsNullOrEmpty(value))
                    resolved.Append(value);
            }
            return resolved.ToString();
        }

        private static string ResolveSpecialFolder(SpecialFolderVar specialFolderVar)
        {
            Environment.SpecialFolder folder = (Environment.SpecialFolder)specialFolderVar;
            if (Enum.IsDefined(typeof(SpecialSysFolderVar), specialFolderVar.ToString()))
            {
                return Environment.GetFolderPath(folder);
            }

            SecurityIdentifier? loggedOnUserSid = GetCurrentLoggedOnUserSid();
            UserTools.UserExtensions.UserProfile? loggedOnUserProfile = loggedOnUserSid == null
                ? null
                : new UserTools.UserExtensions().GetHumanUserAccountInfo()
                    ?.FirstOrDefault(p => string.Equals(p.Sid, loggedOnUserSid.ToString(), StringComparison.OrdinalIgnoreCase));

            return loggedOnUserProfile != null ? loggedOnUserProfile.GetSpecialFolder(folder) : Environment.GetFolderPath(folder);
        }

        private static SecurityIdentifier? GetCurrentLoggedOnUserSid()
        {
            List<UserTools.UserExtensions.UserSessionInfo>? sessions = UserTools.UserExtensions.GetUserSessions();
            if (sessions == null || sessions.Count == 0)
                return null;

            return sessions
                .FirstOrDefault(s => s.State == UserTools.NativeHelpers.WTS_CONNECTSTATE_CLASS.WTSActive && s.UserSid != null)
                ?.UserSid;
        }

        public static byte[] ParseRegistryBinary(string hexString)
        {
            if (string.IsNullOrWhiteSpace(hexString))
                return Array.Empty<byte>();

            string[] hexValues = hexString.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            byte[] bytes = new byte[hexValues.Length];
            for (int i = 0; i < hexValues.Length; i++)
            {
                bytes[i] = Convert.ToByte(hexValues[i], 16);
            }
            return bytes;
        }

        public static bool ByteArraysEqual(byte[]? a1, byte[]? a2)
        {
            if (ReferenceEquals(a1, a2)) return true;
            if (a1 is null || a2 is null) return false;
            if (a1.Length != a2.Length) return false;
            for (int i = 0; i < a1.Length; i++)
            {
                if (a1[i] != a2[i]) return false;
            }
            return true;
        }

        public static bool ProcessComparator(ComparatorPlus comparator, string actualVersion, string referenceVersion)
        {
            int compareResult = CompareVersionStrings(actualVersion, referenceVersion);
            return EvaluateComparator(compareResult, comparator);
        }

        // Shared by Registry and PowerShell String detections. referencePattern is the admin-authored
        // ComparatorValue; an invalid RegEx here means it slipped past Rule.Validate(), so it errors rather
        // than reporting "not met" - unlike actualValue, which is just an observed string and always valid input.
        public static bool EvaluateStringComparator(ComparatorPlus comparator, string actualValue, string referencePattern)
        {
            switch (comparator)
            {
                case ComparatorPlus.Equal:
                    return actualValue.Equals(referencePattern, StringComparison.OrdinalIgnoreCase);
                case ComparatorPlus.NotEqual:
                    return !actualValue.Equals(referencePattern, StringComparison.OrdinalIgnoreCase);
                case ComparatorPlus.Matches:
                case ComparatorPlus.DoesNotMatch:
                    Regex regex;
                    try
                    {
                        regex = new Regex(referencePattern, RegexOptions.IgnoreCase);
                    }
                    catch (ArgumentException ex)
                    {
                        throw new ArgumentException($"'{referencePattern}' is not a valid regular expression.", nameof(referencePattern), ex);
                    }
                    bool isMatch = regex.IsMatch(actualValue);
                    return comparator == ComparatorPlus.Matches ? isMatch : !isMatch;
                default:
                    throw new ArgumentOutOfRangeException(nameof(comparator), $"Comparator: {comparator}, is not supported for String detection.");
            }
        }

        public static bool ProcessComparator(Comparator comparator, string actualVersion, string referenceVersion)
        {
            int compareResult = CompareVersionStrings(actualVersion, referenceVersion);
            return EvaluateComparator(compareResult, comparator);
        }

        private static bool EvaluateComparator(int compareResult, object comparator)
        {
            // ComparatorPlus logic
            if (comparator is ComparatorPlus cp)
            {
                return cp switch
                {
                    ComparatorPlus.Equal => compareResult == 0,
                    ComparatorPlus.NotEqual => compareResult != 0,
                    ComparatorPlus.GreaterThan => compareResult > 0,
                    ComparatorPlus.GreaterThanOrEqual => compareResult >= 0,
                    ComparatorPlus.LessThan => compareResult < 0,
                    ComparatorPlus.LessThanOrEqual => compareResult <= 0,
                    ComparatorPlus.True => true,
                    ComparatorPlus.False => false,
                    _ => throw new ArgumentOutOfRangeException(nameof(comparator), $"Unknown ComparatorPlus: {comparator}")
                };
            }
            // Comparator logic
            if (comparator is Comparator c)
            {
                return c switch
                {
                    Enums.Comparator.GreaterThan => compareResult > 0,
                    Enums.Comparator.GreaterThanOrEqual => compareResult >= 0,
                    Enums.Comparator.Equal => compareResult == 0,
                    Enums.Comparator.LessThan => compareResult < 0,
                    Enums.Comparator.LessThanOrEqual => compareResult <= 0,
                    _ => throw new ArgumentOutOfRangeException(nameof(comparator), $"Unknown Comparator: {comparator}")
                };
            }
            throw new ArgumentException("Unsupported comparator type", nameof(comparator));
        }

        // Returns >0 if left is greater, <0 if right is greater, 0 if equal.
        public static int CompareVersionStrings(string? left, string? right)
        {
            if (left == null && right == null) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            var leftParts = Regex.Split(left, @"[\.\-_\s]+");
            var rightParts = Regex.Split(right, @"[\.\-_\s]+");
            int maxLen = Math.Max(leftParts.Length, rightParts.Length);

            for (int i = 0; i < maxLen; i++)
            {
                string l = i < leftParts.Length ? leftParts[i] : "0";
                string r = i < rightParts.Length ? rightParts[i] : "0";

                // Try numeric comparison first
                bool lIsNum = int.TryParse(l, out int lNum);
                bool rIsNum = int.TryParse(r, out int rNum);

                if (lIsNum && rIsNum)
                {
                    if (lNum != rNum)
                        return lNum.CompareTo(rNum);
                }
                else if (lIsNum)
                {
                    // Numbers are considered less than strings
                    return -1;
                }
                else if (rIsNum)
                {
                    return 1;
                }
                else
                {
                    int cmp = string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0)
                        return cmp;
                }
            }
            return 0;
        }
    }
}
