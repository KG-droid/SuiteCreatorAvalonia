using Microsoft.Win32;

namespace SuiteCreatorAvalonia.Tools
{
    public class WebBrowser
    {
        public static string? GetDefaultBrowserPath()
        {
            string? browserPath = null;

            // Check for user choice first
            using (RegistryKey? userChoiceKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"))
            {
                if (userChoiceKey != null)
                {
                    string? progId = userChoiceKey.GetValue("ProgId") as string;
                    if (!string.IsNullOrEmpty(progId))
                    {
                        browserPath = GetBrowserPathFromProgId(progId);
                        if (!string.IsNullOrEmpty(browserPath))
                            return browserPath;
                    }
                }
            }

            // then Fallback to system-level default
            using (RegistryKey? progIdKey = Registry.ClassesRoot.OpenSubKey(@"http\shell\open\command"))
            {
                if (progIdKey != null)
                {
                    var command = progIdKey.GetValue(null) as string;
                    browserPath = ExtractPathFromCommand(command);
                }
            }

            return browserPath;
        }

        private static string? GetBrowserPathFromProgId(string progId)
        {
            using (RegistryKey? progIdKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command"))
            {
                if (progIdKey != null)
                {
                    var command = progIdKey.GetValue(null) as string;
                    return ExtractPathFromCommand(command);
                }
            }
            return null;
        }

        private static string? ExtractPathFromCommand(string? command)
        {
            if (string.IsNullOrEmpty(command))
                return null;

            int exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex != -1)
            {
                return command.Substring(0, exeIndex + 4).Trim('"');
            }
            return null;
        }
    }
}

