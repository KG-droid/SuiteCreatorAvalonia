using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SuiteCreatorControls.Text
{
    /// <summary>
    /// Formatting for a run of popup message text. A null <see cref="FontSize"/> or <see cref="Foreground"/>
    /// means "inherit from the host control", so unformatted text keeps following the user's theme.
    /// </summary>
    public readonly record struct RichTextStyle(bool Bold, bool Italic, double? FontSize, Color? Foreground);

    public sealed record RichTextRun(string Text, RichTextStyle Style);

    /// <summary>
    /// Inline markup for popup message text: [b]..[/b], [i]..[/i], [size=14]..[/size], [color=#RRGGBB]..[/color],
    /// with a literal '[' written as '[['. Text with no tags parses as a single unstyled run, so messages saved
    /// before formatting existed render exactly as they did. Deliberately not XML so the AOT-published
    /// SuiteUserPopup can render it with no reflection-based parser.
    /// </summary>
    public static class RichTextMarkup
    {
        public static List<RichTextRun> Parse(string? markup)
        {
            List<RichTextRun> runs = new List<RichTextRun>();
            if (string.IsNullOrEmpty(markup))
                return runs;

            List<(string Tag, RichTextStyle Previous)> open = new List<(string, RichTextStyle)>();
            RichTextStyle current = default;
            StringBuilder text = new StringBuilder();
            int i = 0;

            void Flush()
            {
                if (text.Length == 0)
                    return;
                runs.Add(new RichTextRun(text.ToString(), current));
                text.Clear();
            }

            while (i < markup.Length)
            {
                char c = markup[i];
                if (c != '[')
                {
                    text.Append(c);
                    i++;
                    continue;
                }

                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    text.Append('[');
                    i += 2;
                    continue;
                }

                int close = markup.IndexOf(']', i + 1);
                if (close < 0)
                {
                    text.Append(c);
                    i++;
                    continue;
                }

                string body = markup.Substring(i + 1, close - i - 1);
                if (body.Length > 1 && body[0] == '/')
                {
                    string tag = body.Substring(1).Trim().ToLowerInvariant();
                    int index = open.FindLastIndex(o => o.Tag == tag);
                    if (index < 0)
                    {
                        text.Append(c);
                        i++;
                        continue;
                    }

                    Flush();
                    current = open[index].Previous;
                    open.RemoveRange(index, open.Count - index);
                    i = close + 1;
                    continue;
                }

                if (!TryApplyTag(body, current, out string name, out RichTextStyle next))
                {
                    text.Append(c);
                    i++;
                    continue;
                }

                Flush();
                open.Add((name, current));
                current = next;
                i = close + 1;
            }

            Flush();
            return runs;
        }

        private static bool TryApplyTag(string body, RichTextStyle current, out string tag, out RichTextStyle next)
        {
            int eq = body.IndexOf('=');
            tag = (eq < 0 ? body : body.Substring(0, eq)).Trim().ToLowerInvariant();
            string? value = eq < 0 ? null : body.Substring(eq + 1).Trim();
            next = current;

            switch (tag)
            {
                case "b" when value is null:
                    next = current with { Bold = true };
                    return true;
                case "i" when value is null:
                    next = current with { Italic = true };
                    return true;
                case "size" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double size) && size > 0:
                    next = current with { FontSize = size };
                    return true;
                case "color" when value is not null && Color.TryParse(value, out Color color):
                    next = current with { Foreground = color };
                    return true;
                default:
                    return false;
            }
        }

        public static string Serialize(IEnumerable<RichTextRun> runs)
        {
            StringBuilder sb = new StringBuilder();
            List<string> open = new List<string>();

            foreach (RichTextRun run in runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                    continue;

                List<string> required = TagsFor(run.Style);

                int keep = 0;
                while (keep < open.Count && required.Contains(open[keep]))
                    keep++;
                for (int k = open.Count - 1; k >= keep; k--)
                {
                    sb.Append("[/").Append(TagName(open[k])).Append(']');
                    open.RemoveAt(k);
                }

                foreach (string tag in required)
                {
                    if (open.Contains(tag))
                        continue;
                    sb.Append('[').Append(tag).Append(']');
                    open.Add(tag);
                }

                sb.Append(run.Text.Replace("[", "[["));
            }

            for (int k = open.Count - 1; k >= 0; k--)
                sb.Append("[/").Append(TagName(open[k])).Append(']');

            return sb.ToString();
        }

        private static List<string> TagsFor(RichTextStyle style)
        {
            List<string> tags = new List<string>(4);
            if (style.Foreground is Color color)
                tags.Add("color=" + color.ToString());
            if (style.FontSize is double size)
                tags.Add("size=" + size.ToString("0.##", CultureInfo.InvariantCulture));
            if (style.Bold)
                tags.Add("b");
            if (style.Italic)
                tags.Add("i");
            return tags;
        }

        private static string TagName(string tag)
        {
            int eq = tag.IndexOf('=');
            return eq < 0 ? tag : tag.Substring(0, eq);
        }

        public static string ToPlainText(string? markup)
        {
            StringBuilder sb = new StringBuilder();
            foreach (RichTextRun run in Parse(markup))
                sb.Append(run.Text);
            return sb.ToString();
        }

        public static IEnumerable<Inline> ToInlines(string? markup)
        {
            foreach (RichTextRun run in Parse(markup))
            {
                Run inline = new Run(run.Text);
                if (run.Style.Bold)
                    inline.FontWeight = FontWeight.Bold;
                if (run.Style.Italic)
                    inline.FontStyle = FontStyle.Italic;
                if (run.Style.FontSize is double size)
                    inline.FontSize = size;
                if (run.Style.Foreground is Color color)
                    inline.Foreground = new SolidColorBrush(color);
                yield return inline;
            }
        }
    }
}
