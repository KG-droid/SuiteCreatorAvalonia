using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SuiteUserPopup.Services
{
    internal enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2,
    }

    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2,
    }

    internal enum AudioSessionState
    {
        Inactive = 0,
        Active = 1,
        Expired = 2,
    }

    // Only the vtable-order-preserving members actually needed are given accurate signatures; earlier
    // members we never call (EnumAudioEndpoints, GetAudioSessionControl, GetSimpleAudioVolume) are declared
    // with placeholder pointer-returning signatures purely to keep the interfaces' method order matching
    // the real COM vtable - GeneratedComInterface marshals by declaration position, not by name.
    //
    // Every method below is [PreserveSig]: without it, GeneratedComInterface treats any non-zero HRESULT as
    // a thrown COMException instead of handing back the raw int, which broke the manual error handling this
    // code relies on (e.g. GetDefaultAudioEndpoint legitimately returning "not found" when no default
    // microphone endpoint exists - that's a normal "not in use" case, not something that should throw).
    [GeneratedComInterface]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal partial interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    }

    [GeneratedComInterface]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal partial interface IMMDevice
    {
        [PreserveSig]
        int Activate(in Guid iid, uint dwClsCtx, IntPtr pActivationParams, out IAudioSessionManager2 ppInterface);
    }

    [GeneratedComInterface]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    internal partial interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(IntPtr audioSessionGuid, int streamFlags, out IntPtr sessionControl);
        [PreserveSig]
        int GetSimpleAudioVolume(IntPtr audioSessionGuid, int streamFlags, out IntPtr simpleAudioVolume);
        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
    }

    [GeneratedComInterface]
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
    internal partial interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);
        [PreserveSig]
        int GetSession(int sessionIndex, out IAudioSessionControl session);
    }

    [GeneratedComInterface]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    internal partial interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out AudioSessionState state);
    }

    // Reports whether any app currently has a live (not merely permitted) capture stream open on the
    // default microphone - via WASAPI's per-session AudioSessionState, not the mic-access consent store.
    // Conferencing apps virtually always keep the capture stream running while the user is "muted" in a
    // call (so local features like live captions / "you're on mute" nudges keep working), so a
    // software-muted mic still reports Active here.
    internal static partial class MicrophoneActivityDetector
    {
        private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid IidIMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
        private static readonly Guid IidIAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        private const uint ClsCtxAll = 0x17;
        private const uint CoinitApartmentThreaded = 0x2;
        private const int RpcEChangedMode = unchecked((int)0x80010106);

        [LibraryImport("ole32.dll")]
        private static partial int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [LibraryImport("ole32.dll")]
        private static partial void CoUninitialize();

        [LibraryImport("ole32.dll")]
        private static partial int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IMMDeviceEnumerator ppv);

        public static bool IsMicrophoneInUse()
        {
            // The GeneratedComInterface/LibraryImport interop path doesn't auto-initialize COM on the calling
            // thread the way legacy COM interop used to (that used to happen implicitly via [STAThread] on
            // Main) - without this, CoCreateInstance below fails with CO_E_NOTINITIALIZED. S_OK (0) means we
            // initialized it here; S_FALSE (1) means it was already initialized (e.g. by the runtime honoring
            // [STAThread]) - both are fine and both mean we own a reference to release via CoUninitialize.
            // RPC_E_CHANGED_MODE means some other in-process code already initialized COM in the opposite
            // apartment mode, which is a real conflict we can't paper over here.
            int initHr = CoInitializeEx(IntPtr.Zero, CoinitApartmentThreaded);
            if (initHr == RpcEChangedMode)
            {
                throw new InvalidOperationException($"COM was already initialized on this thread in an incompatible mode, HRESULT: 0x{initHr:X8}");
            }
            bool comInitializedHere = initHr >= 0;

            try
            {
                return CheckMicrophoneSessions();
            }
            finally
            {
                if (comInitializedHere)
                {
                    CoUninitialize();
                }
            }
        }

        private static bool CheckMicrophoneSessions()
        {
            int createHr = CoCreateInstance(ClsidMMDeviceEnumerator, IntPtr.Zero, ClsCtxAll, IidIMMDeviceEnumerator, out IMMDeviceEnumerator enumerator);
            if (createHr != 0 || enumerator == null)
            {
                throw new InvalidOperationException($"Failed to create MMDeviceEnumerator, HRESULT: 0x{createHr:X8}");
            }

            int endpointHr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out IMMDevice device);
            if (endpointHr != 0 || device == null)
            {
                // No default capture device (e.g. no microphone present/enabled) - can't be in use.
                return false;
            }

            int activateHr = device.Activate(IidIAudioSessionManager2, ClsCtxAll, IntPtr.Zero, out IAudioSessionManager2 sessionManager);
            if (activateHr != 0 || sessionManager == null)
            {
                throw new InvalidOperationException($"Failed to activate IAudioSessionManager2 on the default capture device, HRESULT: 0x{activateHr:X8}");
            }

            int sessionEnumHr = sessionManager.GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
            if (sessionEnumHr != 0 || sessionEnumerator == null)
            {
                throw new InvalidOperationException($"Failed to get the audio session enumerator, HRESULT: 0x{sessionEnumHr:X8}");
            }

            int countHr = sessionEnumerator.GetCount(out int sessionCount);
            if (countHr != 0)
            {
                throw new InvalidOperationException($"Failed to get the audio session count, HRESULT: 0x{countHr:X8}");
            }

            for (int i = 0; i < sessionCount; i++)
            {
                if (sessionEnumerator.GetSession(i, out IAudioSessionControl session) != 0 || session == null)
                {
                    continue;
                }

                if (session.GetState(out AudioSessionState state) == 0 && state == AudioSessionState.Active)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
