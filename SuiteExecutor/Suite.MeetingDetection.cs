using static SuiteTools.UserTools.ProcessExtensions;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        // Exit codes reported by SuiteUserPopup.exe --CheckMeetingStatus: 0 not in use, 1 in use, 2 check
        // failed/inconclusive.
        private enum MeetingCheckExitType
        {
            NotInUse = 0,
            InUse = 1,
            CheckFailed = 2,
        }

        // Delegates the actual detection to SuiteUserPopup.exe running as the interactive user (via a live
        // WASAPI audio-session check, see MicrophoneActivityDetector in that project), rather than reading
        // any local state SuiteExecutor itself has access to as SYSTEM - registry values can be edited by
        // the user, but a live "is there an open capture stream right now" check can't be spoofed the same
        // way. No window is shown for this check; it's a headless probe with a short timeout.
        private bool IsMicrophoneInUse()
        {
            try
            {
                TimeSpan checkTimeout = TimeSpan.FromSeconds(15);
                ImpersonatedProcessResult? result = StartProcessAsCurrentUser(_userPopExe, "--CheckMeetingStatus", _installedPopupDir, false, true, checkTimeout);
                if (result == null)
                {
                    _log.WriteLog("No active user session found to check microphone usage, assuming not in use", "ExecPopup", Log.Severity.Info);
                    return false;
                }

                switch ((MeetingCheckExitType?)result.ExitCode)
                {
                    case MeetingCheckExitType.InUse:
                        return true;
                    case MeetingCheckExitType.NotInUse:
                        return false;
                    default:
                        // ErrorMessage only covers launch failures (see StartProcessAsCurrentUser); the check
                        // process's own exception, if any, comes back via StandardError instead.
                        string failureDetail = !string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : result.ErrorMessage;
                        _log.WriteLog($"Microphone check returned an unexpected result (exit code: {result.ExitCode}, detail: {failureDetail}), assuming not in use", "ExecPopup", Log.Severity.Warning);
                        return false;
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to check microphone usage: {ex.Message}, assuming not in use", "ExecPopup", Log.Severity.Warning);
                return false;
            }
        }
    }
}
