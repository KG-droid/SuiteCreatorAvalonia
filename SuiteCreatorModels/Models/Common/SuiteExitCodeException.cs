namespace SuiteCreatorAvalonia.Models.Common
{
    // Thrown when a package operation determines the whole suite must exit with a specific,
    // meaningful process exit code (e.g. 1618 - another installation already in progress) rather
    // than the generic failure code Suite.cs otherwise uses.
    public class SuiteExitCodeException : Exception
    {
        public int ExitCode { get; }

        public SuiteExitCodeException(int exitCode, string message) : base(message)
        {
            ExitCode = exitCode;
        }
    }
}
