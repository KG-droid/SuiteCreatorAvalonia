namespace SuiteTools
{
    public class EnvironmentTools
    {
        public static void NotifySystemSettingsChanged()
        {
            IntPtr HWND_BROADCAST = new IntPtr(0xffff);
            const int WM_SETTINGCHANGE = 0x001A;
            const int SMTO_ABORTIFHUNG = 0x0002;
            NativeMethods.SHChangeNotify(
                    0x8000000,
                    0x1000,
                    IntPtr.Zero,
                    IntPtr.Zero);
            NativeMethods.SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, null, SMTO_ABORTIFHUNG, 100, IntPtr.Zero);
            NativeMethods.SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 100, IntPtr.Zero);
        }
    }
}
