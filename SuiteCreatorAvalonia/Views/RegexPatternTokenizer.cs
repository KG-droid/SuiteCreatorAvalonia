using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Views
{
    public record RegexToken(int Start, int Length, string Category, string Description);

    // Hand-rolled lexical scan of a .NET regex pattern for syntax highlighting/tooltips in
    // RegexTesterWindow. Not a full parser - it recognizes the common constructs (escapes,
    // character classes, quantifiers, anchors, groups, alternation) well enough to color and
    // describe them; anything it doesn't recognize is left as an uncolored literal.
    public static class RegexPatternTokenizer
    {
        public static List<RegexToken> Tokenize(string pattern)
        {
            List<RegexToken> tokens = new();
            if (string.IsNullOrEmpty(pattern))
                return tokens;

            int i = 0;
            int length = pattern.Length;
            while (i < length)
            {
                char c = pattern[i];

                if (c == '\\' && i + 1 < length)
                {
                    char next = pattern[i + 1];
                    tokens.Add(new RegexToken(i, 2, "CharClass", DescribeEscape(next)));
                    i += 2;
                    continue;
                }

                if (c == '[')
                {
                    int end = FindCharClassEnd(pattern, i);
                    bool negated = i + 1 < length && pattern[i + 1] == '^';
                    tokens.Add(new RegexToken(i, end - i, "CharClass",
                        negated ? "Character class - matches any character NOT listed" : "Character class - matches any one of the listed characters"));
                    i = end;
                    continue;
                }

                if (c == '(')
                {
                    int delimLength = GetGroupOpenLength(pattern, i);
                    tokens.Add(new RegexToken(i, delimLength, "Group", DescribeGroupOpen(pattern, i, delimLength)));
                    i += delimLength;
                    continue;
                }

                if (c == ')')
                {
                    tokens.Add(new RegexToken(i, 1, "Group", "End of group"));
                    i += 1;
                    continue;
                }

                if (c == '^' || c == '$')
                {
                    tokens.Add(new RegexToken(i, 1, "Anchor", c == '^' ? "Start of line/string" : "End of line/string"));
                    i += 1;
                    continue;
                }

                if (c == '.')
                {
                    tokens.Add(new RegexToken(i, 1, "Wildcard", "Any character (except newline, unless singleline mode is on)"));
                    i += 1;
                    continue;
                }

                if (c == '|')
                {
                    tokens.Add(new RegexToken(i, 1, "Alternation", "Alternation (OR) - matches the pattern on either side"));
                    i += 1;
                    continue;
                }

                if (c == '*' || c == '+' || c == '?')
                {
                    int quantLength = 1;
                    string baseDesc = c switch
                    {
                        '*' => "Zero or more (greedy)",
                        '+' => "One or more (greedy)",
                        _ => "Zero or one - optional (greedy)"
                    };
                    if (i + 1 < length && pattern[i + 1] == '?')
                    {
                        quantLength = 2;
                        baseDesc = baseDesc.Replace("greedy", "lazy - as few as possible");
                    }
                    tokens.Add(new RegexToken(i, quantLength, "Quantifier", baseDesc));
                    i += quantLength;
                    continue;
                }

                if (c == '{')
                {
                    int end = FindQuantifierBraceEnd(pattern, i);
                    if (end > i)
                    {
                        int closeLength = end - i;
                        bool lazy = end < length && pattern[end] == '?';
                        int totalLength = closeLength + (lazy ? 1 : 0);
                        string inner = pattern.Substring(i + 1, closeLength - 2);
                        string desc = DescribeBraceQuantifier(inner) + (lazy ? " (lazy - as few as possible)" : " (greedy)");
                        tokens.Add(new RegexToken(i, totalLength, "Quantifier", desc));
                        i += totalLength;
                        continue;
                    }
                }

                // Unrecognized/literal character - no token, just advance.
                i += 1;
            }

            return tokens;
        }

        private static int FindCharClassEnd(string pattern, int start)
        {
            int i = start + 1;
            if (i < pattern.Length && pattern[i] == '^') i++;
            if (i < pattern.Length && pattern[i] == ']') i++; // a leading ']' is a literal, not the closer
            while (i < pattern.Length && pattern[i] != ']')
            {
                if (pattern[i] == '\\' && i + 1 < pattern.Length) i++;
                i++;
            }
            return i < pattern.Length ? i + 1 : pattern.Length;
        }

        private static int GetGroupOpenLength(string pattern, int start)
        {
            if (start + 2 < pattern.Length && pattern[start + 1] == '?')
            {
                char third = pattern[start + 2];
                if (third == ':' || third == '=' || third == '!' || third == '>' || third == '#')
                    return 3;
                if (third == '<' && start + 3 < pattern.Length && (pattern[start + 3] == '=' || pattern[start + 3] == '!'))
                    return 4;
                if (third == '<' || third == '\'')
                {
                    char closer = third == '<' ? '>' : '\'';
                    int end = pattern.IndexOf(closer, start + 3);
                    if (end > 0) return end - start + 1;
                }
            }
            return 1;
        }

        private static string DescribeGroupOpen(string pattern, int start, int delimLength)
        {
            if (delimLength == 1) return "Start of capturing group";
            string delim = pattern.Substring(start, delimLength);
            if (delim == "(?:") return "Start of non-capturing group";
            if (delim == "(?=") return "Start of positive lookahead - must be followed by this, without consuming it";
            if (delim == "(?!") return "Start of negative lookahead - must NOT be followed by this";
            if (delim == "(?<=") return "Start of positive lookbehind - must be preceded by this, without consuming it";
            if (delim == "(?<!") return "Start of negative lookbehind - must NOT be preceded by this";
            if (delim.StartsWith("(?<") || delim.StartsWith("(?'"))
            {
                string name = delim.Substring(3, delim.Length - 4);
                return $"Start of named capturing group '{name}'";
            }
            return "Start of group";
        }

        private static int FindQuantifierBraceEnd(string pattern, int start)
        {
            int i = start + 1;
            int digitsBeforeComma = 0;
            bool sawComma = false;
            int digitsAfterComma = 0;
            while (i < pattern.Length && pattern[i] != '}')
            {
                if (char.IsDigit(pattern[i]))
                {
                    if (sawComma) digitsAfterComma++;
                    else digitsBeforeComma++;
                }
                else if (pattern[i] == ',' && !sawComma)
                {
                    sawComma = true;
                }
                else
                {
                    return -1; // not a valid {n,m} body
                }
                i++;
            }
            if (i >= pattern.Length || digitsBeforeComma == 0)
                return -1;
            return i + 1;
        }

        private static string DescribeBraceQuantifier(string inner)
        {
            string[] parts = inner.Split(',');
            if (parts.Length == 1)
                return $"Exactly {parts[0]} times";
            if (string.IsNullOrEmpty(parts[1]))
                return $"{parts[0]} or more times";
            return $"Between {parts[0]} and {parts[1]} times";
        }

        private static string DescribeEscape(char c) => c switch
        {
            'd' => "Digit (0-9)",
            'D' => "Not a digit",
            'w' => "Word character (letter, digit, or underscore)",
            'W' => "Not a word character",
            's' => "Whitespace character",
            'S' => "Not a whitespace character",
            'b' => "Word boundary",
            'B' => "Not a word boundary",
            'A' => "Start of string (ignores multiline mode)",
            'Z' => "End of string, or before a trailing newline",
            'z' => "End of string",
            'G' => "Where the previous match left off",
            'n' => "Newline",
            'r' => "Carriage return",
            't' => "Tab",
            'f' => "Form feed",
            'v' => "Vertical tab",
            '0' => "Null character",
            >= '1' and <= '9' => $"Backreference to group {c}",
            _ => $"Literal '{c}' (escaped so it isn't treated as special)"
        };
    }
}
