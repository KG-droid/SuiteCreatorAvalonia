using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Views
{
    // Highlights RegexTesterWindow's live match ranges directly in the sample text editor, the
    // same way regex101.com does, instead of a separate offset list that isn't meaningful to users.
    public class RegexMatchColorizer : DocumentColorizingTransformer
    {
        private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(110, 76, 175, 80));
        private readonly Func<IReadOnlyList<(int Start, int Length)>> _getMatchRanges;

        public RegexMatchColorizer(Func<IReadOnlyList<(int Start, int Length)>> getMatchRanges)
        {
            _getMatchRanges = getMatchRanges;
        }

        protected override void ColorizeLine(DocumentLine line)
        {
            int lineStart = line.Offset;
            int lineEnd = line.EndOffset;

            foreach ((int start, int length) in _getMatchRanges())
            {
                int matchEnd = start + length;
                if (matchEnd <= lineStart || start >= lineEnd)
                    continue;

                int segmentStart = Math.Max(start, lineStart);
                int segmentEnd = Math.Min(matchEnd, lineEnd);
                ChangeLinePart(segmentStart, segmentEnd, element =>
                {
                    element.TextRunProperties.SetBackgroundBrush(HighlightBrush);
                });
            }
        }
    }
}
