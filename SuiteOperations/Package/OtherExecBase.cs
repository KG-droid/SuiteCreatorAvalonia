using Logger;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteTools;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using static SuiteTools.UserTools.ProcessExtensions;
using RestartBehaviorEnum = SuiteCreatorAvalonia.Enums.RestartBehavior;

namespace SuiteOperations.Package
{
    public class OtherExecBase : OtherBase
    {
        private Log _log;
        internal List<RuleSet> _ruleSets;
        public string? SourceDir { get; set; }

        public OtherExecBase(Log log, List<RuleSet> RuleSets)
        {
            _log = log;
            _ruleSets = RuleSets;
        }

        public OtherExecBase()
        {
            _ruleSets = new List<RuleSet>();
        }

        public virtual void SetLog(Log log)
        {
            _log = log;
        }

        public void SetRuleSets(List<RuleSet> ruleSets)
        {
            _ruleSets = ruleSets;
        }

        internal string ParseAndValidateCommand(List<VariableText>? commandList, string commandName)
        {
            if (commandList == null || commandList.Count == 0)
            {
                throw new Exception($"{commandName} must be specified before executing the package.");
            }

            StringBuilder fullTextBuilder = new();
            foreach (VariableText var in commandList)
            {
                fullTextBuilder.Append(var.GetValue());
            }

            string fullText = fullTextBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(fullText))
            {
                throw new Exception($"{commandName} must not be empty.");
            }
            if (fullText.StartsWith("'"))
            {
                throw new Exception($"{commandName} must not use a single quote character: '");
            }

            return fullText;
        }

        internal string GetValidatedExePath(string fullText)
        {
            string execPath = GetExePath(fullText);
            if (!Path.IsPathFullyQualified(execPath))
            {
                string? resolvedPath = ResolveFromSystemPath(execPath);
                execPath = resolvedPath ?? Path.Combine(SourceDir ?? throw new ArgumentNullException(nameof(SourceDir)), execPath);
            }

            if (!Path.Exists(execPath))
            {
                throw new Exception($"The executable path was not found: {execPath}");
            }

            return execPath;
        }

        private static string? ResolveFromSystemPath(string fileName)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv == null) return null;

            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                string fullPath = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            return null;
        }

        internal string GetCommandArguments(string fullText, string execPath)
        {
            // execPath here must be the raw exe token as it literally appears in fullText
            // (i.e. GetExePath(fullText)), not the resolved/validated path from
            // GetValidatedExePath, which can be a different length (e.g. combined with
            // SourceDir or resolved from PATH) and would throw off the offset below.
            int offset = fullText.StartsWith("\"")
                ? execPath.Length + 2  // account for the opening and closing quote chars
                : execPath.Length;
            return offset >= fullText.Length ? string.Empty : fullText.Substring(offset).TrimStart();
        }

        internal void HandleMSIRemoval(OtherMSIRemoval msi)
        {
            if (msi.MSIRemovalItems == null || msi.MSIRemovalItems.Count == 0)
            {
                throw new Exception("No MSI removal items specified.");
            }

            foreach (var item in msi.MSIRemovalItems)
            {
                _log.WriteLog($"Uninstalling MSI {(item.IsProductRemoval ? "Product" : "Family")}: {item.Code}", "OtherPkg", Log.Severity.Info);
                var result = item.IsProductRemoval
                    ? MSITools.UninstallMSI(item.Code, item.LogPath, null, item.AdditionalParams)
                    : MSITools.UninstallMSIFamily(item.Code, item.LogPath, null, item.AdditionalParams);

                _log.WriteLog($"Uninstall command: {result.CommandRun}", "OtherPkg", Log.Severity.Info);
                if (!result.Success)
                {
                    _log.WriteLog($"Uninstall error: {result.ErrorMessage}", "OtherPkg", Log.Severity.Error);
                    throw new Exception("MSI Uninstall failed.");
                }
            }

            _log.WriteLog("MSI Uninstall Complete", "OtherPkg", Log.Severity.Info);
        }

        internal void HandleRegexRemoval(OtherRegexRemoval regex)
        {
            var matches = MSITools.FindAllInstalledProductsByRegex(regex.Manufacturer, regex.ProductName);
            if (matches == null || matches.Count() == 0)
            {
                _log.WriteLog("No products found matching regex patterns.", "OtherPkg", Log.Severity.Info);
                return;
            }

            foreach (var prod in matches)
            {
                _log.WriteLog($"Uninstalling product: {prod.DisplayName} ({prod.ProductCode})", "OtherPkg", Log.Severity.Info);
                var result = MSITools.UninstallMSI(prod.ProductCode, null);
                _log.WriteLog($"Uninstall command: {result.CommandRun}", "OtherPkg", Log.Severity.Info);
                if (!result.Success)
                {
                    _log.WriteLog($"Uninstall error: {result.ErrorMessage}", "OtherPkg", Log.Severity.Error);
                    throw new Exception("Regex Uninstall failed.");
                }
            }

            _log.WriteLog("Regex Uninstall Complete", "OtherPkg", Log.Severity.Info);
        }

        internal void HandleCMDRemoval(OtherCMDRemoval cmd)
        {
            if (cmd.RemoveCommands == null || cmd.RemoveCommands.Count == 0)
            {
                throw new Exception("RemoveCommands must be specified before executing uninstall.");
            }

            foreach (var commandList in cmd.RemoveCommands)
            {
                string fullText = ParseAndValidateCommand(commandList, "CMD Removal command");
                string args = GetCommandArguments(fullText, GetExePath(fullText));
                string execPath = GetValidatedExePath(fullText);

                var result = ExecuteAndActionReturnType(execPath, SourceDir, args);
                if (result != ActionType.Continue)
                {
                    throw new Exception("CMD Uninstall failed.");
                }
            }

            _log.WriteLog("CMD Uninstall Complete", "OtherPkg", Log.Severity.Info);
        }

        private const int MaxUninstallDetectionWaitSeconds = 600;

        /// <summary>
        /// Some uninstallers (notably launcher-style exes and PowerShell scripts that start a
        /// child process without waiting) return before the real removal has finished. Poll the
        /// package's detection rule until it clears, backing off between checks, for up to 10
        /// minutes total before giving up.
        /// </summary>
        internal void WaitForUninstallDetectionToClear(Func<PackageExecDetectionResult> detectionCheck)
        {
            int elapsedSeconds = 0;
            int waitSeconds = 5;
            int attempt = 0;

            while (true)
            {
                PackageExecDetectionResult detection = detectionCheck();
                if (!detection.Result)
                {
                    if (attempt > 0)
                        _log.WriteLog($"Post-uninstall detection confirmed removal after {elapsedSeconds}s ({attempt} recheck{(attempt == 1 ? "" : "s")}).", "OtherPkg", Log.Severity.Info);
                    return;
                }

                if (elapsedSeconds >= MaxUninstallDetectionWaitSeconds)
                {
                    _log.WriteLog($"Post-uninstall detection still shows the package as present after waiting {MaxUninstallDetectionWaitSeconds / 60} minutes; giving up and continuing.", "OtherPkg", Log.Severity.Warning);
                    return;
                }

                attempt++;
                int thisWait = Math.Min(waitSeconds, MaxUninstallDetectionWaitSeconds - elapsedSeconds);
                _log.WriteLog($"Package still detected after uninstall command exited, waiting {thisWait}s before re-checking (attempt {attempt}).", "OtherPkg", Log.Severity.Info);
                Thread.Sleep(TimeSpan.FromSeconds(thisWait));
                elapsedSeconds += thisWait;
                waitSeconds = Math.Min(waitSeconds * 2, 60);
            }
        }

        internal ActionType ExecuteAndActionReturnType(string exec, string workingDir, string args)
        {
            if (!LegacyLongFilePath)
                return RunProcess(exec, workingDir, args);

            char substDrive = FindFreeDriveLetter() ?? throw new Exception("No free drive letter available to create a virtual drive for legacy long file path support.");
            string execDir = Path.GetDirectoryName(exec) ?? throw new Exception("Cannot determine the directory of the executable.");
            SubstDrive(substDrive, execDir);
            try
            {
                return RunProcess($"{substDrive}:\\{Path.GetFileName(exec)}", $"{substDrive}:\\", args);
            }
            finally
            {
                UnsubstDrive(substDrive);
            }
        }

        private ActionType RunProcess(string exec, string? workingDir, string args)
        {
            string loggedArgs = SecureParams ? "***" : args;
            _log.WriteLog($"Command to run: {exec} {loggedArgs}", "OtherPkg", Log.Severity.Info);

            using Process process = new()
            {
                StartInfo = new()
                {
                    FileName = exec,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            if (!string.IsNullOrWhiteSpace(workingDir))
            {
                process.StartInfo.WorkingDirectory = workingDir;
            }

            process.Start();
            process.WaitForExit();


            ActionType? customAction = ExitCodes?.FirstOrDefault(c => c.Code == process.ExitCode)?.Action;
            if (customAction != null)
            {
                _log.WriteLog($"The command returned a custom exit code of: {process.ExitCode}", "OtherPkg", Log.Severity.Info);
                return ApplyRestartBehavior(customAction.Value);
            }
            else 
            {
                _log.WriteLog($"The command returned a exit code of: {process.ExitCode}", "OtherPkg", Log.Severity.Info);
                return ApplyRestartBehavior(GetDefaultActionType(process.ExitCode));
            }
        }

        private static char? FindFreeDriveLetter()
        {
            HashSet<char> usedDrives = new(DriveInfo.GetDrives().Select(d => d.Name[0]));
            for (char c = 'Z'; c >= 'D'; c--)
            {
                if (!usedDrives.Contains(c))
                    return c;
            }
            return null;
        }

        private static void SubstDrive(char driveLetter, string path)
        {
            using Process p = Process.Start(new ProcessStartInfo("subst", $"{driveLetter}: \"{path}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit();
        }

        private static void UnsubstDrive(char driveLetter)
        {
            using Process p = Process.Start(new ProcessStartInfo("subst", $"{driveLetter}: /D") { UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit();
        }

        internal List<ActionType> ExecuteAndActionReturnTypePerUser(string exec, string args, bool isWindowVisibleToUser = true)
        {
            string loggedArgs = SecureParams ? "***" : args;
            _log.WriteLog($"Per-user command to run: {exec} {loggedArgs}", "OtherPkg", Log.Severity.Info);
            List<ActionType> returnActions = new();
            List<ImpersonatedProcessResult>? execResults = null;
            if (!LegacyLongFilePath)
            {
                execResults = StartProcessAsAllUsers(exec, args, Path.GetDirectoryName(exec), isWindowVisibleToUser, true);
            }
            else
            {
                // subst drives are per-session, so the virtual drive must be created in the current (SYSTEM) session
                // before impersonating users, and the remapped path passed through so each user process uses it.
                char substDrive = FindFreeDriveLetter() ?? throw new Exception("No free drive letter available to create a virtual drive for legacy long file path support.");
                string execDir = Path.GetDirectoryName(exec) ?? throw new Exception("Cannot determine the directory of the executable.");
                SubstDrive(substDrive, execDir);
                string remappedExec = $"{substDrive}:\\{Path.GetFileName(exec)}";

                try
                {
                    execResults = StartProcessAsAllUsers(remappedExec, args, $"{substDrive}:\\", isWindowVisibleToUser, true);
                }
                finally
                {
                    UnsubstDrive(substDrive);
                }
            }
            foreach (var result in execResults)
            {
                ActionType? customAction = ExitCodes?.FirstOrDefault(c => c.Code == result.ExitCode)?.Action;
                if (customAction != null)
                {
                    _log.WriteLog($"The command returned a custom exit code of: {result.ExitCode}, for user: {result.UserName}", "OtherPkg", Log.Severity.Info);
                    returnActions.Add(ApplyRestartBehavior(customAction.Value));
                }
                else 
                {
                    _log.WriteLog($"The command returned an exit code of: {result.ExitCode}, for user: {result.UserName}", "OtherPkg", Log.Severity.Info);
                    returnActions.Add(ApplyRestartBehavior(GetDefaultActionType(result.ExitCode)));
                }
            }
            return returnActions;
        }

        private static ActionType GetDefaultActionType(int exitCode)
        {
            switch (exitCode)
            {
                case 0:
                    return ActionType.Continue;
                case 3010:
                    return ActionType.RestartDelayed;
                case 1641:
                    return ActionType.RestartImmediate;
                default:
                    return ActionType.Abort;
            }
        }

        private ActionType ApplyRestartBehavior(ActionType baseAction)
        {
            if (baseAction == ActionType.Abort || baseAction == ActionType.Continue)
            {
                return baseAction;
            }
            return RestartBehavior switch
            {
                RestartBehaviorEnum.AlwaysImmediate => ActionType.RestartImmediate,
                RestartBehaviorEnum.AlwaysDelayed => ActionType.RestartDelayed,
                RestartBehaviorEnum.BasedOnReturnCodeImmediate when baseAction == ActionType.RestartImmediate => ActionType.RestartImmediate,
                RestartBehaviorEnum.BasedOnReturnCodeDelayed when baseAction == ActionType.RestartImmediate => ActionType.RestartDelayed,
                RestartBehaviorEnum.Ignore => ActionType.Continue,
                _ => baseAction
            };
        }

        internal string GetExePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                throw new Exception("Cannot parse a null exe variable path");
            }

            if (fullPath.StartsWith("\""))
            {
                int endQuote = fullPath.IndexOf('"', 1);
                if (endQuote > 0)
                {
                    return fullPath.Substring(1, endQuote - 1);
                }
            }
            else
            {
                int firstSpace = fullPath.IndexOf(' ');
                if (firstSpace > 0)
                {
                    return fullPath.Substring(0, firstSpace);
                }
                else
                {
                    return fullPath;
                }
            }

            throw new Exception($"Unable to obtain exe path from the parsed variable string of: {fullPath}");
        }

        public new void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourceDir))
            {
                throw new ArgumentNullException(nameof(SourceDir), "SourceDir must be specified before executing the package.");
            }

            string? baseValidation = base.Validate();
            if (baseValidation != null)
            {
                throw new Exception(baseValidation);
            }
        }
    }
}
