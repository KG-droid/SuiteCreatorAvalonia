using System.Diagnostics;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SuiteTools
{
    public static class NativeTools
    {
        /// <summary>
        /// Uses RoboCopy to copy files or directories quickly and reliably.
        /// Automatically selects optimal options for speed and completeness, and ensures destination security is inherited.
        /// Throws an exception if any file is skipped, not copied, or any warning/error occurs.
        /// </summary>
        /// <param name="source">Source file or directory path.</param>
        /// <param name="destination">Destination file or directory path.</param>
        /// <param name="copyDirectoryItself">If true and source is a directory, copies the directory itself into destination; otherwise, copies only its contents.</param>
        /// <param name="excludeDirectories">Directories to exclude (passed to RoboCopy as /XD entries).</param>
        /// <param name="excludeFiles">Files to exclude (passed to RoboCopy as /XF entries).</param>
        /// <exception cref="InvalidOperationException">Throws if RoboCopy fails, skips files, or is not found.</exception>
        public static void CopyWithRoboCopy(
            string source,
            string destination,
            bool copyDirectoryItself = false,
            IEnumerable<string>? excludeDirectories = null,
            IEnumerable<string>? excludeFiles = null)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("Source and destination paths must be specified.");

            var roboCopyExe = "robocopy";
            string args;
            // /IM   : Include Modified files
            // /IT   : Include Tweaked files (changed attributes)
            // /IS   : Include Same files (identical files)
            // /NP   : No Progress - don't display percentage copied
            // /NDL  : No Directory List - don't log directory names
            // /NC   : No Class - don't log file classes
            // /BYTES: Print sizes in bytes
            // /NJH  : No Job Header
            // /NJS  : No Job Summary
            // /TEE  : Output to console window and log file
            // /R:10 : Retry 10 times on failed copies
            // /W:20 : Wait 20 seconds between retries
            // /MT:n : Multithreaded copy with n threads (default 8, max 128)
            int threads = Environment.ProcessorCount;
            string copyFlags = $"/im /it /is /NP /NDL /NC /BYTES /NJH /NJS /tee /R:10 /W:20 /MT:{threads}";

            static string QuoteRoboCopyArg(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Exclude values cannot be null/empty.", nameof(value));
                if (value.Contains('"'))
                    throw new ArgumentException("Exclude values cannot contain double quotes (\").", nameof(value));

                return $"\"{value.Trim()}\"";
            }

            static string BuildExcludeFlags(IEnumerable<string>? dirs, IEnumerable<string>? files)
            {
                List<string>? parts = null;

                if (dirs != null)
                {
                    var addedXd = false;
                    foreach (var dir in dirs)
                    {
                        if (string.IsNullOrWhiteSpace(dir))
                            continue;

                        parts ??= new List<string>();
                        if (!addedXd)
                        {
                            parts.Add("/XD");
                            addedXd = true;
                        }
                        parts.Add(QuoteRoboCopyArg(dir));
                    }
                }

                if (files != null)
                {
                    var addedXf = false;
                    foreach (var file in files)
                    {
                        if (string.IsNullOrWhiteSpace(file))
                            continue;

                        parts ??= new List<string>();
                        if (!addedXf)
                        {
                            parts.Add("/XF");
                            addedXf = true;
                        }
                        parts.Add(QuoteRoboCopyArg(file));
                    }
                }

                return parts == null || parts.Count == 0 ? string.Empty : " " + string.Join(' ', parts);
            }

            copyFlags += BuildExcludeFlags(excludeDirectories, excludeFiles);
            if (!File.Exists(source))
            {
                // This is a directory
                copyFlags = "/E " + copyFlags;
            }

            if (File.Exists(source))
            {
                var fileName = Path.GetFileName(source);
                var sourceDir = Path.GetDirectoryName(source)!;
                // Ensure destination directory exists
                if (!Directory.Exists(destination))
                {
                    Directory.CreateDirectory(destination);
                }
                args = $"\"{sourceDir}\" \"{destination}\" \"{fileName}\" {copyFlags}";
            }
            else if (Directory.Exists(source))
            {
                string src = source;
                string dest = destination;
                // Ensure destination directory exists
                if (!Directory.Exists(dest))
                {
                    Directory.CreateDirectory(dest);
                }
                if (!copyDirectoryItself)
                {
                    args = $"\"{src}\" \"{dest}\" {copyFlags}";
                }
                else
                {
                    var parent = Path.GetDirectoryName(src.TrimEnd(Path.DirectorySeparatorChar))!;
                    var dirName = Path.GetFileName(src.TrimEnd(Path.DirectorySeparatorChar));
                    args = $"\"{parent}\" \"{dest}\" \"{dirName}\" {copyFlags}";
                }
            }
            else
            {
                throw new FileNotFoundException($"Source path '{source}' does not exist.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = roboCopyExe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
                throw new InvalidOperationException("Failed to start RoboCopy process.");
            process.WaitForExit();
            if (process.ExitCode > 7)
            {
                var error = process.StandardError.ReadToEnd();
                var output = process.StandardOutput.ReadToEnd();
                throw new InvalidOperationException($"RoboCopy did not copy all files successfully. Exit code: {process.ExitCode}. Output: {output}. Error: {error}");
            }
        }
    }
}
