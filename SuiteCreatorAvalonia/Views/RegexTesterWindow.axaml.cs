using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteCreatorAvalonia.ViewModels;
using System;
using System.Linq;

namespace SuiteCreatorAvalonia.Views;

public partial class RegexTesterWindow : Window
{
    public RegexTesterWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is RegexTesterWindowViewModel vm)
            {
                SampleEditor.TextArea.TextView.LineTransformers.Add(new RegexMatchColorizer(() => vm.MatchRanges));
                vm.MatchesUpdated += () => SampleEditor.TextArea.TextView.Redraw();

                PatternEditor.TextArea.TextView.LineTransformers.Add(new RegexPatternColorizer(() => vm.PatternTokens));
                vm.PatternTokensUpdated += () => PatternEditor.TextArea.TextView.Redraw();

                PatternEditor.PointerMoved += (s2, e2) => ShowTokenTooltip(vm, e2);
                PatternEditor.PointerExited += (s2, e2) => ToolTip.SetIsOpen(PatternEditor, false);
            }
        };
    }

    // Shows a tooltip describing the regex construct under the pointer (e.g. "\d" -> "Digit
    // (0-9)"), the same way regex101's pattern bar explains tokens on hover.
    private void ShowTokenTooltip(RegexTesterWindowViewModel vm, PointerEventArgs e)
    {
        var point = e.GetPosition(PatternEditor);
        var pos = PatternEditor.GetPositionFromPoint(point);
        if (pos.HasValue)
        {
            int offset = PatternEditor.Document.GetOffset(pos.Value.Line, pos.Value.Column);
            RegexToken? token = vm.PatternTokens.FirstOrDefault(t => offset >= t.Start && offset < t.Start + t.Length);
            if (token != null)
            {
                ToolTip.SetTip(PatternEditor, $"{token.Category}: {token.Description}");
                ToolTip.SetIsOpen(PatternEditor, true);
                return;
            }
        }
        ToolTip.SetIsOpen(PatternEditor, false);
    }

    // Inserts a builder-panel snippet into the pattern at the current caret position, then
    // returns focus to the editor so the admin can keep typing.
    private void InsertToken_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control && control.Tag is string token && DataContext is RegexTesterWindowViewModel vm)
        {
            int offset = Math.Clamp(PatternEditor.TextArea.Caret.Offset, 0, vm.PatternDoc.TextLength);
            vm.PatternDoc.Insert(offset, token);
            PatternEditor.TextArea.Caret.Offset = offset + token.Length;
            PatternEditor.Focus();
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void UsePattern(object sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
