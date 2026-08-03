using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Views
{
    // Colors each recognized regex construct in RegexTesterWindow's pattern editor by category,
    // the same way regex101's pattern bar does, so the tooltip-on-hover has something to key off.
    public class RegexPatternColorizer : DocumentColorizingTransformer
    {
        private static readonly IBrush AnchorBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0x86, 0xF2));
        private static readonly IBrush CharClassBrush = new SolidColorBrush(Color.FromRgb(0x4F, 0xC1, 0xE9));
        private static readonly IBrush QuantifierBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xA1, 0x4F));
        private static readonly IBrush GroupBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xF5));
        private static readonly IBrush AlternationBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5D, 0x5D));
        private static readonly IBrush WildcardBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xC5, 0x4F));

        private readonly Func<IReadOnlyList<RegexToken>> _getTokens;

        public RegexPatternColorizer(Func<IReadOnlyList<RegexToken>> getTokens)
        {
            _getTokens = getTokens;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            int lineStart = line.Offset;
            int lineEnd = line.EndOffset;

            foreach (RegexToken token in _getTokens())
            {
                int tokenEnd = token.Start + token.Length;
                if (tokenEnd <= lineStart || token.Start >= lineEnd)
                    continue;

                int segmentStart = Math.Max(token.Start, lineStart);
                int segmentEnd = Math.Min(tokenEnd, lineEnd);
                IBrush brush = GetBrush(token.Category);
                ChangeLinePart(segmentStart, segmentEnd, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(brush);
                });
            }
        }

        private static IBrush GetBrush(string category) => category switch
        {
            "Anchor" => AnchorBrush,
            "CharClass" => CharClassBrush,
            "Quantifier" => QuantifierBrush,
            "Group" => GroupBrush,
            "Alternation" => AlternationBrush,
            "Wildcard" => WildcardBrush,
            _ => Brushes.Gray
        };
    }
}
