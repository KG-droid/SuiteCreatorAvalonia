using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        // Must match the SuiteSfxStub extraction root (SuiteSfxStub.Program._cacheRoot). The SFX extracts the
        // suite here and retains it (on KeepCache or when a run defers), and the deferral reminder re-runs from it.
        private const string _suiteCacheRoot = @"C:\Windows\SuiteInstallerCache";
        private const string _deferralTaskPrefix = "SuiteReminder_";

        private bool IsDeferralActive()
        {
            string upgradeCode = _suiteConfig.BuildSettings.UpgradeCode.ToString();
            string taskName = _deferralTaskPrefix + upgradeCode;
            string cachedSfxDir = Path.Combine(_suiteCacheRoot, upgradeCode);

            // The scheduled reminder task is the sole indicator of a pending deferral. The cache folder is the
            // shared installer cache (it exists during every run), so its presence must NOT be treated as a
            // deferral marker — otherwise the initial run would delete its own active cache as an "orphan".
            bool taskExists = DoesScheduledTaskExist(taskName);
            if (!taskExists)
            {
                return false;
            }

            bool cacheExists = Directory.Exists(cachedSfxDir);
            _log.WriteLog($"Deferral check — reminder task exists: {taskExists}, cached installer exists: {cacheExists}", "Deferral", Log.Severity.Info);

            if (cacheExists)
            {
                return true;
            }

            // The reminder task exists but the cached media it needs is gone, so it can never run — clean up the orphan task.
            _log.WriteLog("Scheduled reminder task exists but cached installer is missing — cleaning up orphaned task", "Deferral", Log.Severity.Warning);
            DeleteScheduledTask(taskName);
            return false;
        }

        private void ScheduleReminder(TimeOnly reminderTime)
        {
            string upgradeCode = _suiteConfig.BuildSettings.UpgradeCode.ToString();
            string taskName = _deferralTaskPrefix + upgradeCode;
            string? thisProcPath = Environment.ProcessPath;

            try
            {
                if (string.IsNullOrWhiteSpace(thisProcPath)) throw new FileNotFoundException("Unable to determine this executable path");

                // Calculate the next occurrence of the reminder time
                DateTime now = DateTime.Now;
                DateTime reminderDateTime = new DateTime(now.Year, now.Month, now.Day, reminderTime.Hour, reminderTime.Minute, 0);
                if (reminderDateTime <= now)
                {
                    // If the chosen time has already passed today, schedule for tomorrow
                    reminderDateTime = reminderDateTime.AddDays(1);
                }

                // Create the scheduled task
                CreateReminderScheduledTask(taskName, thisProcPath, _action, reminderDateTime);
                _log.WriteLog($"Deferral scheduled — task '{taskName}' will run at {reminderDateTime}", "Deferral", Log.Severity.Info);
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to schedule deferral reminder: {ex.Message}", "Deferral", Log.Severity.Error);
                try { DeleteScheduledTask(taskName); } catch { }
            }
        }

        private void CleanupDeferral()
        {
            string upgradeCode = _suiteConfig.BuildSettings.UpgradeCode.ToString();
            string taskName = _deferralTaskPrefix + upgradeCode;
            string cachedSfxDir = Path.Combine(_suiteCacheRoot, upgradeCode);

            try
            {
                if (DoesScheduledTaskExist(taskName))
                {
                    DeleteScheduledTask(taskName);
                    _log.WriteLog($"Cleaned up deferral scheduled task '{taskName}'", "Deferral", Log.Severity.Info);
                }

                // This runs only after a successful suite completion (i.e. we are not deferring). Retain the
                // cache only when the build opts into KeepCache; otherwise remove it now.
                if (_suiteConfig.BuildSettings.KeepCache)
                {
                    _log.WriteLog($"KeepCache is enabled — retaining cached installer directory '{cachedSfxDir}'", "Deferral", Log.Severity.Info);
                }
                else if (Directory.Exists(cachedSfxDir))
                {
                    // The suite set its working directory into this cache dir at startup; move off it first,
                    // otherwise the directory is locked as our CWD and the delete fails — notably on the
                    // reminder run, where there is no SFX process to clean it up afterwards.
                    Directory.SetCurrentDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                    DeleteCacheDirWithRetry(cachedSfxDir);
                    _log.WriteLog($"Cleaned up cached installer directory '{cachedSfxDir}'", "Deferral", Log.Severity.Info);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Error during deferral cleanup: {ex.Message}", "Deferral", Log.Severity.Warning);
            }
        }

        // Something inside the cache dir (e.g. AV/indexer briefly scanning a freshly-extracted exe) can still be
        // releasing a handle right after use - retry a couple of times before giving up, rather than failing
        // cleanup on the first transient lock. This is only for genuinely transient locks now that the known
        // persistent-lock causes (progress popup CWD, failsafe watcher CWD - both inside this cache dir) are
        // fixed at the source, so the budget here is intentionally short.
        //
        // Directory.Delete(path, recursive:true) only reports one aggregate exception for the whole tree, so a
        // sharing violation deep inside gives no clue which file or subfolder is actually still open. Delete the
        // tree item-by-item instead so a persistent lock is logged with the exact path holding it, not just the
        // cache root.
        private void DeleteCacheDirWithRetry(string cachedSfxDir)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                string? lockedPath = TryDeleteTree(cachedSfxDir);
                if (lockedPath == null)
                    return;

                if (attempt < maxAttempts)
                {
                    _log.WriteLog($"Cached installer directory '{cachedSfxDir}' is still in use — locked item: '{lockedPath}' (attempt {attempt}/{maxAttempts}), retrying shortly", "Deferral", Log.Severity.Warning);
                    Thread.Sleep(2000);
                }
                else
                {
                    throw new IOException($"Failed to delete cached installer directory '{cachedSfxDir}' — locked item: '{lockedPath}'");
                }
            }
        }

        // Deletes files and subdirectories bottom-up one at a time. Returns the path of the first item that
        // could not be removed (so the caller can log/retry against a concrete culprit), or null on full success.
        private string? TryDeleteTree(string dir)
        {
            try
            {
                foreach (string subDir in Directory.GetDirectories(dir))
                {
                    string? lockedPath = TryDeleteTree(subDir);
                    if (lockedPath != null)
                        return lockedPath;
                }

                foreach (string file in Directory.GetFiles(dir))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _log.WriteLog($"Locked file blocking cache cleanup: '{file}' — {ex.Message}", "Deferral", Log.Severity.Warning);
                        return file;
                    }
                }

                Directory.Delete(dir);
                return null;
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Locked directory blocking cache cleanup: '{dir}' — {ex.Message}", "Deferral", Log.Severity.Warning);
                return dir;
            }
        }

        private void CreateReminderScheduledTask(string taskName, string sfxPath, SuiteAction action, DateTime triggerTime)
        {
            string actionArg = action switch
            {
                SuiteAction.Deployment => "deploy",
                SuiteAction.Removal => "remove",
                SuiteAction.Rollback => "rollback",
                _ => "deploy"
            };

            string expiryDate = triggerTime.AddDays(7).ToString("yyyy-MM-ddTHH:mm:ss");
            string triggerDate = triggerTime.ToString("yyyy-MM-ddTHH:mm:ss");
            string descriptionName = System.Security.SecurityElement.Escape(_suiteConfig.BuildSettings.Name ?? "Suite");
            string commandPath = System.Security.SecurityElement.Escape(sfxPath);
            string configArg = string.IsNullOrWhiteSpace(_suiteConfigPath)
                ? string.Empty
                : $@" --Config ""{System.Security.SecurityElement.Escape(_suiteConfigPath)}""";

            string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Suite deferral reminder for {descriptionName}</Description>
  </RegistrationInfo>
  <Triggers>
    <TimeTrigger>
      <StartBoundary>{triggerDate}</StartBoundary>
      <EndBoundary>{expiryDate}</EndBoundary>
      <Enabled>true</Enabled>
    </TimeTrigger>
    <BootTrigger>
      <Delay>PT5M</Delay>
      <EndBoundary>{expiryDate}</EndBoundary>
      <Enabled>true</Enabled>
    </BootTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <DeleteExpiredTaskAfter>PT0S</DeleteExpiredTaskAfter>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT2H</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>""{commandPath}""</Command>
      <Arguments>{actionArg}{configArg} --reminder</Arguments>
    </Exec>
  </Actions>
</Task>";

            string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{taskName}.xml");
            try
            {
                File.WriteAllText(tempXmlPath, taskXml);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = SystemPaths.SchTasks,
                    Arguments = $"/Create /TN \"{taskName}\" /XML \"{tempXmlPath}\" /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null)
                    throw new InvalidOperationException("Failed to start schtasks.exe");

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"schtasks /Create failed (exit {process.ExitCode}): {error}");
                }

                _log.WriteLog($"Created scheduled task '{taskName}' — trigger: {triggerDate}, expiry: {expiryDate}", "Deferral", Log.Severity.Info);
            }
            finally
            {
                try { File.Delete(tempXmlPath); } catch { }
            }
        }

        private bool DoesScheduledTaskExist(string taskName)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = SystemPaths.SchTasks,
                    Arguments = $"/Query /TN \"{taskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        private void DeleteScheduledTask(string taskName)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = SystemPaths.SchTasks,
                    Arguments = $"/Delete /TN \"{taskName}\" /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using Process? process = Process.Start(psi);
                if (process == null) return;

                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    _log.WriteLog($"Failed to delete scheduled task '{taskName}': {error}", "Deferral", Log.Severity.Warning);
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Error deleting scheduled task '{taskName}': {ex.Message}", "Deferral", Log.Severity.Warning);
            }
        }
    }
}
