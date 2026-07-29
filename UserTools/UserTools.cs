using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SuiteTools
{
    public class UserTools
    {
        public class NativeHelpers
        {
            // Session state enum
            public enum WTS_CONNECTSTATE_CLASS
            {
                WTSActive,
                WTSConnected,
                WTSConnectQuery,
                WTSShadow,
                WTSDisconnected,
                WTSIdle,
                WTSListen,
                WTSReset,
                WTSDown,
                WTSInit
            }

            // Session info struct
            [StructLayout(LayoutKind.Sequential)]
            public struct WTS_SESSION_INFO
            {
                public int SessionID;
                [MarshalAs(UnmanagedType.LPWStr)]
                public string pWinStationName;
                public WTS_CONNECTSTATE_CLASS State;
            }

            internal enum WTS_INFO_CLASS
            {
                WTSUserName = 5,
                WTSDomainName = 7
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct STARTUPINFO
            {
                public int cb;
                public string lpReserved;
                public string lpDesktop;
                public string lpTitle;
                public int dwX;
                public int dwY;
                public int dwXSize;
                public int dwYSize;
                public int dwXCountChars;
                public int dwYCountChars;
                public int dwFillAttribute;
                public int dwFlags;
                public short wShowWindow;
                public short cbReserved2;
                public IntPtr lpReserved2;
                public IntPtr hStdInput;
                public IntPtr hStdOutput;
                public IntPtr hStdError;
            }
            [StructLayout(LayoutKind.Sequential)]
            internal struct PROCESS_INFORMATION
            {
                public IntPtr hProcess;
                public IntPtr hThread;
                public int dwProcessId;
                public int dwThreadId;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct SECURITY_ATTRIBUTES
            {
                public int nLength;
                public IntPtr lpSecurityDescriptor;
                public bool bInheritHandle;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize;
                public UIntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public UIntPtr ProcessMemoryLimit;
                public UIntPtr JobMemoryLimit;
                public UIntPtr PeakProcessMemoryUsed;
                public UIntPtr PeakJobMemoryUsed;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct TOKEN_PRIVILEGES
            {
                public uint PrivilegeCount;
                public NativeMethods.LUID Luid;
                public uint Attributes;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct PROFILEINFO
            {
                public int dwSize;
                public int dwFlags;
                public string lpUserName;
                public string? lpProfilePath;
                public string? lpDefaultPath;
                public string? lpServerName;
                public string? lpPolicyPath;
                public IntPtr hProfile;
            }
        }

        internal class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

            [DllImport("Wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern bool WTSEnumerateSessions(
                IntPtr hServer,
                int Reserved,
                int Version,
                out IntPtr ppSessionInfo,
                out int pCount
            );

            [DllImport("Wtsapi32.dll")]
            internal static extern void WTSFreeMemory(IntPtr pMemory);

            [DllImport("Wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern bool WTSQuerySessionInformation(
                IntPtr hServer,
                int sessionId,
                NativeHelpers.WTS_INFO_CLASS wtsInfoClass,
                out IntPtr ppBuffer,
                out int pBytesReturned
            );

            [DllImport("Wtsapi32.dll", SetLastError = true)]
            internal static extern bool WTSQueryUserToken(int SessionId, out SafeNativeHandle phToken);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern bool CreateProcessAsUserW(
                IntPtr hToken,
                string lpApplicationName,
                string lpCommandLine,
                IntPtr lpProcessAttributes,
                IntPtr lpThreadAttributes,
                bool bInheritHandles,
                uint dwCreationFlags,
                IntPtr lpEnvironment,
                string lpCurrentDirectory,
                ref NativeHelpers.STARTUPINFO lpStartupInfo,
                out NativeHelpers.PROCESS_INFORMATION lpProcessInformation);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool DuplicateTokenEx(
                IntPtr hExistingToken,
                uint dwDesiredAccess,
                IntPtr lpTokenAttributes,
                int ImpersonationLevel,
                int TokenType,
                out SafeNativeHandle phNewToken);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool CloseHandle(IntPtr hObject);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool ImpersonateLoggedOnUser(IntPtr hToken);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool RevertToSelf();

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool CreatePipe(
                out IntPtr hReadPipe,
                out IntPtr hWritePipe,
                ref NativeHelpers.SECURITY_ATTRIBUTES lpPipeAttributes,
                uint nSize);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeNativeHandle tokenHandle);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool PrivilegeCheck(IntPtr clientToken, ref PRIVILEGE_SET requiredPrivileges, out bool pfResult);

            [StructLayout(LayoutKind.Sequential)]
            internal struct LUID
            {
                public uint LowPart;
                public int HighPart;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct LUID_AND_ATTRIBUTES
            {
                public LUID Luid;
                public uint Attributes;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct PRIVILEGE_SET
            {
                public uint PrivilegeCount;
                public uint Control;
                public LUID_AND_ATTRIBUTES Privilege;
            }

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, ref NativeHelpers.JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

            [DllImport("advapi32.dll", SetLastError = true)]
            internal static extern bool AdjustTokenPrivileges(
                IntPtr tokenHandle,
                bool disableAllPrivileges,
                ref NativeHelpers.TOKEN_PRIVILEGES newState,
                uint bufferLength,
                IntPtr previousState,
                IntPtr returnLength);

            [DllImport("userenv.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            internal static extern bool LoadUserProfile(IntPtr hToken, ref NativeHelpers.PROFILEINFO lpProfileInfo);

            [DllImport("userenv.dll", SetLastError = true)]
            internal static extern bool UnloadUserProfile(IntPtr hToken, IntPtr hProfile);

            [DllImport("userenv.dll", SetLastError = true)]
            internal static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

            [DllImport("userenv.dll", SetLastError = true)]
            internal static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);
        }

        public sealed class SafeNativeHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            public SafeNativeHandle() : base(true) { }
            public SafeNativeHandle(IntPtr handle) : base(true) { SetHandle(handle); }
            protected override bool ReleaseHandle()
            {
                return NativeMethods.CloseHandle(handle);
            }
        }

        public class ProcessExtensions
        {
            private static SafeNativeHandle? DuplicateToPrimaryToken(SafeNativeHandle impersonationToken)
            {
                const uint TOKEN_ALL_ACCESS = 0xF01FF;
                const int SecurityImpersonation = 2;
                const int TokenPrimary = 1;
                SafeNativeHandle primaryToken;
                if (!NativeMethods.DuplicateTokenEx(
                        impersonationToken.DangerousGetHandle(),
                        TOKEN_ALL_ACCESS,
                        IntPtr.Zero,
                        SecurityImpersonation,
                        TokenPrimary,
                        out primaryToken) || primaryToken.IsInvalid)
                {
                    return null;
                }
                return primaryToken;
            }

            private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
            private const int JobObjectExtendedLimitInformation = 9;
            private static IntPtr _childProcessJobHandle = IntPtr.Zero;
            private static readonly object _childProcessJobLock = new object();

            // Lazily creates a Job Object with KILL_ON_JOB_CLOSE set. The only handle to this job is held by
            // this process, so Windows automatically closes it - killing every process assigned to it (e.g. the
            // progress popup) - the moment this process terminates for any reason, including a crash or being
            // forcibly killed, not just on a clean/graceful exit.
            private static IntPtr GetChildProcessJobHandle()
            {
                if (_childProcessJobHandle != IntPtr.Zero)
                    return _childProcessJobHandle;

                lock (_childProcessJobLock)
                {
                    if (_childProcessJobHandle != IntPtr.Zero)
                        return _childProcessJobHandle;

                    IntPtr jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                    if (jobHandle == IntPtr.Zero)
                        return IntPtr.Zero;

                    NativeHelpers.JOBOBJECT_EXTENDED_LIMIT_INFORMATION limitInfo = new NativeHelpers.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    limitInfo.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    uint infoSize = (uint)Marshal.SizeOf<NativeHelpers.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                    if (!NativeMethods.SetInformationJobObject(jobHandle, JobObjectExtendedLimitInformation, ref limitInfo, infoSize))
                    {
                        NativeMethods.CloseHandle(jobHandle);
                        return IntPtr.Zero;
                    }

                    _childProcessJobHandle = jobHandle;
                    return _childProcessJobHandle;
                }
            }

            // Ties the lifetime of a launched child process (e.g. the progress popup) to this process, so it
            // cannot be left running as an orphan if this process exits unexpectedly.
            private static void EnsureProcessDiesWithParent(IntPtr hProcess)
            {
                try
                {
                    if (hProcess == IntPtr.Zero)
                        return;

                    IntPtr jobHandle = GetChildProcessJobHandle();
                    if (jobHandle != IntPtr.Zero)
                    {
                        NativeMethods.AssignProcessToJobObject(jobHandle, hProcess);
                    }
                }
                catch
                {
                    // Best-effort: failing to link the child's lifetime to ours should not prevent it from running.
                }
            }

            public class ImpersonatedProcessResult
            {
                public int ExitCode { get; set; }
                public string? StandardOutput { get; set; }
                public string? StandardError { get; set; }
                public string? ErrorMessage { get; set; }
                public string? UserSid { get; set; }
                public string? UserName { get; set; }
            }

            // Duplicates the session's user token, launches exePath under it via CreateProcessAsUser, and
            // captures stdout/stderr/exit code. Shared by StartProcessAsAllUsers and StartProcessAsCurrentUser
            // so the two entry points can't drift apart on how the process is actually created.
            private static ImpersonatedProcessResult RunProcessInSession(
                UserExtensions.UserSessionInfo session,
                string exePath,
                string? arguments,
                string workingDirectory,
                bool isWindowVisibleToUser,
                bool wait,
                TimeSpan? waitTimeout,
                bool? killWithParent,
                Action<string, string?>? onOutputLine = null)
            {
                // Ensure a healthy state for permission to impersonate.
                NativeMethods.RevertToSelf();
                EnablePrivilege("SeAssignPrimaryTokenPrivilege");
                EnablePrivilege("SeIncreaseQuotaPrivilege");

                SafeNativeHandle userToken;
                if (!NativeMethods.WTSQueryUserToken(session.SessionID, out userToken) || userToken.IsInvalid)
                    return new ImpersonatedProcessResult { ExitCode = -1, ErrorMessage = $"Failed to get user token for session {session.SessionID}.", UserSid = session.UserSid?.ToString(), UserName = session.UserName };

                using (userToken)
                {
                    using (SafeNativeHandle? primaryToken = DuplicateToPrimaryToken(userToken))
                    {
                        if (primaryToken == null || primaryToken.IsInvalid)
                            return new ImpersonatedProcessResult { ExitCode = -1, ErrorMessage = $"Failed to duplicate token for session {session.SessionID}.", UserSid = session.UserSid?.ToString(), UserName = session.UserName };

                        // Mount HKEY_CURRENT_USER for this token so apps that touch the profile/registry
                        // during startup (Windows Installer, PowerShell ISE, etc.) don't fail outright.
                        IntPtr userProfileHandle = string.IsNullOrEmpty(session.UserName)
                            ? IntPtr.Zero
                            : LoadUserProfileForToken(primaryToken, session.UserName);

                        // Build the target user's own environment block (%TEMP%, %APPDATA%, %USERPROFILE%,
                        // etc.) rather than letting the child inherit our SYSTEM service's environment -
                        // otherwise apps that write scratch/log files to %TEMP% during startup try to write
                        // to a path the impersonated user's token has no access to.
                        bool hasEnvironmentBlock = NativeMethods.CreateEnvironmentBlock(out IntPtr environmentBlock, primaryToken.DangerousGetHandle(), false);
                        try
                        {
                            NativeHelpers.SECURITY_ATTRIBUTES pipeAttr = new NativeHelpers.SECURITY_ATTRIBUTES
                            {
                                nLength = Marshal.SizeOf<NativeHelpers.SECURITY_ATTRIBUTES>(),
                                lpSecurityDescriptor = IntPtr.Zero,
                                bInheritHandle = true
                            };

                            if (!NativeMethods.CreatePipe(out IntPtr stdoutRead, out IntPtr stdoutWrite, ref pipeAttr, 0))
                                return new ImpersonatedProcessResult { ExitCode = -1, ErrorMessage = $"Failed to create stdout pipe: {Marshal.GetLastWin32Error()}", UserSid = session.UserSid?.ToString(), UserName = session.UserName };
                            if (!NativeMethods.CreatePipe(out IntPtr stderrRead, out IntPtr stderrWrite, ref pipeAttr, 0))
                            {
                                NativeMethods.CloseHandle(stdoutRead);
                                NativeMethods.CloseHandle(stdoutWrite);
                                return new ImpersonatedProcessResult { ExitCode = -1, ErrorMessage = $"Failed to create stderr pipe: {Marshal.GetLastWin32Error()}", UserSid = session.UserSid?.ToString(), UserName = session.UserName };
                            }

                            // Make the read ends non-inheritable so only the child inherits the write ends
                            const uint HANDLE_FLAG_INHERIT = 0x1;
                            NativeMethods.SetHandleInformation(stdoutRead, HANDLE_FLAG_INHERIT, 0);
                            NativeMethods.SetHandleInformation(stderrRead, HANDLE_FLAG_INHERIT, 0);

                            NativeHelpers.STARTUPINFO si = new NativeHelpers.STARTUPINFO();
                            si.cb = Marshal.SizeOf(typeof(NativeHelpers.STARTUPINFO));
                            si.lpDesktop = "winsta0\\default";
                            si.dwFlags = 0x100;
                            si.wShowWindow = (short)(isWindowVisibleToUser ? 5 : 0);
                            si.hStdOutput = stdoutWrite;
                            si.hStdError = stderrWrite;
                            si.hStdInput = IntPtr.Zero;

                            NativeHelpers.PROCESS_INFORMATION pi = new NativeHelpers.PROCESS_INFORMATION();
                            // CREATE_NO_WINDOW only matters for console-subsystem targets (it suppresses their
                            // console); GUI apps ignore it and rely on wShowWindow instead. Still, honor
                            // isWindowVisibleToUser here so a visible console-subsystem target actually gets one.
                            uint creationFlags = isWindowVisibleToUser ? 0x00000010u : 0x08000000u; // CREATE_NEW_CONSOLE : CREATE_NO_WINDOW
                            if (hasEnvironmentBlock)
                                creationFlags |= 0x00000400; // CREATE_UNICODE_ENVIRONMENT
                            string commandLine = string.IsNullOrEmpty(arguments) ? $"\"{exePath}\"" : $"\"{exePath}\" {arguments}";
                            bool result = NativeMethods.CreateProcessAsUserW(
                                primaryToken.DangerousGetHandle(),
                                exePath,
                                commandLine,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                true,
                                creationFlags,
                                hasEnvironmentBlock ? environmentBlock : IntPtr.Zero,
                                workingDirectory,
                                ref si,
                                out pi
                            );

                            int err = result ? 0 : Marshal.GetLastWin32Error();

                            // Close the write ends in this process so ReadToEnd completes when the child exits
                            NativeMethods.CloseHandle(stdoutWrite);
                            NativeMethods.CloseHandle(stderrWrite);

                            if (!result)
                            {
                                NativeMethods.CloseHandle(stdoutRead);
                                NativeMethods.CloseHandle(stderrRead);
                                return new ImpersonatedProcessResult
                                {
                                    ExitCode = -1,
                                    ErrorMessage = $"Failed to start process for user {session.UserName} (Session {session.SessionID}): {err}, Command line: {commandLine}",
                                    UserSid = session.UserSid?.ToString(),
                                    UserName = session.UserName
                                };
                            }

                            if (killWithParent.HasValue && killWithParent.Value)
                            {
                                // Ensure this child is killed if this process ever exits, even via a crash
                                EnsureProcessDiesWithParent(pi.hProcess);
                            }

                            if (!wait)
                            {
                                // Fire-and-forget: don't block this call on the child's output, since the
                                // write ends stay open (and reads would block) until the child exits. Close
                                // our read ends without reading them - the child's own writes will simply
                                // fail once nobody is listening, which is expected for a detached process.
                                NativeMethods.CloseHandle(stdoutRead);
                                NativeMethods.CloseHandle(stderrRead);
                                if (pi.hProcess != IntPtr.Zero)
                                    NativeMethods.CloseHandle(pi.hProcess);
                                if (pi.hThread != IntPtr.Zero)
                                    NativeMethods.CloseHandle(pi.hThread);

                                return new ImpersonatedProcessResult
                                {
                                    ExitCode = -1,
                                    UserName = session.UserName,
                                    UserSid = session.UserSid?.ToString()
                                };
                            }

                            // Read stdout and stderr concurrently to prevent pipe buffer deadlock
                            using System.IO.FileStream stdoutStream = new System.IO.FileStream(new SafeFileHandle(stdoutRead, true), System.IO.FileAccess.Read, 4096, false);
                            using System.IO.FileStream stderrStream = new System.IO.FileStream(new SafeFileHandle(stderrRead, true), System.IO.FileAccess.Read, 4096, false);
                            using System.IO.StreamReader stdoutReader = new System.IO.StreamReader(stdoutStream);
                            using System.IO.StreamReader stderrReader = new System.IO.StreamReader(stderrStream);

                            Task<string> stdoutTask = ReadStreamWithLineCallbackAsync(
                                stdoutReader,
                                onOutputLine == null ? null : (Action<string>)(line => onOutputLine(line, session.UserName)));
                            Task<string> stderrTask = stderrReader.ReadToEndAsync();

                            try
                            {
                                int exitCode = -1;
                                if (pi.hProcess != IntPtr.Zero)
                                {
                                    uint waitMs;
                                    if (waitTimeout.HasValue)
                                    {
                                        double ms = waitTimeout.Value.TotalMilliseconds;
                                        if (ms > uint.MaxValue)
                                            waitMs = 0xFFFFFFFF;
                                        else if (ms < 0)
                                            waitMs = 0;
                                        else
                                            waitMs = (uint)ms;
                                    }
                                    else
                                    {
                                        waitMs = 0xFFFFFFFF; // INFINITE
                                    }
                                    uint waitResult = NativeMethods.WaitForSingleObject(pi.hProcess, waitMs);
                                    if (waitResult == 0xFFFFFFFF) // WAIT_FAILED
                                    {
                                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed.");
                                    }
                                    else if (waitResult == 0x00000102) // WAIT_TIMEOUT
                                    {
                                        // Don't leave the process running unattended just because we stopped waiting on it.
                                        NativeMethods.TerminateProcess(pi.hProcess, unchecked((uint)-1));
                                        // TerminateProcess only initiates termination and returns immediately - the
                                        // process (and handles it holds, e.g. its current directory) may not be
                                        // fully torn down yet. Wait for it to actually exit so callers that clean up
                                        // the working directory afterward (e.g. the SFX cache) don't race it.
                                        NativeMethods.WaitForSingleObject(pi.hProcess, 5000);
                                        throw new TimeoutException($"Process exceeded the time limit of {(waitTimeout.HasValue ? waitTimeout.Value.ToString() : "unknown")} and was terminated.");
                                    }
                                    NativeMethods.GetExitCodeProcess(pi.hProcess, out exitCode);
                                }

                                string stdOut = stdoutTask.GetAwaiter().GetResult();
                                string stdErr = stderrTask.GetAwaiter().GetResult();

                                return new ImpersonatedProcessResult
                                {
                                    ExitCode = exitCode,
                                    UserName = session.UserName,
                                    UserSid = session.UserSid?.ToString(),
                                    StandardOutput = stdOut,
                                    StandardError = stdErr
                                };
                            }
                            finally
                            {
                                if (pi.hProcess != IntPtr.Zero)
                                    NativeMethods.CloseHandle(pi.hProcess);
                                if (pi.hThread != IntPtr.Zero)
                                    NativeMethods.CloseHandle(pi.hThread);
                            }
                        }
                        finally
                        {
                            if (hasEnvironmentBlock && environmentBlock != IntPtr.Zero)
                                NativeMethods.DestroyEnvironmentBlock(environmentBlock);
                            if (userProfileHandle != IntPtr.Zero)
                                NativeMethods.UnloadUserProfile(primaryToken.DangerousGetHandle(), userProfileHandle);
                        }
                    }
                }
            }

            // onOutputLine, when supplied, is invoked with (line, userName) for each line of standard output
            // as the process writes it, letting callers stream progress instead of waiting for completion.
            public static List<ImpersonatedProcessResult> StartProcessAsAllUsers(
                string exePath,
                string? arguments,
                string workingDirectory,
                bool isWindowVisibleToUser = false,
                bool wait = true,
                Action<string, string?>? onOutputLine = null
                )
            {
                if (!HasSeTcbPrivilege())
                    return new List<ImpersonatedProcessResult> { StartProcessDirectly(exePath, arguments, workingDirectory, isWindowVisibleToUser, wait, false, onOutputLine) };

                List<UserExtensions.UserSessionInfo>? sessions = UserExtensions.GetUserSessions();
                if (sessions == null || sessions.Count == 0)
                    throw new InvalidOperationException("No user sessions found.");

                List<ImpersonatedProcessResult> results = new List<ImpersonatedProcessResult>();
                foreach (UserExtensions.UserSessionInfo session in sessions)
                {
                    // Only run for active sessions
                    if (string.IsNullOrEmpty(session.UserName) || session.State != NativeHelpers.WTS_CONNECTSTATE_CLASS.WTSActive)
                        continue;
                    results.Add(RunProcessInSession(session, exePath, arguments, workingDirectory, isWindowVisibleToUser, wait, null, false, onOutputLine));
                }
                return results;
            }

            // Reads a stream line-by-line, invoking onLine for each as it arrives, while still returning the
            // full text at the end (matching ReadToEndAsync's contract for callers that don't pass a callback).
            private static async Task<string> ReadStreamWithLineCallbackAsync(StreamReader reader, Action<string>? onLine)
            {
                if (onLine == null)
                    return await reader.ReadToEndAsync();

                string fullText = string.Empty;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    fullText += (fullText.Length == 0 ? string.Empty : "\n") + line;
                    onLine(line);
                }
                return fullText;
            }

            private static ImpersonatedProcessResult StartProcessDirectly(
                string exePath,
                string? arguments,
                string workingDirectory,
                bool isWindowVisibleToUser,
                bool wait,
                bool? killWithParent = false,
                Action<string, string?>? onOutputLine = null)
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = arguments;
                psi.WorkingDirectory = workingDirectory;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = !isWindowVisibleToUser;

                using (Process proc = new Process())
                {
                    proc.StartInfo = psi;
                    proc.Start();
                    if (killWithParent.HasValue && killWithParent.Value)
                    {
                        EnsureProcessDiesWithParent(proc.Handle);
                    }
                    string? stdOut = null;
                    string? stdErr = null;
                    int exitCode = 0;
                    if (wait)
                    {
                        // Read stdout and stderr asynchronously to avoid deadlock when both streams have data
                        Task<string> stdOutTask = ReadStreamWithLineCallbackAsync(
                            proc.StandardOutput,
                            onOutputLine == null ? null : (Action<string>)(line => onOutputLine(line, null)));
                        Task<string> stdErrTask = proc.StandardError.ReadToEndAsync();
                        proc.WaitForExit();
                        stdOut = stdOutTask.GetAwaiter().GetResult();
                        stdErr = stdErrTask.GetAwaiter().GetResult();
                        exitCode = proc.ExitCode;
                    }

                    string? userName = null;
                    string? userSid = null;
                    try
                    {
                        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                        {
                            userName = identity.Name;
                            userSid = identity.User?.ToString();
                        }
                    }
                    catch { }

                    ImpersonatedProcessResult result = new ImpersonatedProcessResult();
                    result.ExitCode = exitCode;
                    result.StandardOutput = stdOut;
                    result.StandardError = stdErr;
                    result.UserName = userName;
                    result.UserSid = userSid;
                    return result;
                }
            }

            public static ImpersonatedProcessResult? StartProcessAsCurrentUser(
                string exePath,
                string? arguments,
                string workingDirectory,
                bool isWindowVisibleToUser = false,
                bool wait = true,
                TimeSpan? waitTimeout = null,
                bool? killWithParent = false
                )
            {
                // Without SeTcbPrivilege we can't query other sessions' tokens, so just run directly as
                // whoever we're already running as.
                if (!HasSeTcbPrivilege())
                {
                    if (killWithParent.HasValue && killWithParent.Value)
                    {
                        return StartProcessDirectly(exePath, arguments, workingDirectory, isWindowVisibleToUser, wait, killWithParent);
                    }
                    else
                    {
                        return StartProcessDirectly(exePath, arguments, workingDirectory, isWindowVisibleToUser, wait);
                    }
                }

                List<UserExtensions.UserSessionInfo>? sessions = UserExtensions.GetUserSessions();
                if (sessions == null || sessions.Count == 0)
                    throw new InvalidOperationException("No user sessions found.");

                // Find the first active session with a user
                UserExtensions.UserSessionInfo? session = sessions.FirstOrDefault(s => !string.IsNullOrEmpty(s.UserName) && s.State == NativeHelpers.WTS_CONNECTSTATE_CLASS.WTSActive);
                if (session == null) return null;

                return RunProcessInSession(session, exePath, arguments, workingDirectory, isWindowVisibleToUser, wait, waitTimeout, killWithParent);
            }

            public static bool HasSeTcbPrivilege()
            {
                const uint TOKEN_QUERY = 0x0008;
                const uint SE_PRIVILEGE_ENABLED = 0x00000002;

                // A token can hold SeTcbPrivilege without it being enabled (privileges are disabled by
                // default until enabled). Try to enable it before checking, so this doesn't report false
                // just because nothing has switched it on yet.
                EnablePrivilege("SeTcbPrivilege");

                IntPtr processHandle = System.Diagnostics.Process.GetCurrentProcess().Handle;
                if (!NativeMethods.OpenProcessToken(processHandle, TOKEN_QUERY, out SafeNativeHandle token) || token.IsInvalid)
                    return false;
                using (token)
                {
                    if (!NativeMethods.LookupPrivilegeValue(null, "SeTcbPrivilege", out NativeMethods.LUID luid))
                        return false;
                    NativeMethods.PRIVILEGE_SET privSet = new NativeMethods.PRIVILEGE_SET
                    {
                        PrivilegeCount = 1,
                        Control = 1,
                        Privilege = new NativeMethods.LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                    };
                    NativeMethods.PrivilegeCheck(token.DangerousGetHandle(), ref privSet, out bool hasPrivilege);
                    return hasPrivilege;
                }
            }

            // LoadUserProfile requires SeRestorePrivilege/SeBackupPrivilege to be enabled on our token - a
            // SYSTEM service token normally holds both, but privileges are disabled by default until enabled.
            private static bool EnablePrivilege(string privilegeName)
            {
                const uint TOKEN_QUERY = 0x0008;
                const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
                const uint SE_PRIVILEGE_ENABLED = 0x00000002;
                IntPtr processHandle = System.Diagnostics.Process.GetCurrentProcess().Handle;
                if (!NativeMethods.OpenProcessToken(processHandle, TOKEN_QUERY | TOKEN_ADJUST_PRIVILEGES, out SafeNativeHandle token) || token.IsInvalid)
                    return false;
                using (token)
                {
                    if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out NativeMethods.LUID luid))
                        return false;
                    NativeHelpers.TOKEN_PRIVILEGES tp = new NativeHelpers.TOKEN_PRIVILEGES
                    {
                        PrivilegeCount = 1,
                        Luid = luid,
                        Attributes = SE_PRIVILEGE_ENABLED
                    };
                    return NativeMethods.AdjustTokenPrivileges(token.DangerousGetHandle(), false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)
                        && Marshal.GetLastWin32Error() == 0;
                }
            }

            // Loads the target user's registry hive (HKEY_CURRENT_USER) so processes started under their
            // token via CreateProcessAsUser see a properly mounted profile. Without this, apps that touch
            // HKCU or per-user profile state during startup (Windows Installer, PowerShell ISE, etc.) can
            // fail outright, even though simpler apps like Notepad launch fine regardless.
            private static IntPtr LoadUserProfileForToken(SafeNativeHandle primaryToken, string userName)
            {
                EnablePrivilege("SeRestorePrivilege");
                EnablePrivilege("SeBackupPrivilege");

                const int PI_NOUI = 0x00000001;
                NativeHelpers.PROFILEINFO profileInfo = new NativeHelpers.PROFILEINFO
                {
                    dwSize = Marshal.SizeOf<NativeHelpers.PROFILEINFO>(),
                    dwFlags = PI_NOUI,
                    lpUserName = userName
                };

                if (!NativeMethods.LoadUserProfile(primaryToken.DangerousGetHandle(), ref profileInfo))
                    return IntPtr.Zero;

                return profileInfo.hProfile;
            }

            public static void RunAsAllUsersImpersonated(Action action)
            {
                // No SeTcbPrivilege usually means an admin is running the suite directly to test it - just run
                // the action once as that admin rather than impersonating every session.
                if (!HasSeTcbPrivilege())
                {
                    action();
                    return;
                }

                List<UserExtensions.UserSessionInfo>? sessions = UserExtensions.GetUserSessions();
                if (sessions == null || sessions.Count == 0)
                    throw new InvalidOperationException("No user sessions found.");
                foreach (UserExtensions.UserSessionInfo session in sessions)
                {
                    // Only run for active sessions
                    if (string.IsNullOrEmpty(session.UserName) || session.State != NativeHelpers.WTS_CONNECTSTATE_CLASS.WTSActive)
                        continue;
                    RunAsImpersonatedUser(session.SessionID, action);
                }
            }

            public static List<T> RunAsAllUsersImpersonated<T>(Func<T> action)
            {
                // No SeTcbPrivilege usually means an admin is running the suite directly to test it - just run
                // the action once as that admin rather than impersonating every session.
                if (!HasSeTcbPrivilege())
                    return new List<T> { action() };

                List<UserTools.UserExtensions.UserSessionInfo>? sessions = UserTools.UserExtensions.GetUserSessions();
                if (sessions == null || sessions.Count == 0)
                    throw new InvalidOperationException("No user sessions found.");
                List<T> results = new List<T>();
                foreach (UserTools.UserExtensions.UserSessionInfo session in sessions)
                {
                    // Only run for active sessions
                    if (string.IsNullOrEmpty(session.UserName) || session.State != NativeHelpers.WTS_CONNECTSTATE_CLASS.WTSActive)
                        continue;
                    results.Add(RunAsImpersonatedUser(session.SessionID, action));
                }
                return results;
            }

            public static void RunAsImpersonatedUser(int sessionId, Action action)
            {
                // No SeTcbPrivilege usually means an admin is running the suite directly to test it - just run
                // the action directly as that admin rather than impersonating.
                if (!HasSeTcbPrivilege())
                {
                    action();
                    return;
                }
                SafeNativeHandle userToken;
                if (!NativeMethods.WTSQueryUserToken(sessionId, out userToken) || userToken.IsInvalid)
                    throw new InvalidOperationException($"Failed to get user token for session {sessionId}.");
                using (userToken)
                {
                    using (SafeNativeHandle? primaryToken = DuplicateToPrimaryToken(userToken))
                    {
                        if (primaryToken == null || primaryToken.IsInvalid)
                            throw new InvalidOperationException($"Failed to duplicate token for session {sessionId}.");
                        if (!NativeMethods.ImpersonateLoggedOnUser(primaryToken.DangerousGetHandle()))
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Impersonation failed for session {sessionId}.");
                        try
                        {
                            action();
                        }
                        finally
                        {
                            NativeMethods.RevertToSelf();
                        }
                    }
                }
            }

            public static T RunAsImpersonatedUser<T>(int sessionId, Func<T> action)
            {
                // No SeTcbPrivilege usually means an admin is running the suite directly to test it - just run
                // the action directly as that admin rather than impersonating.
                if (!HasSeTcbPrivilege())
                    return action();
                SafeNativeHandle userToken;
                if (!NativeMethods.WTSQueryUserToken(sessionId, out userToken) || userToken.IsInvalid)
                    throw new InvalidOperationException($"Failed to get user token for session {sessionId}.");
                using (userToken)
                {
                    using (SafeNativeHandle? primaryToken = DuplicateToPrimaryToken(userToken))
                    {
                        if (primaryToken == null || primaryToken.IsInvalid)
                            throw new InvalidOperationException($"Failed to duplicate token for session {sessionId}.");
                        if (!NativeMethods.ImpersonateLoggedOnUser(primaryToken.DangerousGetHandle()))
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Impersonation failed for session {sessionId}.");
                        try
                        {
                            return action();
                        }
                        finally
                        {
                            NativeMethods.RevertToSelf();
                        }
                    }
                }
            }
        }

        public class UserExtensions
        {
            public class UserProfile
            {
                public required string Sid { get; set; }
                public required string LocalPath { get; set; }

                public string GetSpecialFolder(Environment.SpecialFolder specialFolder)
                {
                    string fullSysPath = Environment.GetFolderPath(specialFolder);
                    string currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (!fullSysPath.StartsWith(currentUserProfile, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Special folder '{specialFolder}' ({fullSysPath}) is not located under the current profile ({currentUserProfile}); its path cannot be mapped to another user's profile.");

                    string partialPath = fullSysPath.Substring(currentUserProfile.Length).TrimStart('\\', '/');
                    return Path.Combine(LocalPath, partialPath);
                }
            }

            public class UserSessionInfo
            {
                public int SessionID { get; set; }
                public string? UserName { get; set; }
                public SecurityIdentifier? UserSid { get; set; }
                public NativeHelpers.WTS_CONNECTSTATE_CLASS State { get; set; }
            }

            public static List<UserSessionInfo>? GetUserSessions()
            {
                IntPtr serverHandle = IntPtr.Zero; // WTS_CURRENT_SERVER_HANDLE
                IntPtr sessionInfoPtr = IntPtr.Zero;
                int sessionCount = 0;
                List<UserSessionInfo> sessions = new List<UserSessionInfo>();
                try
                {
                    if (NativeMethods.WTSEnumerateSessions(serverHandle, 0, 1, out sessionInfoPtr, out sessionCount))
                    {
                        try
                        {
                            int dataSize = Marshal.SizeOf(typeof(NativeHelpers.WTS_SESSION_INFO));
                            for (int i = 0; i < sessionCount; i++)
                            {
                                IntPtr current = IntPtr.Add(sessionInfoPtr, i * dataSize);
                                NativeHelpers.WTS_SESSION_INFO si = Marshal.PtrToStructure<NativeHelpers.WTS_SESSION_INFO>(current);
                                string? userName = null;
                                string? domainName = null;
                                SecurityIdentifier? userSid = null;
                                IntPtr userPtr;
                                IntPtr domainPtr;
                                int bytesReturned;
                                // Get user name
                                if (NativeMethods.WTSQuerySessionInformation(
                                        serverHandle, si.SessionID, NativeHelpers.WTS_INFO_CLASS.WTSUserName, out userPtr, out bytesReturned
                                    ) &&
                                    userPtr != IntPtr.Zero)
                                {
                                    userName = Marshal.PtrToStringUni(userPtr);
                                    NativeMethods.WTSFreeMemory(userPtr);
                                }
                                // Get domain name
                                if (NativeMethods.WTSQuerySessionInformation(
                                        serverHandle, si.SessionID, NativeHelpers.WTS_INFO_CLASS.WTSDomainName, out domainPtr, out bytesReturned
                                    ) &&
                                    domainPtr != IntPtr.Zero)
                                {
                                    domainName = Marshal.PtrToStringUni(domainPtr);
                                    NativeMethods.WTSFreeMemory(domainPtr);
                                }
                                // Get SID
                                if (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(domainName))
                                {
                                    try
                                    {
                                        NTAccount ntAccount = new NTAccount(domainName, userName);
                                        userSid = (SecurityIdentifier?)ntAccount.Translate(typeof(SecurityIdentifier));
                                    }
                                    catch
                                    {
                                        // SID translation can fail for service/system sessions; continue enumeration
                                    }
                                }
                                sessions.Add(new UserSessionInfo
                                {
                                    SessionID = si.SessionID,
                                    UserName = userName,
                                    UserSid = userSid,
                                    State = si.State
                                });
                            }
                        }
                        finally
                        {
                            NativeMethods.WTSFreeMemory(sessionInfoPtr);
                        }
                    }
                    else
                    {
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to enumerate user sessions.", ex);
                }
                return sessions;
            }

            public List<UserProfile>? GetHumanUserAccountInfo()
            {
                List<string> sids = new();
                List<UserProfile> users = new();

                // Get accounts in the User Manager, as ProfileList also contains accounts some apps create, which arent actual logons
                using (RegistryKey? usersKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\UserManager\Users"))
                    {
                        if (usersKey == null)
                            return null;
                        foreach (string userId in usersKey.GetSubKeyNames())
                    {
                        using (var userKey = usersKey.OpenSubKey(userId))
                        {
                            if (userKey?.GetValue("Sid")?.ToString() is string sidStr)
                                sids.Add(sidStr.ToLowerInvariant());
                        }
                    }
                }

                // Get local path for the sids from ProfileList. Deliberately avoids System.Management/WMI:
                // ManagementObjectSearcher relies on reflection-based COM marshalling that Native AOT can't
                // statically analyze, and throws "No parameterless constructor defined for type
                // 'System.Management.WbemDefPath'" at runtime in an AOT-published exe (e.g. SuiteExecutor).
                using (RegistryKey? profileListKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
                {
                    if (profileListKey == null)
                        return null;
                    foreach (string sidKeyName in profileListKey.GetSubKeyNames())
                    {
                        if (!sids.Contains(sidKeyName.ToLowerInvariant()))
                            continue;
                        using RegistryKey? profileKey = profileListKey.OpenSubKey(sidKeyName);
                        if (profileKey?.GetValue("ProfileImagePath")?.ToString() is not string localPath || string.IsNullOrWhiteSpace(localPath))
                            continue;
                        users.Add(new UserProfile()
                        {
                            LocalPath = localPath,
                            Sid = sidKeyName,
                        });
                    }
                }

                if (users.Count > 0)
                    return users;
                else
                    return null;
            }
        }
    }
}
