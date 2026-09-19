using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SuiteCreatorControls.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuiteCreatorAvalonia.Controls;

/// <summary>
/// WYSIWYG editor for popup message text (bold, italic, font size, colour). Formatting is held per character
/// alongside the TextBox text and round-tripped through <see cref="RichTextMarkup"/> via <see cref="Markup"/>.
/// </summary>
public partial class RichTextEditor : UserControl
{
    public static readonly StyledProperty<string?> MarkupProperty =
        AvaloniaProperty.Register<RichTextEditor, string?>(nameof(Markup), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Size unformatted text renders at (the popup's own font size); text with no size tag shows this in the toolbar.</summary>
    public static readonly StyledProperty<double> DefaultFontSizeProperty =
        AvaloniaProperty.Register<RichTextEditor, double>(nameof(DefaultFontSize), 13);

    private RichTextStyle[] _styles = Array.Empty<RichTextStyle>();
    // Formatting toggled with nothing selected applies to whatever is typed next at that caret position.
    private RichTextStyle? _pendingStyle;
    private int _pendingCaret = -1;
    private bool _loadingMarkup;
    private bool _updatingToolbar;
    private string? _lastPushedMarkup;

    public RichTextEditor()
    {
        InitializeComponent();

        Text_RichTextBox.FontSize = DefaultFontSize;
        Text_RichTextBox.TextEdited += OnTextEdited;
        Text_RichTextBox.TemplateApplied += (_, _) => PushStylesToPresenter();
        Text_RichTextBox.PropertyChanged += OnTextBoxPropertyChanged;
        Text_RichTextBox.GotFocus += (_, _) => UpdateToolbar();
        Text_RichTextBox.LostFocus += (_, _) => UpdateToolbar();
        Text_RichTextBox.AddHandler(KeyDownEvent, OnTextBoxKeyDown, RoutingStrategies.Tunnel);

        Bold_ToggleButton.IsCheckedChanged += (_, _) =>
        {
            if (_updatingToolbar)
                return;
            bool bold = Bold_ToggleButton.IsChecked == true;
            ApplyStyle(s => s with { Bold = bold });
        };
        Italic_ToggleButton.IsCheckedChanged += (_, _) =>
        {
            if (_updatingToolbar)
                return;
            bool italic = Italic_ToggleButton.IsChecked == true;
            ApplyStyle(s => s with { Italic = italic });
        };
        FontSize_NumericUpDown.ValueChanged += OnFontSizeChanged;
        Color_ColorPicker.ColorChanged += OnColorChanged;
        AutoColor_Button.Click += (_, _) => ApplyStyle(s => s with { Foreground = null });
        ClearFormat_Button.Click += (_, _) => ApplyStyle(_ => default);

        UpdateToolbar();
    }

    public string? Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    public double DefaultFontSize
    {
        get => GetValue(DefaultFontSizeProperty);
        set => SetValue(DefaultFontSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Text_RichTextBox is null)
            return;

        if (change.Property == MarkupProperty)
        {
            string? markup = change.GetNewValue<string?>();
            if (!string.Equals(markup ?? "", _lastPushedMarkup ?? "", StringComparison.Ordinal))
                LoadMarkup(markup);
        }
        else if (change.Property == DefaultFontSizeProperty)
        {
            Text_RichTextBox.FontSize = change.GetNewValue<double>();
            UpdateToolbar();
        }
    }

    private void LoadMarkup(string? markup)
    {
        StringBuilder text = new StringBuilder();
        List<RichTextStyle> styles = new List<RichTextStyle>();
        foreach (RichTextRun run in RichTextMarkup.Parse(markup))
        {
            text.Append(run.Text);
            for (int i = 0; i < run.Text.Length; i++)
                styles.Add(run.Style);
        }

        _lastPushedMarkup = markup;
        _loadingMarkup = true;
        try
        {
            Text_RichTextBox.Text = text.ToString();
        }
        finally
        {
            _loadingMarkup = false;
        }

        _styles = styles.ToArray();
        _pendingStyle = null;
        PushStylesToPresenter();
        UpdateToolbar();
    }

    private void OnTextEdited(object? sender, TextEditedEventArgs e)
    {
        if (_loadingMarkup)
            return;

        RichTextStyle[] old = _styles;
        if (old.Length != e.OldText.Length)
        {
            RichTextStyle[] resized = new RichTextStyle[e.OldText.Length];
            Array.Copy(old, resized, Math.Min(old.Length, resized.Length));
            old = resized;
        }

        RichTextStyle insertStyle = _pendingStyle is RichTextStyle pending && _pendingCaret == e.Start
            ? pending
            : StyleForInsertion(old, e.Start, e.RemovedLength);

        RichTextStyle[] next = new RichTextStyle[e.NewText.Length];
        Array.Copy(old, 0, next, 0, e.Start);
        for (int i = 0; i < e.InsertedLength; i++)
            next[e.Start + i] = insertStyle;
        Array.Copy(old, e.Start + e.RemovedLength, next, e.Start + e.InsertedLength, old.Length - e.Start - e.RemovedLength);

        _styles = next;
        _pendingStyle = null;
        PushStylesToPresenter();
        PushMarkup();
    }

    private static RichTextStyle StyleForInsertion(RichTextStyle[] styles, int start, int removed)
    {
        if (styles.Length == 0)
            return default;
        if (removed > 0)
            return styles[start];
        return start > 0 ? styles[start - 1] : styles[0];
    }

    private void PushStylesToPresenter()
    {
        Text_RichTextBox.StyledPresenter?.SetStyles(_styles);
    }

    private void PushMarkup()
    {
        string markup = RichTextMarkup.Serialize(CurrentRuns());
        _lastPushedMarkup = markup;
        SetCurrentValue(MarkupProperty, markup);
    }

    private IEnumerable<RichTextRun> CurrentRuns()
    {
        string text = Text_RichTextBox.Text ?? "";
        if (text.Length != _styles.Length)
        {
            yield return new RichTextRun(text, default);
            yield break;
        }

        int runStart = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && _styles[i] == _styles[runStart])
                continue;
            yield return new RichTextRun(text.Substring(runStart, i - runStart), _styles[runStart]);
            runStart = i;
        }
    }

    private (int Start, int End) GetTargetSelection()
    {
        int start = Math.Min(Text_RichTextBox.SelectionStart, Text_RichTextBox.SelectionEnd);
        int end = Math.Max(Text_RichTextBox.SelectionStart, Text_RichTextBox.SelectionEnd);
        if (end == start && !Text_RichTextBox.IsFocused)
            (start, end) = Text_RichTextBox.LastFocusedSelection;

        int length = _styles.Length;
        return (Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
    }

    private void ApplyStyle(Func<RichTextStyle, RichTextStyle> transform)
    {
        (int start, int end) = GetTargetSelection();
        if (end > start)
        {
            for (int i = start; i < end; i++)
                _styles[i] = transform(_styles[i]);
            PushStylesToPresenter();
            PushMarkup();
        }
        else
        {
            _pendingStyle = transform(_pendingStyle ?? StyleAtCaret());
            _pendingCaret = Text_RichTextBox.CaretIndex;
        }
        UpdateToolbar();
    }

    private RichTextStyle StyleAtCaret()
    {
        if (_styles.Length == 0)
            return default;
        int caret = Math.Clamp(Text_RichTextBox.CaretIndex, 0, _styles.Length);
        return caret > 0 ? _styles[caret - 1] : _styles[0];
    }

    private RichTextStyle CurrentStyle(out bool allBold, out bool allItalic)
    {
        (int start, int end) = GetTargetSelection();
        if (end > start)
        {
            allBold = true;
            allItalic = true;
            for (int i = start; i < end; i++)
            {
                allBold &= _styles[i].Bold;
                allItalic &= _styles[i].Italic;
            }
            return _styles[start];
        }

        RichTextStyle style = _pendingStyle ?? StyleAtCaret();
        allBold = style.Bold;
        allItalic = style.Italic;
        return style;
    }

    private Color DefaultForeground =>
        Text_RichTextBox.Foreground is ISolidColorBrush brush ? brush.Color : Colors.Black;

    private void UpdateToolbar()
    {
        if (Bold_ToggleButton is null)
            return;

        RichTextStyle style = CurrentStyle(out bool allBold, out bool allItalic);
        _updatingToolbar = true;
        try
        {
            Bold_ToggleButton.IsChecked = allBold;
            Italic_ToggleButton.IsChecked = allItalic;
            FontSize_NumericUpDown.Value = (decimal)(style.FontSize ?? DefaultFontSize);
            Color_ColorPicker.Color = style.Foreground ?? DefaultForeground;
        }
        finally
        {
            _updatingToolbar = false;
        }
    }

    private void OnFontSizeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_updatingToolbar || e.NewValue is not decimal value)
            return;

        double size = (double)value;
        double? stored = Math.Abs(size - DefaultFontSize) < 0.01 ? null : size;
        ApplyStyle(s => s with { FontSize = stored });
    }

    private void OnColorChanged(object? sender, ColorChangedEventArgs e)
    {
        if (_updatingToolbar)
            return;

        Color readable = ReadableColors.Clamp(Color.FromRgb(e.NewColor.R, e.NewColor.G, e.NewColor.B));
        if (readable != e.NewColor)
        {
            _updatingToolbar = true;
            try
            {
                Color_ColorPicker.Color = readable;
            }
            finally
            {
                _updatingToolbar = false;
            }
        }
        ApplyStyle(s => s with { Foreground = readable });
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.CaretIndexProperty
            && e.Property != TextBox.SelectionStartProperty
            && e.Property != TextBox.SelectionEndProperty)
        {
            return;
        }

        if (_pendingStyle is not null && Text_RichTextBox.CaretIndex != _pendingCaret)
            _pendingStyle = null;
        UpdateToolbar();
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control)
            return;

        if (e.Key == Key.B)
        {
            CurrentStyle(out bool allBold, out _);
            bool bold = !allBold;
            ApplyStyle(s => s with { Bold = bold });
            e.Handled = true;
        }
        else if (e.Key == Key.I)
        {
            CurrentStyle(out _, out bool allItalic);
            bool italic = !allItalic;
            ApplyStyle(s => s with { Italic = italic });
            e.Handled = true;
        }
    }
}
