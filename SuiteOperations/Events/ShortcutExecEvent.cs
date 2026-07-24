using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using System.Runtime.InteropServices;
using Environment = System.Environment;

namespace SuiteOperations.Events
{
    public partial class ShortcutExecEvent : Shortcut
    {
        private Log _log;

        public ShortcutExecEvent(Log log)
        {
            _log = log;
        }

        public ShortcutExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void ExecuteEvent()
        {
            if (ShortAction == null)
            {
                _log.WriteLog("No shortcut action specified.");
                throw new InvalidOperationException("No shortcut action specified.");
            }
            switch (ShortAction)
            {
                case ShortcutAction.Create:
                    CreateShortcut();
                    break;
                case ShortcutAction.Delete:
                    DeleteShortcut();
                    break;
                default:
                    _log.WriteLog($"Unknown shortcut action: {ShortAction}");
                    throw new InvalidOperationException($"Unknown shortcut action: {ShortAction}");
            }
        }

        private void CreateShortcut()
        {
            if (PlacementList == null || PlacementList.Count == 0)
            {
                throw new Exception("No shortcut placement specified.");
            }
            if (string.IsNullOrWhiteSpace(Name) || Target == null)
            {
                throw new Exception("Shortcut Name or Target is missing.");
            }
            foreach (ShortcutPlacement placement in PlacementList)
            {
                string shortcutPath = GetShortcutPath(placement);
                if (ShortcutType == SuiteCreatorAvalonia.Enums.ShortcutType.Web)
                {
                    CreateWebShortcut(shortcutPath);
                }
                else
                {
                    CreateShortcutWithShellLink(shortcutPath);
                }
                _log.WriteLog($"Created shortcut '{Name}' at {shortcutPath}", nameof(ShortcutExecEvent));
            }
        }

        private void CreateWebShortcut(string shortcutPath)
        {
            string content = $"[InternetShortcut]\r\nURL={Target!.ToString()}\r\n";
            if (!string.IsNullOrWhiteSpace(IconPath))
                content += $"IconFile={IconPath}\r\nIconIndex={IconIndex}\r\n";
            File.WriteAllText(shortcutPath, content, System.Text.Encoding.UTF8);
        }

        private unsafe void CreateShortcutWithShellLink(string shortcutPath)
        {
            string targetPath = Target!.LocalPath;
            string? arguments = string.IsNullOrWhiteSpace(Arguments) ? null : Arguments;
            string? workingDirectory = string.IsNullOrWhiteSpace(WorkingDIR?.LocalPath) ? null : WorkingDIR.LocalPath;
            string? iconPath = string.IsNullOrWhiteSpace(IconPath) ? null : IconPath;

            int initializeResult = NativeComMethods.CoInitializeEx(IntPtr.Zero, NativeComMethods.COINIT_APARTMENTTHREADED);
            bool shouldUninitialize = initializeResult >= 0 && initializeResult != NativeComMethods.RPC_E_CHANGED_MODE;
            nint shellLinkPointer = IntPtr.Zero;
            nint persistFilePointer = IntPtr.Zero;

            try
            {
                Guid shellLinkClassId = NativeComMethods.CLSID_ShellLink;
                Guid shellLinkInterfaceId = NativeComMethods.IID_IShellLinkW;

                int createResult = NativeComMethods.CoCreateInstance(&shellLinkClassId, IntPtr.Zero, NativeComMethods.CLSCTX_INPROC_SERVER, &shellLinkInterfaceId, out shellLinkPointer);
                ThrowIfFailed(createResult, "Creating Shell Link COM instance");

                ShellLinkVTable* shellLinkVTable = *(ShellLinkVTable**)shellLinkPointer;

                fixed (char* targetPathPointer = targetPath)
                {
                    int setPathResult = shellLinkVTable->SetPath(shellLinkPointer, targetPathPointer);
                    ThrowIfFailed(setPathResult, "Setting shortcut target path");
                }

                if (!string.IsNullOrWhiteSpace(arguments))
                {
                    fixed (char* argumentsPointer = arguments)
                    {
                        int setArgumentsResult = shellLinkVTable->SetArguments(shellLinkPointer, argumentsPointer);
                        ThrowIfFailed(setArgumentsResult, "Setting shortcut arguments");
                    }
                }

                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    fixed (char* workingDirectoryPointer = workingDirectory)
                    {
                        int setWorkingDirectoryResult = shellLinkVTable->SetWorkingDirectory(shellLinkPointer, workingDirectoryPointer);
                        ThrowIfFailed(setWorkingDirectoryResult, "Setting shortcut working directory");
                    }
                }

                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    fixed (char* iconPathPointer = iconPath)
                    {
                        int setIconLocationResult = shellLinkVTable->SetIconLocation(shellLinkPointer, iconPathPointer, IconIndex);
                        ThrowIfFailed(setIconLocationResult, "Setting shortcut icon");
                    }
                }

                Guid persistFileInterfaceId = NativeComMethods.IID_IPersistFile;
                int queryInterfaceResult = shellLinkVTable->QueryInterface(shellLinkPointer, &persistFileInterfaceId, &persistFilePointer);
                ThrowIfFailed(queryInterfaceResult, "Querying IPersistFile");

                PersistFileVTable* persistFileVTable = *(PersistFileVTable**)persistFilePointer;
                fixed (char* shortcutPathPointer = shortcutPath)
                {
                    int saveResult = persistFileVTable->Save(persistFilePointer, shortcutPathPointer, 1);
                    ThrowIfFailed(saveResult, "Saving shortcut file");
                }
            }
            finally
            {
                if (persistFilePointer != IntPtr.Zero)
                {
                    PersistFileVTable* persistFileVTable = *(PersistFileVTable**)persistFilePointer;
                    _ = persistFileVTable->Release(persistFilePointer);
                }

                if (shellLinkPointer != IntPtr.Zero)
                {
                    ShellLinkVTable* shellLinkVTable = *(ShellLinkVTable**)shellLinkPointer;
                    _ = shellLinkVTable->Release(shellLinkPointer);
                }

                if (shouldUninitialize)
                {
                    NativeComMethods.CoUninitialize();
                }
            }
        }

        private static void ThrowIfFailed(int hResult, string operation)
        {
            if (hResult >= 0)
            {
                return;
            }

            Exception exception = Marshal.GetExceptionForHR(hResult) ?? new InvalidOperationException($"{operation} failed with HRESULT 0x{hResult:X8}.");
            throw new InvalidOperationException($"{operation} failed with HRESULT 0x{hResult:X8}.", exception);
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct ShellLinkVTable
        {
            public delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int> QueryInterface;
            public delegate* unmanaged[Stdcall]<nint, uint> AddRef;
            public delegate* unmanaged[Stdcall]<nint, uint> Release;
            public nint GetPath;
            public nint GetIDList;
            public nint SetIDList;
            public nint GetDescription;
            public nint SetDescription;
            public nint GetWorkingDirectory;
            public delegate* unmanaged[Stdcall]<nint, char*, int> SetWorkingDirectory;
            public nint GetArguments;
            public delegate* unmanaged[Stdcall]<nint, char*, int> SetArguments;
            public nint GetHotkey;
            public nint SetHotkey;
            public nint GetShowCmd;
            public nint SetShowCmd;
            public nint GetIconLocation;
            public delegate* unmanaged[Stdcall]<nint, char*, int, int> SetIconLocation;
            public nint SetRelativePath;
            public nint Resolve;
            public delegate* unmanaged[Stdcall]<nint, char*, int> SetPath;
        }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct PersistFileVTable
        {
            public delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int> QueryInterface;
            public delegate* unmanaged[Stdcall]<nint, uint> AddRef;
            public delegate* unmanaged[Stdcall]<nint, uint> Release;
            public nint GetClassId;
            public nint IsDirty;
            public nint Load;
            public delegate* unmanaged[Stdcall]<nint, char*, int, int> Save;
            public nint SaveCompleted;
            public nint GetCurFile;
        }

        private static class NativeComMethods
        {
            public const uint CLSCTX_INPROC_SERVER = 0x1;
            public const uint COINIT_APARTMENTTHREADED = 0x2;
            public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);
            public static readonly Guid CLSID_ShellLink = new Guid("00021401-0000-0000-C000-000000000046");
            public static readonly Guid IID_IShellLinkW = new Guid("000214F9-0000-0000-C000-000000000046");
            public static readonly Guid IID_IPersistFile = new Guid("0000010B-0000-0000-C000-000000000046");

            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern unsafe int CoCreateInstance(Guid* rclsid, nint pUnkOuter, uint dwClsContext, Guid* riid, out nint ppv);

            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

            [DllImport("ole32.dll", ExactSpelling = true)]
            public static extern void CoUninitialize();
        }

        private void DeleteShortcut()
        {
            if (PlacementList == null || PlacementList.Count == 0)
            {
                _log.WriteLog("No shortcut placement specified.", nameof(ShortcutExecEvent), Log.Severity.Error);
                throw new InvalidOperationException("No shortcut placement specified.");
            }
            if (string.IsNullOrWhiteSpace(Name))
            {
                _log.WriteLog("Shortcut Name is missing.", nameof(ShortcutExecEvent), Log.Severity.Error);
                throw new InvalidOperationException("Shortcut Name is missing.");
            }
            foreach (ShortcutPlacement placement in PlacementList)
            {
                string shortcutPath = GetShortcutPath(placement);
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    _log.WriteLog($"Deleted shortcut '{Name}' at {shortcutPath}", nameof(ShortcutExecEvent));
                }
                else
                {
                    _log.WriteLog($"Shortcut '{Name}' not found at {shortcutPath}", nameof(ShortcutExecEvent), Log.Severity.Error);
                    throw new FileNotFoundException($"Shortcut '{Name}' not found at {shortcutPath}", shortcutPath);
                }
            }
        }

        private string GetShortcutPath(ShortcutPlacement placement)
        {
            bool isUser = Context == Contexts.User;
            string folder = placement switch
            {
                ShortcutPlacement.Desktop => isUser
                    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                ShortcutPlacement.StartMenu => isUser
                    ? Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
                    : Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                _ => isUser
                    ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                    : Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };
            string extension = ShortcutType == SuiteCreatorAvalonia.Enums.ShortcutType.Web ? ".url" : ".lnk";
            return Path.Combine(folder, $"{Name}{extension}");
        }
    }
}
