using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Views
{
    public record RegexSnippet(string Token, string Name, string Description);

    // Reference catalog for RegexTesterWindow's builder panel - click an entry to insert it into
    // the pattern at the caret. Descriptions intentionally mirror RegexPatternTokenizer's so the
    // "what does this mean" wording stays consistent between typing and browsing.
    public static class RegexSnippetLibrary
    {
        public static readonly IReadOnlyList<RegexSnippet> All = new List<RegexSnippet>
        {
            new(@"\d", "Digit", "Matches any digit (0-9)"),
            new(@"\D", "Not digit", "Matches any character that is not a digit"),
            new(@"\w", "Word character", "Matches any letter, digit, or underscore"),
            new(@"\W", "Not word character", "Matches any character that is not a letter, digit, or underscore"),
            new(@"\s", "Whitespace", "Matches any whitespace character (space, tab, newline)"),
            new(@"\S", "Not whitespace", "Matches any character that is not whitespace"),
            new(@"\b", "Word boundary", "Matches the position between a word character and a non-word character"),
            new(@"\B", "Not word boundary", "Matches any position that is not a word boundary"),
            new(".", "Any character", "Matches any character except a newline"),
            new("^", "Start of line", "Matches the start of the string or line"),
            new("$", "End of line", "Matches the end of the string or line"),
            new("*", "Zero or more", "Matches the preceding element zero or more times (greedy)"),
            new("+", "One or more", "Matches the preceding element one or more times (greedy)"),
            new("?", "Zero or one", "Matches the preceding element zero or one time - optional (greedy)"),
            new("*?", "Zero or more (lazy)", "Matches the preceding element zero or more times, as few as possible"),
            new("+?", "One or more (lazy)", "Matches the preceding element one or more times, as few as possible"),
            new("{n}", "Exact count", "Matches the preceding element exactly n times"),
            new("{n,}", "At least n", "Matches the preceding element n or more times"),
            new("{n,m}", "Between n and m", "Matches the preceding element between n and m times"),
            new("[abc]", "Character set", "Matches any one of the listed characters"),
            new("[^abc]", "Negated set", "Matches any character NOT listed"),
            new("[a-z]", "Character range", "Matches any character in the given range"),
            new("(...)", "Capturing group", "Groups a pattern and captures the matched text"),
            new("(?:...)", "Non-capturing group", "Groups a pattern without capturing the matched text"),
            new("(?<name>...)", "Named group", "Groups a pattern and captures it under a name"),
            new("(?=...)", "Positive lookahead", "Matches only if followed by this pattern, without consuming it"),
            new("(?!...)", "Negative lookahead", "Matches only if NOT followed by this pattern"),
            new("(?<=...)", "Positive lookbehind", "Matches only if preceded by this pattern, without consuming it"),
            new("(?<!...)", "Negative lookbehind", "Matches only if NOT preceded by this pattern"),
            new("|", "Alternation", "Matches either the pattern before or after this symbol"),
            new(@"\1", "Backreference", "Matches the same text as previously matched by group 1"),
        };
    }
}
