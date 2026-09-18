using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using SuiteCreatorControls.Text;
using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Controls;

/// <summary>
/// TextPresenter that lays the TextBox text out with per-character bold/italic/size/colour, so the caret,
/// selection and hit-testing all follow the styled layout rather than a uniform one.
/// </summary>
public class StyledTextPresenter : TextPresenter
{
    private Size _constraint;
    private RichTextStyle[]? _styles;

    /// <summary>One style per character of <see cref="TextPresenter.Text"/>; null (or a length mismatch) renders unstyled.</summary>
    public void SetStyles(RichTextStyle[]? styles)
    {
        _styles = styles;
        InvalidateTextLayout();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _constraint = availableSize;
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // TextPresenter keeps its wrap constraint in a private field; mirror its arrange logic so the layout
        // built in CreateTextLayout wraps at the same width the base class expects.
        if (Math.Abs(_constraint.Width - finalSize.Width) > 1e-6)
            _constraint = new Size(Math.Ceiling(finalSize.Width), double.PositiveInfinity);
        return base.ArrangeOverride(finalSize);
    }

    protected override TextLayout CreateTextLayout()
    {
        string? text = Text;
        RichTextStyle[]? styles = _styles;
        if (styles is null || string.IsNullOrEmpty(text) || styles.Length != text.Length
            || !string.IsNullOrEmpty(PreeditText) || PasswordChar != default)
        {
            return base.CreateTextLayout();
        }

        double maxWidth = _constraint.Width <= 0 ? double.PositiveInfinity : _constraint.Width;
        double maxHeight = _constraint.Height <= 0 ? double.PositiveInfinity : _constraint.Height;
        Typeface typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);

        return new TextLayout(
            text,
            typeface,
            FontSize,
            Foreground,
            TextAlignment,
            TextWrapping,
            null,
            null,
            FlowDirection,
            maxWidth,
            maxHeight,
            LineHeight,
            LetterSpacing,
            0,
            FontFeatures,
            BuildOverrides(text, styles));
    }

    private List<ValueSpan<TextRunProperties>> BuildOverrides(string text, RichTextStyle[] styles)
    {
        int selectionStart = Math.Min(SelectionStart, SelectionEnd);
        int selectionEnd = Math.Max(SelectionStart, SelectionEnd);
        bool highlight = ShowSelectionHighlight && SelectionForegroundBrush is not null && selectionEnd > selectionStart;
        bool IsSelected(int index) => highlight && index >= selectionStart && index < selectionEnd;

        List<ValueSpan<TextRunProperties>> overrides = new List<ValueSpan<TextRunProperties>>();
        int runStart = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && styles[i] == styles[runStart] && IsSelected(i) == IsSelected(runStart))
                continue;

            bool selected = IsSelected(runStart);
            if (styles[runStart] != default || selected)
                overrides.Add(new ValueSpan<TextRunProperties>(runStart, i - runStart, CreateRunProperties(styles[runStart], selected)));
            runStart = i;
        }
        return overrides;
    }

    private GenericTextRunProperties CreateRunProperties(RichTextStyle style, bool selected)
    {
        Typeface typeface = new Typeface(
            FontFamily,
            style.Italic ? FontStyle.Italic : FontStyle,
            style.Bold ? FontWeight.Bold : FontWeight,
            FontStretch);
        IBrush? foreground = selected
            ? SelectionForegroundBrush
            : style.Foreground is Color color ? new SolidColorBrush(color) : Foreground;
        return new GenericTextRunProperties(typeface, style.FontSize ?? FontSize, foregroundBrush: foreground, fontFeatures: FontFeatures);
    }
}
