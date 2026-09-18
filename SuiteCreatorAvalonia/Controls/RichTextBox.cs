using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System;
using System.Collections.Generic;

namespace SuiteCreatorAvalonia.Controls;

public sealed class TextEditedEventArgs : EventArgs
{
    public TextEditedEventArgs(int start, int removedLength, int insertedLength, string oldText, string newText)
    {
        Start = start;
        RemovedLength = removedLength;
        InsertedLength = insertedLength;
        OldText = oldText;
        NewText = newText;
    }

    public int Start { get; }
    public int RemovedLength { get; }
    public int InsertedLength { get; }
    public string OldText { get; }
    public string NewText { get; }
}

/// <summary>
/// TextBox used by <see cref="RichTextEditor"/>: reports each text change as a single replaced range (so the
/// editor can splice its per-character styles), exposes the styled presenter, and remembers the selection when
/// focus leaves (TextBox clears it) so toolbar controls that take focus can still act on it.
/// </summary>
public class RichTextBox : TextBox
{
    private string _lastText = "";
    private bool _preCaptured;
    private int _preCaret;
    private int _preSelectionStart;
    private int _preSelectionEnd;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public StyledTextPresenter? StyledPresenter { get; private set; }

    public (int Start, int End) LastFocusedSelection { get; private set; }

    public event EventHandler<TextEditedEventArgs>? TextEdited;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        StyledPresenter = e.NameScope.Find<StyledTextPresenter>("PART_TextPresenter");
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        LastFocusedSelection = (Math.Min(SelectionStart, SelectionEnd), Math.Max(SelectionStart, SelectionEnd));
        base.OnLostFocus(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        CapturePreEditState();
        try
        {
            base.OnTextInput(e);
        }
        finally
        {
            _preCaptured = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        CapturePreEditState();
        try
        {
            base.OnKeyDown(e);
        }
        finally
        {
            _preCaptured = false;
        }
    }

    private void CapturePreEditState()
    {
        _preCaptured = true;
        _preCaret = CaretIndex;
        _preSelectionStart = Math.Min(SelectionStart, SelectionEnd);
        _preSelectionEnd = Math.Max(SelectionStart, SelectionEnd);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TextProperty)
        {
            string oldText = _lastText;
            string newText = change.GetNewValue<string?>() ?? "";
            _lastText = newText;
            if (oldText != newText)
                RaiseTextEdited(oldText, newText);
        }
        base.OnPropertyChanged(change);
    }

    private void RaiseTextEdited(string oldText, string newText)
    {
        int maxPrefix = Math.Min(oldText.Length, newText.Length);
        int prefix = 0;
        while (prefix < maxPrefix && oldText[prefix] == newText[prefix])
            prefix++;

        int maxSuffix = maxPrefix - prefix;
        int suffix = 0;
        while (suffix < maxSuffix && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix])
            suffix++;

        int removed = oldText.Length - prefix - suffix;
        int inserted = newText.Length - prefix - suffix;

        // Runs of identical characters make the edit position ambiguous (backspacing one 'a' of "aaa") and the
        // prefix diff always lands on the last one. Prefer the position implied by the caret/selection before
        // the edit, so the formatting dropped or inherited comes from the character the user actually edited.
        int start = prefix;
        foreach (int candidate in CandidateStarts(removed, inserted))
        {
            if (IsValidWindow(oldText, newText, candidate, removed, inserted))
            {
                start = candidate;
                break;
            }
        }

        TextEdited?.Invoke(this, new TextEditedEventArgs(start, removed, inserted, oldText, newText));
    }

    private IEnumerable<int> CandidateStarts(int removed, int inserted)
    {
        if (_preCaptured)
        {
            if (_preSelectionEnd > _preSelectionStart)
                yield return _preSelectionStart;
            yield return _preCaret - removed;
            yield return _preCaret;
        }

        int selectionStart = Math.Min(SelectionStart, SelectionEnd);
        if (Math.Max(SelectionStart, SelectionEnd) > selectionStart)
            yield return selectionStart;
        yield return CaretIndex - inserted;
        yield return CaretIndex - removed;
        yield return CaretIndex;
    }

    private static bool IsValidWindow(string oldText, string newText, int start, int removed, int inserted)
    {
        if (start < 0 || start + removed > oldText.Length || start + inserted > newText.Length)
            return false;

        int tail = oldText.Length - start - removed;
        return string.CompareOrdinal(oldText, 0, newText, 0, start) == 0
            && string.CompareOrdinal(oldText, start + removed, newText, start + inserted, tail) == 0;
    }
}
