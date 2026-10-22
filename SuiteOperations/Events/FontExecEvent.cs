using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Environment = System.Environment;
using WinRegistry = Microsoft.Win32.Registry;

namespace SuiteOperations.Events
{
    public partial class FontExecEvent : SystemFont
    {
        private const string FontsRegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
        private const int MoveFileDelayUntilReboot = 0x4;
        private const uint WmFontChange = 0x001D;
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);

        private Log _log;

        public FontExecEvent(Log log)
        {
            _log = log;
        }

        public FontExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            switch (Action)
            {
                case FontAction.Install:
                    InstallFonts();
                    break;
                case FontAction.Remove:
                    RemoveFonts();
                    break;
                default:
                    _log.WriteLog($"Unknown font action: {Action}");
                    throw new InvalidOperationException($"Unknown font action: {Action}");
            }
        }

        private static string FontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public void InstallFonts()
        {
            _log.WriteLog($"Installing font(s) from: {FontPath}");
            try
            {
                if (string.IsNullOrWhiteSpace(FontPath))
                {
                    _log.WriteLog("Font path is not specified.", "FontExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException("Font path is not specified.");
                }
                List<string> fontFiles;
                if (Directory.Exists(FontPath))
                {
                    fontFiles = Directory.EnumerateFiles(FontPath, "*", SearchOption.AllDirectories).Where(IsFontFile).ToList();
                }
                else if (File.Exists(FontPath))
                {
                    fontFiles = new List<string> { FontPath };
                }
                else
                {
                    _log.WriteLog($"Font path was not found: {FontPath}", "FontExecEvent", Log.Severity.Error);
                    throw new FileNotFoundException($"Font path was not found: {FontPath}", FontPath);
                }
                if (fontFiles.Count == 0)
                {
                    throw new FileNotFoundException($"No font files were found in: {FontPath}");
                }

                foreach (string fontFile in fontFiles)
                {
                    InstallFont(fontFile);
                }
                BroadcastFontChange();
                _log.WriteLog($"Installed {fontFiles.Count} font file(s).");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to install font(s): {ex.Message}", "FontExecEvent", Log.Severity.Error);
                throw;
            }
        }

        private void InstallFont(string sourceFile)
        {
            string fileName = Path.GetFileName(sourceFile);
            string destination = Path.Combine(FontsDir, fileName);
            string registryName = FontNameReader.GetRegistryName(sourceFile);
            _log.WriteLog($"Installing font {fileName} as \"{registryName}\"");

            // A font that's already registered and in use can't be overwritten, so unload any existing copy first
            if (File.Exists(destination))
            {
                FontNative.RemoveFontResource(destination);
            }
            try
            {
                File.Copy(sourceFile, destination, true);
            }
            catch (IOException) when (File.Exists(destination) && new FileInfo(destination).Length == new FileInfo(sourceFile).Length)
            {
                // Locked by a running app, but already the same size as the one being installed - keep it
                _log.WriteLog($"{fileName} is in use and already present, leaving the existing file in place.", "FontExecEvent", Log.Severity.Warning);
            }

            using (RegistryKey fontsKey = WinRegistry.LocalMachine.CreateSubKey(FontsRegistryPath))
            {
                // Drop any other registration of the same file (e.g. under a different name) so it isn't listed twice
                RemoveRegistryEntriesForFile(fontsKey, fileName, registryName);
                fontsKey.SetValue(registryName, fileName, RegistryValueKind.String);
            }
            if (FontNative.AddFontResource(destination) == 0)
            {
                _log.WriteLog($"Font {fileName} was installed but could not be loaded into the current session, it will be available after the next sign in.", "FontExecEvent", Log.Severity.Warning);
            }
        }

        public void RemoveFonts()
        {
            List<string> fileNames = GetFileNames().ToList();
            _log.WriteLog($"Removing font(s): {string.Join(", ", fileNames)}");
            try
            {
                if (fileNames.Count == 0)
                {
                    _log.WriteLog("Font file name is not specified.", "FontExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException("Font file name is not specified.");
                }
                foreach (string fileName in fileNames)
                {
                    RemoveFont(fileName);
                }
                BroadcastFontChange();
                _log.WriteLog("Font(s) removed.");
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to remove font(s): {ex.Message}", "FontExecEvent", Log.Severity.Error);
                throw;
            }
        }

        private void RemoveFont(string fileName)
        {
            string fontPath = Path.Combine(FontsDir, fileName);
            using (RegistryKey? fontsKey = WinRegistry.LocalMachine.OpenSubKey(FontsRegistryPath, true))
            {
                if (fontsKey != null)
                {
                    RemoveRegistryEntriesForFile(fontsKey, fileName, null);
                }
            }

            if (!File.Exists(fontPath))
            {
                _log.WriteLog($"Font file {fileName} is not in the Fonts folder, nothing to delete.", "FontExecEvent", Log.Severity.Warning);
                return;
            }

            FontNative.RemoveFontResource(fontPath);
            try
            {
                File.Delete(fontPath);
                _log.WriteLog($"Removed font file: {fileName}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Typically held open by a running app, so have Windows delete it on the next restart
                if (!FontNative.MoveFileEx(fontPath, null, MoveFileDelayUntilReboot))
                {
                    throw new IOException($"Could not delete font file {fontPath}: {ex.Message}", ex);
                }
                _log.WriteLog($"Font file {fileName} is in use, it will be deleted on the next restart.", "FontExecEvent", Log.Severity.Warning);
            }
        }

        // Removes every value in the Fonts key that points at fileName (the value data is the file name for
        // fonts in the Fonts folder, or a full path), except keepValueName.
        private void RemoveRegistryEntriesForFile(RegistryKey fontsKey, string fileName, string? keepValueName)
        {
            foreach (string valueName in fontsKey.GetValueNames())
            {
                if (keepValueName != null && valueName.Equals(keepValueName, StringComparison.OrdinalIgnoreCase)) continue;
                if (fontsKey.GetValue(valueName) is not string data) continue;
                if (Path.GetFileName(data).Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                    (!Path.IsPathRooted(data) || Path.GetDirectoryName(data)?.TrimEnd('\\').Equals(FontsDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true))
                {
                    fontsKey.DeleteValue(valueName, false);
                    _log.WriteLog($"Removed font registration: {valueName}");
                }
            }
        }

        private static bool IsFontFile(string path) => SystemFont.IsFontFile(path);

        private static void BroadcastFontChange()
        {
            // Best effort - lets running apps know the font list changed
            FontNative.SendMessageTimeout(HwndBroadcast, WmFontChange, IntPtr.Zero, IntPtr.Zero, 0x2, 1000, out _);
        }

        private static class FontNative
        {
            [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "AddFontResourceW")]
            public static extern int AddFontResource(string path);

            [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RemoveFontResourceW")]
            public static extern bool RemoveFontResource(string path);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
            public static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);

            [DllImport("user32.dll", SetLastError = true)]
            public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        }
    }
}
