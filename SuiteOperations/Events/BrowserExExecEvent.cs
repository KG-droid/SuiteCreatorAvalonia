using Logger;
using Microsoft.Win32;
using SuiteCreatorAvalonia.Models.Events;
using System.IO;
using System.Text.Json;

namespace SuiteOperations.Events
{
    public partial class BrowserExExecEvent : BrowserExt
    {
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
                    if (Source == SuiteCreatorAvalonia.Enums.BrowserExtensionSource.Local && !string.IsNullOrWhiteSpace(ExtPath))
                    {
                        updateUrl = "file://" + ExtPath.Replace("\\", "/");
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
                    string json = BuildExtensionSettingsJson(updateUrl);
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
                _log.WriteLog("Browser extension uninstalled.");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to uninstall browser extension: {ex.Message}", "Application", Log.Severity.Error);
                throw;
            }
        }

        // Json is tiny, so this is AOT safe, and probably not worth anything more complex
        private static string BuildExtensionSettingsJson(string updateUrl)
        {
            using MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("installation_mode", "force_installed");
                writer.WriteString("update_url", updateUrl);
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
