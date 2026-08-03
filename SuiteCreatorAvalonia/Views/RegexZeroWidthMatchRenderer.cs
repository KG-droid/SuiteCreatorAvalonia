using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Views
{
    // A zero-width match (e.g. a lookahead like (?=llo)) has no characters for
    // RegexMatchColorizer to paint a background over, so instead this draws a thin vertical
    // marker at the match's index, the way regex101 does for zero-width matches.
    public class RegexZeroWidthMatchRenderer : IBackgroundRenderer
    {
        private static readonly IPen MarkerPen = new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x5D, 0x5D)), 2);
        private readonly Func<IReadOnlyList<int>> _getOffsets;

        public RegexZeroWidthMatchRenderer(Func<IReadOnlyList<int>> getOffsets)
        {
            _getOffsets = getOffsets;
        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (textView?.Document == null)
                return;

            textView.EnsureVisualLines();

            foreach (int offset in _getOffsets())
            {
                if (offset < 0 || offset > textView.Document.TextLength)
                    continue;

                TextViewPosition position = new(textView.Document.GetLocation(offset));
                Point top, bottom;
                try
                {
                    top = textView.GetVisualPosition(position, VisualYPosition.LineTop);
                    bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                top -= textView.ScrollOffset;
                bottom -= textView.ScrollOffset;
                drawingContext.DrawLine(MarkerPen, top, bottom);
            }
        }
    }
}
