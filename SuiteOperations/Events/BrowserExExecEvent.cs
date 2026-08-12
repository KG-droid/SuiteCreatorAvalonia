using Logger;
using Microsoft.Win32;
using SuiteCreatorAvalonia.Models.Events;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml;

namespace SuiteOperations.Events
{
    public partial class BrowserExExecEvent : BrowserExt
    {
        // The gupdate response namespace Chrome/Edge expect an update manifest to use - see
        // https://www.chromium.org/administrators/pre-configured-extensions/#update-server-manifest
        private const string GupdateNamespace = "http://www.google.com/update2/response";

        // Local CRX files only live in the suite's temp install cache (SuiteInstallerCache), which gets
        // deleted once the suite finishes running. Chrome/Edge re-reads the force_installed update_url
        // for extension updates for as long as the policy exists, so the file needs to persist here
        // instead. Kept out of the SuiteExecutor folder and named plainly so it's discoverable by anyone
        // poking around Program Files without prior knowledge of the suite tooling.
        private static readonly string PermanentExtensionStoreRoot = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "BrowserExtensions");

        private Log _log;

        public BrowserExExecEvent(Log log)
        {
            _log = log;
        }

        public BrowserExExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            if (Action == null)
            {
                _log.WriteLog("No browser extension action specified.");
                throw new InvalidOperationException("No browser extension action specified.");
            }
            switch (Action)
            {
                case SuiteCreatorAvalonia.Enums.ExtAction.Install:
                    InstallBrowserExtension();
                    break;
                case SuiteCreatorAvalonia.Enums.ExtAction.Uninstall:
                    UninstallBrowserExtension();
                    break;
                default:
                    _log.WriteLog($"Unknown browser extension action: {Action}");
                    throw new InvalidOperationException($"Unknown browser extension action: {Action}");
            }
        }

        public void InstallBrowserExtension()
        {
            if (Browser == null || Source == null)
            {
                _log.WriteLog("Browser or Source not specified for extension install.", "Application", Log.Severity.Error);
                throw new InvalidOperationException("Browser or Source not specified for extension install.");
            }
            string browserKey = Browser == SuiteCreatorAvalonia.Enums.BrowserType.GoogleChrome ?
                @"SOFTWARE\\Policies\\Google\\Chrome\\ExtensionSettings" :
                @"SOFTWARE\\Policies\\Microsoft\\Edge\\ExtensionSettings";
            string extensionId = ExtensionId;
            if (string.IsNullOrWhiteSpace(extensionId))
            {
                _log.WriteLog("ExtensionId is required for browser extension install.", "Application", Log.Severity.Error);
                throw new InvalidOperationException("ExtensionId is required for browser extension install.");
            }
            try
            {
                using (RegistryKey baseKey = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(browserKey))
                {
                    if (baseKey == null)
                    {
                        _log.WriteLog($"Failed to open or create registry key: {browserKey}", "Application", Log.Severity.Error);
                        throw new InvalidOperationException($"Failed to open or create registry key: {browserKey}");
                    }
                    string updateUrl;
                    string? minimumVersionRequired = null;
                    bool overrideUpdateUrl = false;
                    if (Source == SuiteCreatorAvalonia.Enums.BrowserExtensionSource.Local && !string.IsNullOrWhiteSpace(ExtPath))
                    {
                        (string permanentManifestPath, string version) = CopyExtensionToPermanentStore();
                        updateUrl = ToFileUri(permanentManifestPath);
                        // A manual/local CRX means updates are self-managed rather than coming from a web
                        // store - override_update_url lets a non-store update_url actually take effect, and
                        // pinning minimum_version_required to the CRX's own version stops the browser from
                        // silently running a stale copy if the update manifest ever fails to fetch.
                        overrideUpdateUrl = true;
                        minimumVersionRequired = version;
                    }
                    else if (Source == SuiteCreatorAvalonia.Enums.BrowserExtensionSource.ChromeWebStore)
                    {
                        updateUrl = "https://clients2.google.com/service/update2/crx";
                    }
                    else if (Source == SuiteCreatorAvalonia.Enums.BrowserExtensionSource.MSEdgeWebStore)
                    {
                        updateUrl = "https://edge.microsoft.com/extensionwebstorebase/v1/crx";
                    }
                    else
                    {
                        _log.WriteLog($"Unsupported extension source: {Source}", "Application", Log.Severity.Error);
                        throw new InvalidOperationException($"Unsupported extension source: {Source}");
                    }
                    string json = BuildExtensionSettingsJson(updateUrl, overrideUpdateUrl, minimumVersionRequired);
                    baseKey.SetValue(extensionId, json, RegistryValueKind.String);
                    _log.WriteLog($"Extension {extensionId} policy written to {browserKey}.");
                }
                _log.WriteLog("Browser extension installed successfully.");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to install browser extension: {ex.Message}", "Application", Log.Severity.Error);
                throw;
            }
        }

        // Chrome/Edge don't force-install a local CRX directly off update_url - they expect update_url to
        // point at an update manifest (the gupdate XML below), which in turn points at the actual CRX via
        // its codebase attribute. Build both here: copy the CRX into the permanent store, read its real
        // version out of manifest.json so the update manifest doesn't just lie with a hardcoded version,
        // and write the update manifest alongside it.
        private (string ManifestPath, string Version) CopyExtensionToPermanentStore()
        {
            string destDir = Path.Combine(PermanentExtensionStoreRoot, Id.ToString());
            Directory.CreateDirectory(destDir);

            string? crxSourcePath = Directory.GetFiles(ExtPath, "*.crx").FirstOrDefault()
                ?? Directory.GetFiles(ExtPath).FirstOrDefault();
            if (crxSourcePath == null)
            {
                throw new FileNotFoundException($"No CRX file found in extension source directory: {ExtPath}");
            }

            string crxDestPath = Path.Combine(destDir, Path.GetFileName(crxSourcePath));
            File.Copy(crxSourcePath, crxDestPath, true);

            string version = ReadCrxVersion(crxDestPath);
            string manifestPath = Path.Combine(destDir, "update_manifest.xml");
            WriteUpdateManifest(manifestPath, ExtensionId, ToFileUri(crxDestPath), version);

            _log.WriteLog($"Copied local browser extension (version {version}) to permanent store: {destDir}");
            return (manifestPath, version);
        }

        // CRX3 layout: 4-byte magic "Cr24", 4-byte format version, 4-byte header size, then that many bytes
        // of protobuf header, then a plain zip archive - see https://www.chromium.org/developers/design-documents/extensions/how-the-extension-system-works/crx-package-format/
        private static string ReadCrxVersion(string crxPath)
        {
            byte[] data = File.ReadAllBytes(crxPath);
            if (data.Length < 12 || data[0] != 'C' || data[1] != 'r' || data[2] != '2' || data[3] != '4')
            {
                throw new InvalidOperationException($"'{crxPath}' is not a valid CRX file.");
            }

            int headerSize = BitConverter.ToInt32(data, 8);
            int zipStart = 12 + headerSize;
            if (headerSize < 0 || zipStart >= data.Length)
            {
                throw new InvalidOperationException($"'{crxPath}' has an invalid CRX header.");
            }

            using MemoryStream zipStream = new MemoryStream(data, zipStart, data.Length - zipStart, writable: false);
            using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            ZipArchiveEntry? manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                throw new InvalidOperationException($"'{crxPath}' does not contain a manifest.json.");
            }

            using Stream manifestStream = manifestEntry.Open();
            using JsonDocument manifest = JsonDocument.Parse(manifestStream);
            if (!manifest.RootElement.TryGetProperty("version", out JsonElement versionElement) || versionElement.GetString() is not string version)
            {
                throw new InvalidOperationException($"manifest.json in '{crxPath}' has no version field.");
            }

            return version;
        }

        private static void WriteUpdateManifest(string manifestPath, string extensionId, string crxFileUri, string version)
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
            };
            using XmlWriter writer = XmlWriter.Create(manifestPath, settings);
            writer.WriteStartDocument();
            writer.WriteStartElement("gupdate", GupdateNamespace);
            writer.WriteAttributeString("protocol", "2.0");
            writer.WriteStartElement("app", GupdateNamespace);
            writer.WriteAttributeString("appid", extensionId);
            writer.WriteStartElement("updatecheck", GupdateNamespace);
            writer.WriteAttributeString("codebase", crxFileUri);
            writer.WriteAttributeString("version", version);
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        private static string ToFileUri(string path) => "file:///" + path.Replace("\\", "/");

        public void UninstallBrowserExtension()
        {
            if (Browser == null)
            {
                _log.WriteLog("Browser not specified for extension uninstall.", "Application", Log.Severity.Error);
                throw new InvalidOperationException("Browser not specified for extension uninstall.");
            }
            string browserKey = Browser == SuiteCreatorAvalonia.Enums.BrowserType.GoogleChrome ?
                @"SOFTWARE\\Policies\\Google\\Chrome\\ExtensionSettings" :
                @"SOFTWARE\\Policies\\Microsoft\\Edge\\ExtensionSettings";
            string extensionId = ExtensionId;
            if (string.IsNullOrWhiteSpace(extensionId))
            {
                _log.WriteLog("ExtensionId is required for browser extension uninstall.", "Application", Log.Severity.Error);
                throw new InvalidOperationException("ExtensionId is required for browser extension uninstall.");
            }
            try
            {
                using (RegistryKey baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(browserKey, writable: true))
                {
                    if (baseKey == null)
                    {
                        _log.WriteLog($"Failed to open registry key: {browserKey}", "Application", Log.Severity.Error);
                        throw new InvalidOperationException($"Failed to open registry key: {browserKey}");
                    }
                    baseKey.DeleteValue(extensionId, false);
                    _log.WriteLog($"Extension {extensionId} policy removed from {browserKey}.");
                }
                RemoveFromPermanentStore();
                _log.WriteLog("Browser extension uninstalled.");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to uninstall browser extension: {ex.Message}", "Application", Log.Severity.Error);
                throw;
            }
        }

        private void RemoveFromPermanentStore()
        {
            string destDir = Path.Combine(PermanentExtensionStoreRoot, Id.ToString());
            if (Directory.Exists(destDir))
            {
                Directory.Delete(destDir, true);
                _log.WriteLog($"Removed permanent browser extension store: {destDir}");
            }

            if (Directory.Exists(PermanentExtensionStoreRoot) && !Directory.EnumerateFileSystemEntries(PermanentExtensionStoreRoot).Any())
            {
                Directory.Delete(PermanentExtensionStoreRoot);
                _log.WriteLog($"Removed empty browser extension store root: {PermanentExtensionStoreRoot}");
            }
        }

        // Json is tiny, so this is AOT safe, and probably not worth anything more complex
        private static string BuildExtensionSettingsJson(string updateUrl, bool overrideUpdateUrl, string? minimumVersionRequired)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("installation_mode", "force_installed");
                writer.WriteString("update_url", updateUrl);
                if (overrideUpdateUrl)
                {
                    writer.WriteBoolean("override_update_url", true);
                }
                if (!string.IsNullOrWhiteSpace(minimumVersionRequired))
                {
                    writer.WriteString("minimum_version_required", minimumVersionRequired);
                }
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
