using Microsoft.Win32;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private void CreateSuiteRunningRegistry()
        {
            try
            {
                Guid upgradeCode = _suiteConfig.BuildSettings.UpgradeCode;
                Version suiteVersion = _suiteConfig.BuildSettings.SuiteVersion;
                int suiteRevision = _suiteConfig.BuildSettings.Revision;

                using (var key = Registry.LocalMachine.CreateSubKey(_suiteRegistryPath))
                {
                    if (key == null)
                    {
                        _log.WriteLog($"Failed to create or open registry key: {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Error);
                        return;
                    }
                    key.SetValue("LastSuiteVersionRan", suiteVersion.ToString(), RegistryValueKind.String);
                    key.SetValue("LastSuiteRevisionRan", suiteRevision, RegistryValueKind.DWord);
                    _log.WriteLog($"Set LastSuiteVersionRan = {suiteVersion}, LastSuiteRevisionRan = {suiteRevision} in {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Info);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to create suite running registry: {ex.Message}", "SuiteRegistry", Log.Severity.Error);
                throw;
            }
        }

        private void RemoveSuiteRunningRegistry()
        {
            try
            {
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(_suiteRegistryPath, writable: true))
                {
                    if (key == null)
                    {
                        return;
                    }

                    // Remove the values if they exist
                    if (key.GetValue("LastSuiteVersionRan") != null)
                        key.DeleteValue("LastSuiteVersionRan", false);
                    if (key.GetValue("LastSuiteRevisionRan") != null)
                        key.DeleteValue("LastSuiteRevisionRan", false);
                }

                // Check if the key is now empty and delete if so
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(_suiteRegistryPath))
                {
                    if (key != null && key.GetValueNames().Length == 0 && key.GetSubKeyNames().Length == 0)
                    {
                        Registry.LocalMachine.DeleteSubKey(_suiteRegistryPath);
                        _log.WriteLog($"Deleted empty registry key: {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Info);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to remove suite running registry: {ex.Message}", "SuiteRegistry", Log.Severity.Error);
            }
        }

        // Has dependency on CreateUninstallMedia() running first to ensure _uninstallMediaPath is set
        private void WriteSuiteDetectionRegistry()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_uninstallMediaPath)) throw new Exception("Uninstall media path is not set. Cannot write suite detection registry.");
                Guid upgradeCode = _suiteConfig.BuildSettings.UpgradeCode;
                Version suiteVersion = _suiteConfig.BuildSettings.SuiteVersion;
                string manufacturer = _suiteConfig.BuildSettings.Manufacturer ?? "Unknown";
                string name = _suiteConfig.BuildSettings.Name ?? "Unknown";

                using (RegistryKey? key = Registry.LocalMachine.CreateSubKey(_suiteRegistryPath))
                {
                    if (key == null)
                    {
                        _log.WriteLog($"Failed to create or open registry key: {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Error);
                        return;
                    }
                    key.SetValue("DisplayName", $"{manufacturer} {name} Suite", RegistryValueKind.String);
                    key.SetValue("DisplayVersion", suiteVersion?.ToString() ?? "0.0.0.0", RegistryValueKind.String);
                    key.SetValue("Version", suiteVersion?.ToString() ?? "0.0.0.0", RegistryValueKind.String);
                    key.SetValue("Publisher", manufacturer, RegistryValueKind.String);
                    key.SetValue("Revision", _suiteConfig.BuildSettings.Revision, RegistryValueKind.DWord);
                    key.SetValue("UninstallString", @$"""C:\Program Files\SuiteExecutor\SuiteExecutor.exe"" remove --Config ""{_uninstallMediaPath}\SuiteConfig.scfg""");
                    key.SetValue("DisplayIcon", @"C:\Program Files\SuiteExecutor\SuiteExecutor.exe", RegistryValueKind.String);
                    _log.WriteLog($"Set suite detection registry: Version = {suiteVersion}, Revision = {_suiteConfig.BuildSettings.Revision}", "SuiteRegistry", Log.Severity.Info);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to write suite detection registry: {ex.Message}", "SuiteRegistry", Log.Severity.Error);
                throw;
            }
        }

        private void RemoveSuiteDetectionRegistry()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(_suiteRegistryPath, false);
                _log.WriteLog($"Removed suite detection registry key: {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to remove suite detection registry: {ex.Message}", "SuiteRegistry", Log.Severity.Error);
            }
        }

        private void CreateSuitePopupInitialDateRegistry()
        {
            try
            {
                using (RegistryKey? key = Registry.LocalMachine.CreateSubKey(_suiteRegistryPath))
                {
                    if (key == null)
                    {
                        _log.WriteLog($"Failed to create or open registry key: {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Error);
                        return;
                    }
                    key.SetValue($"{_action}FirstPopupShownDate", DateTime.Now.ToString("o"), RegistryValueKind.String);
                    _log.WriteLog($"Set {_action}FirstPopupShownDate in registry: {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Info);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to create suite popup initial date registry: {ex.Message}", "SuiteRegistry", Log.Severity.Error);
                throw;
            }
        }

        private DateTime? GetSuitePopupInitialDateRegistry()
        {
            try
            {
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(_suiteRegistryPath))
                {
                    if (key == null)
                    {
                        _log.WriteLog($"Registry key not found for popup initial date at: {_suiteRegistryPath}", "SuiteRegistry", Log.Severity.Info);
                        return null;
                    }
                    string? dateValue = key.GetValue($"{_action}FirstPopupShownDate") as string;
                    if (string.IsNullOrWhiteSpace(dateValue))
                        return null;
                    if (DateTime.TryParse(dateValue, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime firstPopupDate))
                    {
                        _log.WriteLog($"Retrieved FirstPopupShownDate from registry: {firstPopupDate:o}", "SuiteRegistry", Log.Severity.Info);
                    }
                    else
                    {
                        throw new Exception($"Failed to parse FirstPopupShownDate from registry value: {dateValue}");
                    }
                    return firstPopupDate;
                }
            }
            catch
            {
                throw;
            }
        }
    }
}
