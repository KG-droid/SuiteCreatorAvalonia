using System.Management.Automation.Language;

namespace SystemTools
{
    internal class PS
    {
        public static bool IsPowerShellSyntaxValid(string scriptText, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(scriptText))
                return true;

            var ast = Parser.ParseInput(scriptText, out var tokens, out var parseErrors);
            if (parseErrors != null && parseErrors.Length > 0)
            {
                error = string.Join(Environment.NewLine, parseErrors.Select(e => e.Message));
                return false;
            }
            return true;
        }
    }
}
