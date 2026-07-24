using Avalonia.Platform.Storage;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Models.Common
{
    internal class SysIOPickerTypes
    {
        public static List<FilePickerFileType> Any { get; } = new()
        {
            new ("Any")
                {
                    Patterns = new[] { "*.*" },
                }
        };
        public static List<FilePickerFileType> PowerShell { get; } = new()
        {
            new ("PowerShell")
                {
                    Patterns = new[] { "*.ps1" },
                }
        };
        public static List<FilePickerFileType> MSI { get; } = new()
        {
            new ("MSI")
                {
                    Patterns = new[] { "*.msi" },
                }
        };
        public static List<FilePickerFileType> MSP { get; } = new()
        {
            new ("MSP")
                {
                    Patterns = new[] { "*.msp" },
                }
        };
        public static List<FilePickerFileType> MST { get; } = new()
        {
            new ("MST")
                {
                    Patterns = new[] { "*.mst" },
                }
        };
        public static List<FilePickerFileType> MSIx { get; } = new()
        {
            new ("MSIx")
                {
                    Patterns = new[] { "*.msix" },
                }
        };
        public static List<FilePickerFileType> Certificate { get; } = new()
        {
            new ("Certificate")
                {
                    Patterns = new[] { "*.cer", "*.pfx" },
                }
        };

        public static List<FilePickerFileType> Inf { get; } = new()
        {
            new ("Driver INF")
                {
                    Patterns = new[] { "*.inf" },
                }
        };

        public static List<FilePickerFileType> CRX { get; } = new()
        {
            new ("CRX")
                {
                    Patterns = new[] { "*.crx", "*.crx" },
                }
        };

        public static List<FilePickerFileType> Reg { get; } = new()
        {
            new ("Reg")
                {
                    Patterns = new[] { "*.reg", "*.reg" },
                }
        };

        public static List<FilePickerFileType> Image { get; } = new()
        {
            new ("Image")
                {
                    Patterns = new[] { "*.png", "*.jpeg", "*.bmp" },
                }
        };

        public static List<FilePickerFileType> ImageIncGif { get; } = new()
        {
            new ("ImageIncGif")
                {
                    Patterns = new[] { "*.png", "*.jpeg", "*.bmp", "*.gif" },
                }
        };

        public static List<FilePickerFileType> SuiteProject { get; } = new()
        {
            new ("SuiteProject")
                {
                    Patterns = new[] { "*.scp" },
                }
        };

        public static List<FilePickerFileType> SuiteExecConfig { get; } = new()
        {
            new ("SuiteExecConfig")
                {
                    Patterns = new[] { "*.scfg" },
                }
        };

        public static List<FilePickerFileType> SuiteExe { get; } = new()
        {
            new ("SuiteExe")
                {
                    Patterns = new[] { "*.exe" },
                }
        };

        public static List<FilePickerFileType> Icon { get; } = new()
        {
            new ("Icon")
                {
                    Patterns = new[] { "*.ico" },
                }
        };

        public static List<FilePickerFileType> Json { get; } = new()
        {
            new ("JSON")
                {
                    Patterns = new[] { "*.json" },
                }
        };
    }
}
