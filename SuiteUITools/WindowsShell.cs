using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SuiteCreatorAvalonia.Tools
{
    public class WindowsShell
    {
        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_SMALLICON = 0x000000001;

        private class NativeMethods
        {
            [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern int SHGetFileInfo(string pszPath, uint dwFileAttributes, ref NativeHelpers.SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

            [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr DestroyIcon(IntPtr hIcon);

            [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
            internal static extern int ExtractIconEx(string sFile, int iIndex, out IntPtr piLargeVersion, out IntPtr piSmallVersion, int amountIcons);
        }

        private class NativeHelpers
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct SHFILEINFO
            {
                public IntPtr hIcon;
                public int iIcon;
                public uint dwAttributes;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szDisplayName;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
                public string szTypeName;
            }
        }

        public static Avalonia.Media.Imaging.Bitmap? GetIconFromShell(string filePath, bool largeIcon)
        {
            NativeHelpers.SHFILEINFO shinfo = new NativeHelpers.SHFILEINFO();
            try
            {
                if (!Path.Exists(filePath)) { return null; }
                uint flags = SHGFI_ICON | (largeIcon ? SHGFI_LARGEICON : SHGFI_SMALLICON);
                int result = NativeMethods.SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
                if (result == 0 || shinfo.hIcon == IntPtr.Zero) { return null; }
                using (Icon icon = Icon.FromHandle(shinfo.hIcon))
                {
                    using (System.Drawing.Bitmap bmp = icon.ToBitmap())
                    {
                        using (MemoryStream stream = new())
                        {
                            bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                            stream.Seek(0, SeekOrigin.Begin);
                            return new Avalonia.Media.Imaging.Bitmap(stream);
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shinfo.hIcon != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(shinfo.hIcon); // Ensure the icon handle is released
                }
            }
        }

        public static Avalonia.Media.Imaging.Bitmap? ExtractIconFromExecutable(string filePath, int index, bool largeIcon)
        {
            IntPtr large = IntPtr.Zero;
            IntPtr small = IntPtr.Zero;
            try
            {
                NativeMethods.ExtractIconEx(filePath, index, out large, out small, 1);
                IntPtr iconHandle = largeIcon ? large : small;
                if (iconHandle == IntPtr.Zero)
                    return null;

                // Convert to Avalonia Bitmap
                using var iconStream = new MemoryStream();
                System.Drawing.Bitmap bitmap = Icon.FromHandle(iconHandle).ToBitmap();
                using (var memoryStream = new MemoryStream())
                {
                    bitmap.Save(memoryStream, ImageFormat.Png);
                    memoryStream.Position = 0;
                    Avalonia.Media.Imaging.Bitmap image = new(memoryStream);
                    return image;
                }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (large != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(large);
                }
                if (small != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(small);
                }
            }
        }

        public static List<Avalonia.Media.Imaging.Bitmap>? ExtractAllIconsFromExecutable(string filePath, bool largeIcon)
        {
            List<Avalonia.Media.Imaging.Bitmap> bitmaps = new();
            Avalonia.Media.Imaging.Bitmap? icon = null;
            do
            {
                icon = ExtractIconFromExecutable(filePath, bitmaps.Count, largeIcon);
                if (icon != null)
                {
                    bitmaps.Add(icon);
                }
            } while (icon != null);
            if (bitmaps.Count > 0)
                return bitmaps;
            else
                return null;
        }
    }
}
