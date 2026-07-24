using System.Text.Json.Nodes;
using static SuiteTools.UserTools.ProcessExtensions;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private static readonly string _progressPopupExe = Path.Combine(_installedPopupDir, "SuiteProgressPopup.exe");
        private string? _progressFilePath;
        private bool _progressPopupStarted = false;
        private Task? _progressPopupTask;

        private void StartProgressPopup()
        {
            if (!_suiteConfig.PopupSettings.ShowProgress)
                return;

            try
            {
                string popupDir = Path.Combine(_suiteRootDir, "Popup");
                string suiteLogoPath = Path.Combine(popupDir, "SuiteLogo.png");
                string progressFilePath = Path.Combine(popupDir, "progress.json");

                if (!Directory.Exists(popupDir))
                {
                    _log.WriteLog($"Progress popup is enabled but Popup directory does not exist: {popupDir}. Skipping progress popup.", "Progress", Log.Severity.Warning);
                    return;
                }
                if (!File.Exists(suiteLogoPath))
                {
                    _log.WriteLog($"Progress popup is enabled but suite logo was not found: {suiteLogoPath}. Skipping progress popup.", "Progress", Log.Severity.Warning);
                    return;
                }
                if (!File.Exists(_progressPopupExe))
                {
                    _log.WriteLog($"Progress popup is enabled but SuiteProgressPopup.exe was not found: {_progressPopupExe}. Skipping progress popup.", "Progress", Log.Severity.Warning);
                    return;
                }

                _progressFilePath = progressFilePath;
                WriteProgressStatus(0, $"Preparing {_suiteConfig.BuildSettings.Name}...", isComplete: false, isError: false);

                string progressArguments = $"--SuiteLogo \"{suiteLogoPath}\" --ProgressFile \"{progressFilePath}\" --LogFile \"{_logPath}\" --ProgressColour \"{_suiteConfig.PopupSettings.BackgroundColor.Value}\"";
                _log.WriteLog($"Launching progress popup: \"{_progressPopupExe}\" {progressArguments}", "Progress", Log.Severity.Info);

                _progressPopupTask = Task.Run(() =>
                {
                    try
                    {
                        StartProcessAsCurrentUser(_progressPopupExe, progressArguments, _installedPopupDir, true, true, TimeSpan.FromSeconds(30), true);
                    }
                    catch (Exception ex)
                    {
                        _log.WriteLog($"Progress popup process ended with error: {ex.Message}", "Progress", Log.Severity.Warning);
                    }
                });

                _progressPopupStarted = true;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to start progress popup: {ex.Message}", "Progress", Log.Severity.Error);
            }
        }

        private void UpdateProgress(int percentage, string? statusText)
        {
            if (!_progressPopupStarted)
                return;

            WriteProgressStatus(percentage, statusText, isComplete: false, isError: false);
        }

        private void CompleteProgressPopup(bool isError)
        {
            if (!_progressPopupStarted)
                return;

            string statusText = isError ? "Installation failed" : "Installation complete";
            WriteProgressStatus(100, statusText, isComplete: !isError, isError: isError);
        }

        // CompleteProgressPopup only signals the popup to close by writing to the progress file — the popup
        // notices asynchronously (its own poll interval, a completion linger, then shutdown) and exits on its
        // own time, or is force-terminated once its waitTimeout elapses (see StartProgressPopup). Wait for
        // that to actually happen before anything (e.g. CleanupDeferral) touches paths the popup still holds
        // open, rather than racing a fixed delay.
        private void WaitForProgressPopupExit(TimeSpan timeout)
        {
            if (_progressPopupTask == null)
                return;

            try
            {
                if (!_progressPopupTask.Wait(timeout))
                {
                    _log.WriteLog($"Progress popup did not exit within {timeout}; proceeding anyway", "Progress", Log.Severity.Warning);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Error waiting for progress popup to exit: {ex.Message}", "Progress", Log.Severity.Warning);
            }
        }

        private void WriteProgressStatus(int percentage, string? statusText, bool isComplete, bool isError)
        {
            if (string.IsNullOrWhiteSpace(_progressFilePath))
                return;

            try
            {
                JsonObject json = new JsonObject
                {
                    ["Percentage"] = Math.Clamp(percentage, 0, 100),
                    ["StatusText"] = statusText,
                    ["ProductName"] = _suiteConfig.BuildSettings.Name,
                    ["IsComplete"] = isComplete,
                    ["IsError"] = isError
                };

                File.WriteAllText(_progressFilePath, json.ToJsonString());
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to write progress status: {ex.Message}", "Progress", Log.Severity.Warning);
            }
        }
    }
}
