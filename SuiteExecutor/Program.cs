using SuiteOperations.Events;
using System.Diagnostics;

namespace SuiteExecutor
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                // Handle failsafe switches before any Suite/config loading
                if (args.Length >= 3 && string.Equals(args[0], "--failsafe-unblock-all", StringComparison.OrdinalIgnoreCase))
                {
                    return HandleFailsafeUnblockAll(args[1], args[2]);
                }
                if (args.Length >= 5 && string.Equals(args[0], "--watch-pid", StringComparison.OrdinalIgnoreCase))
                {
                    return HandleWatchPid(args[1], args[2], args[3], args[4]);
                }

#if DEBUG
                if (args.Contains("--debug"))
                {
                    while (!System.Diagnostics.Debugger.IsAttached)
                    {
                        Thread.Sleep(100);
                    }
                }
#endif

                string? actionArg = null;
                string? configPath = null;
                string? failsafeTaskName = null;
                SuiteRunMode runMode = SuiteRunMode.Normal;
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

#if DEBUG
                    if (string.Equals(arg, "--debug", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
#endif
                    if (string.Equals(arg, "--Config", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                        {
                            Console.Error.WriteLine("Error: --Config requires a path value.\n");
                            return 87;
                        }

                        configPath = Path.GetFullPath(args[i + 1]);
                        i++;
                        continue;
                    }

                    // Set by the deferral reminder scheduled task so the run proceeds instead of re-deferring.
                    if (string.Equals(arg, "--reminder", StringComparison.OrdinalIgnoreCase))
                    {
                        runMode = SuiteRunMode.Reminder;
                        continue;
                    }

                    // Set by the FailSafe recovery scheduled task: run the suite unattended (no deferral, no popup).
                    if (string.Equals(arg, "--failsafe", StringComparison.OrdinalIgnoreCase))
                    {
                        runMode = SuiteRunMode.FailSafe;
                        continue;
                    }

                    // The FailSafe task passes its own name back so this run can delete it directly if it
                    // turns out the cached config it needs is gone (see the config-not-found check below).
                    if (string.Equals(arg, "--failsafe-task", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                        {
                            Console.Error.WriteLine("Error: --failsafe-task requires a value.\n");
                            return 87;
                        }

                        failsafeTaskName = args[i + 1];
                        i++;
                        continue;
                    }

                    if (actionArg == null)
                    {
                        actionArg = arg;
                        continue;
                    }

                    Console.Error.WriteLine($"Error: Unrecognized argument '{arg}'.\n");
                    return 87;
                }

                if (actionArg == null)
                {
                    Console.Error.WriteLine("Error: You must specify a suite action (deploy/remove/rollback)\n");
                    return 87;
                }

                SuiteAction action;
                if (!TryParseAction(actionArg, out action))
                {
                    Console.Error.WriteLine($"Error: Invalid action argument '{actionArg}'. Valid actions: deploy, remove, rollback.\n");
                    return 87;
                }

                if (configPath == null)
                {
                    string selfDIR = Path.GetDirectoryName(Environment.ProcessPath)!;
                    configPath = Path.Combine(selfDIR, "SuiteConfig.scfg");
                }

                if (!File.Exists(configPath))
                {
                    Console.Error.WriteLine($"Config file not found: {configPath}");

                    // A FailSafe recovery run that can't find its cached config can never succeed — without
                    // this, the task stays registered and keeps retrying (and failing) on every subsequent
                    // boot/logon forever, since Suite.Execute's own RemoveFailSafeTask cleanup is never
                    // reached (a Suite is never constructed on this path).
                    if (runMode == SuiteRunMode.FailSafe && !string.IsNullOrWhiteSpace(failsafeTaskName))
                    {
                        Console.Error.WriteLine($"Cached installer for this FailSafe recovery is missing — removing orphaned scheduled task '{failsafeTaskName}'.");
                        TryDeleteOrphanedFailSafeTask(failsafeTaskName);
                    }

                    return 1603;
                }

                Suite suite = new Suite(configPath);
                suite.Execute(action, runMode);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Suite exec loadup failed: {ex.Message}");
                return 1603;
            }
        }

        // Best-effort deletion of a FailSafe/restart-retry scheduled task that can never succeed (its cached
        // config is gone). Used only from the config-not-found path above, before a Suite/Log instance exists.
        private static void TryDeleteOrphanedFailSafeTask(string taskName)
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

                process.StandardError.ReadToEnd();
                process.WaitForExit();
            }
            catch
            {
                // Best-effort only — nothing else can be done for an orphaned task from here.
            }
        }

        private static bool TryParseAction(string arg, out SuiteAction action)
        {
            action = SuiteAction.Deployment;

            if (string.Equals(arg, "deploy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "deployment", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "install", StringComparison.OrdinalIgnoreCase))
            {
                action = SuiteAction.Deployment;
                return true;
            }

            if (string.Equals(arg, "remove", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "removal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "uninstall", StringComparison.OrdinalIgnoreCase))
            {
                action = SuiteAction.Removal;
                return true;
            }

            if (string.Equals(arg, "rollback", StringComparison.OrdinalIgnoreCase))
            {
                action = SuiteAction.Rollback;
                return true;
            }

            return false;
        }

        /// <summary>
        /// ONSTART failsafe: restores every entry in the shared block-list file (one per suite run) — removes
        /// the IFEO Debugger block for every process it blocked, and restores every service it stopped/disabled
        /// to its captured prior start type/running state — then deletes the list and this scheduled task.
        /// Usage: SuiteExecutor.exe --failsafe-unblock-all {blockListPath} {taskName}
        /// </summary>
        private static int HandleFailsafeUnblockAll(string blockListPath, string taskName)
        {
            try { ClosureFailsafe.RestoreAllEntries(blockListPath); }
            catch { /* Best-effort */ }

            ClosureFailsafe.DeleteFailsafeTask(taskName, blockListPath);

            return 0;
        }

        /// <summary>
        /// Background watcher: polls until the suite process exits (or PID is reused), then unblocks
        /// every process in the shared block list for that suite run.
        /// Usage: SuiteExecutor.exe --watch-pid {pid} {processName} {blockListPath} {taskName}
        /// </summary>
        private static int HandleWatchPid(string pidStr, string suiteProcessName, string blockListPath, string taskName)
        {
            if (!int.TryParse(pidStr, out int suitePid))
                return 1;

            // Poll every 3 seconds until the suite process is gone or reused by a different process
            while (true)
            {
                try
                {
                    using Process suiteProcess = Process.GetProcessById(suitePid);
                    if (!string.Equals(suiteProcess.ProcessName, suiteProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        break; // PID reused by a different process — suite is gone
                    }
                }
                catch (ArgumentException)
                {
                    break; // Process not found — suite exited
                }
                catch
                {
                    break; // Any other error — assume suite is gone
                }

                Thread.Sleep(3_000);
            }

            // Suite is gone — perform cleanup
            return HandleFailsafeUnblockAll(blockListPath, taskName);
        }
    }
}
