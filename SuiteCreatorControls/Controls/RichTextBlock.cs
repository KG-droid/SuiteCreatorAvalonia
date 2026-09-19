using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using SuiteCreatorControls.Text;
using System;
using System.Collections.Generic;

namespace SuiteCreatorControls.Controls
{
    /// <summary>
    /// A TextBlock that renders <see cref="RichTextMarkup"/> (the popup message format) with bold, italic, size and
    /// colour. Text not covered by a tag inherits the TextBlock's own font size and foreground.
    /// <para>
    /// The formatting is applied through the text layout's style overrides rather than <c>Inlines</c>. An
    /// inline-based TextBlock can end up with a cached empty layout after a theme change (zero width, one blank
    /// line), so the message vanished when the user switched between light and dark while the popup was open.
    /// </para>
    /// </summary>
    public class RichTextBlock : TextBlock
    {
        public static readonly StyledProperty<string?> MarkupProperty =
            AvaloniaProperty.Register<RichTextBlock, string?>(nameof(Markup));

        private List<RichTextRun> _runs = new List<RichTextRun>();

        // Keep the stock TextBlock theme/styles (foreground etc.) applying to this subclass.
        protected override Type StyleKeyOverride => typeof(TextBlock);

        public string? Markup
        {
            get => GetValue(MarkupProperty);
            set => SetValue(MarkupProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == MarkupProperty)
                ApplyMarkup();
        }

        private void ApplyMarkup()
        {
            _runs = RichTextMarkup.Parse(Markup);

            string plain = string.Concat(_runs.ConvertAll(r => r.Text));
            SetCurrentValue(TextProperty, plain);

            // The plain text may be unchanged while the formatting is not.
            InvalidateTextLayout();
        }

        protected override TextLayout CreateTextLayout(string? text)
        {
            if (_runs.Count == 0 || string.IsNullOrEmpty(text))
                return base.CreateTextLayout(text);

            Typeface typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            Size maxSize = GetMaxSizeFromConstraint();

            return new TextLayout(
                text,
                typeface,
                FontSize,
                Foreground,
                IsMeasureValid ? TextAlignment : TextAlignment.Left,
                TextWrapping,
                TextTrimming,
                TextDecorations,
                FlowDirection,
                maxSize.Width,
                maxSize.Height,
                LineHeight,
                LetterSpacing,
                MaxLines,
                FontFeatures,
                BuildStyleOverrides(text));
        }

        private List<ValueSpan<TextRunProperties>> BuildStyleOverrides(string text)
        {
            List<ValueSpan<TextRunProperties>> overrides = new List<ValueSpan<TextRunProperties>>();
            int start = 0;

            foreach (RichTextRun run in _runs)
            {
                int length = Math.Min(run.Text.Length, text.Length - start);
                if (length <= 0)
                    break;

                if (run.Style != default)
                {
                    RichTextStyle style = run.Style;
                    Typeface typeface = new Typeface(
                        FontFamily,
                        style.Italic ? FontStyle.Italic : FontStyle,
                        style.Bold ? FontWeight.Bold : FontWeight,
                        FontStretch);
                    IBrush? foreground = style.Foreground is Color color
                        ? new SolidColorBrush(ReadableColors.Clamp(color))
                        : Foreground;

                    overrides.Add(new ValueSpan<TextRunProperties>(
                        start,
                        length,
                        new GenericTextRunProperties(typeface, style.FontSize ?? FontSize, TextDecorations, foreground, fontFeatures: FontFeatures)));
                }

                start += length;
            }

            return overrides;
        }
    }
}
