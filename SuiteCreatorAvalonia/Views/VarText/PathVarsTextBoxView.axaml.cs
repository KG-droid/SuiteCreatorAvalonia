using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorModels.Enums;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SuiteCreatorAvalonia.Views;

/// <summary>
/// A single-line command/path editor. Variables (special folders, package files) live in the
/// underlying AvaloniaEdit document as a single placeholder character each, rendered as inline
/// pills by VariableTokenElementGenerator - so the whole command is one text surface with one
/// caret and one horizontal scroll, and a variable behaves as one atomic character.
/// </summary>
public partial class PathVarsTextBoxView : UserControl
{
    internal const char TokenChar = '￼'; // Object Replacement Character - can't be typed, one per variable

    private static readonly Regex OpenVariableRegex = new("\\{(?'VariableName'[^\\{\\}\\s￼]*)$", RegexOptions.Compiled);
    private static readonly char[] IllegalChars = { '\r', '\n', TokenChar };

    private sealed class VariableToken
    {
        public required TextAnchor Anchor { get; init; }
        public required VariableText Variable { get; init; }
    }

    private sealed record PillClipboardPayload(string PlainText, string RawText, IReadOnlyList<(int Offset, VariableText Variable)> Tokens);

    // Shared across all PathVars boxes so pills survive copy/paste between them. The system
    // clipboard only carries the plain-text form; the payload is used when the clipboard text
    // still matches what we last copied.
    private static PillClipboardPayload? s_pillClipboard;

    private readonly List<VariableToken> _tokens = new();
    private EnvDIRVarPopupView? _envPop;
    private EnvDIRVarPopupViewModel? _envPopVM;
    private PathVarsTextBoxViewModel? _vm;
    private bool _internalDocChange;
    private bool _pushingToVM;
    private Window? _subscribedWindow;
    private EventHandler<WindowResizedEventArgs>? _windowResizedHandler;

    public PathVarsTextBoxView()
    {
        InitializeComponent();

        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.AllowScrollBelowDocument = false;
        Editor.TextArea.TextView.ElementGenerators.Add(new VariableTokenElementGenerator(TokenAt));
        Editor.TextArea.TextEntering += Editor_TextEntering;
        Editor.TextChanged += Editor_TextChanged;
        Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        Editor.TextArea.AddHandler(KeyDownEvent, Editor_PreviewKeyDown, RoutingStrategies.Tunnel);
        // Match the selection colour every TextBox in the app uses (App.axaml's global TextBox
        // style) - AvaloniaEdit doesn't pick that style up since TextArea isn't a TextBox.
        Editor.TextArea.Bind(TextArea.SelectionBrushProperty, new DynamicResourceExtension("AccentVariant2"));
        OuterBorder.PointerPressed += OuterBorder_PointerPressed;

        DataContextChanged += (s, e) => HookViewModel();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeWindowResized();
        // AvaloniaEdit's TextView fills whatever height it's given and draws the line starting at
        // its top, rather than centering it (unlike TextBox) - so without an explicit line-height
        // Height, the single line renders pinned to the top of the 34px pill. DynamicResource fonts
        // only resolve once attached, so defer this a frame past attachment.
        Dispatcher.UIThread.Post(SizeEditorToLineHeight, DispatcherPriority.Loaded);
    }

    private void SizeEditorToLineHeight()
    {
        double lineHeight = Editor.TextArea.TextView.DefaultLineHeight;
        Editor.Height = lineHeight > 0 ? lineHeight : Editor.FontSize * 1.3;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseEnvPop();
        UnsubscribeWindowResized();
        base.OnDetachedFromVisualTree(e);
    }

    private void HookViewModel()
    {
        if (_vm != null)
        {
            _vm.VariablePath.CollectionChanged -= VariablePath_CollectionChanged;
            _vm.PropertyChanged -= VM_PropertyChanged;
        }
        _vm = DataContext as PathVarsTextBoxViewModel;
        if (_vm == null) return;
        _vm.VariablePath.CollectionChanged += VariablePath_CollectionChanged;
        _vm.PropertyChanged += VM_PropertyChanged;
        SetupEnvPop();
        RebuildDocumentFromVM();
    }

    private void VM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PathVarsTextBoxViewModel.DontShowUserVars))
            SetupEnvPop();
        else if (e.PropertyName == nameof(PathVarsTextBoxViewModel.PlaceholderText))
            UpdateWatermark();
    }

    private void SetupEnvPop()
    {
        if (_vm == null) return;
        CloseEnvPop();
        _envPopVM = new EnvDIRVarPopupViewModel(_vm.DontShowUserVars);
        _envPopVM.VariableSelected += (s, name) =>
        {
            if (!string.IsNullOrEmpty(name))
                InsertSpecialVariable(name);
        };
        _envPop = new EnvDIRVarPopupView { DataContext = _envPopVM };
        SubscribeWindowResized();
    }

    private void SubscribeWindowResized()
    {
        UnsubscribeWindowResized();
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _windowResizedHandler = (s, args) => CloseEnvPop();
            window.Resized += _windowResizedHandler;
            _subscribedWindow = window;
        }
    }

    private void UnsubscribeWindowResized()
    {
        if (_subscribedWindow != null && _windowResizedHandler != null)
            _subscribedWindow.Resized -= _windowResizedHandler;
        _subscribedWindow = null;
        _windowResizedHandler = null;
    }

    // ------------------------------------------------------------------
    // Document <-> VariablePath sync
    // ------------------------------------------------------------------

    private void VariablePath_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_pushingToVM) return;
        RebuildDocumentFromVM();
    }

    private void RebuildDocumentFromVM()
    {
        if (_vm == null) return;
        StringBuilder sb = new();
        List<(int Offset, VariableText Variable)> pending = new();
        foreach (VariableText segment in _vm.VariablePath)
        {
            if (segment is LiteralText lit)
            {
                sb.Append(StripIllegalChars(lit.Value));
            }
            else
            {
                pending.Add((sb.Length, segment.Clone()));
                sb.Append(TokenChar);
            }
        }
        _internalDocChange = true;
        try
        {
            _tokens.Clear();
            TextDocument doc = Editor.Document;
            doc.Text = sb.ToString();
            foreach ((int offset, VariableText variable) in pending)
                _tokens.Add(new VariableToken { Anchor = CreateTokenAnchor(offset), Variable = variable });
            doc.UndoStack.ClearAll();
            Editor.TextArea.TextView.Redraw();
        }
        finally
        {
            _internalDocChange = false;
        }
        UpdateWatermark();
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_internalDocChange) return;
        SanitizeDocument();
        PruneDeadTokens();
        PushToViewModel();
        UpdateWatermark();
        // Popup placement needs the visual line for the new text, which is built after layout
        Dispatcher.UIThread.Post(EvaluateEnvPop, DispatcherPriority.Background);
    }

    /// <summary>
    /// Removes newlines and any placeholder characters that don't map to a token (e.g. pasted in).
    /// </summary>
    private void SanitizeDocument()
    {
        TextDocument doc = Editor.Document;
        string text = doc.Text;
        List<int>? removeOffsets = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n' || (c == TokenChar && TokenAt(i) == null))
                (removeOffsets ??= new()).Add(i);
        }
        if (removeOffsets == null) return;
        _internalDocChange = true;
        try
        {
            for (int i = removeOffsets.Count - 1; i >= 0; i--)
                doc.Remove(removeOffsets[i], 1);
        }
        finally
        {
            _internalDocChange = false;
        }
    }

    private void PruneDeadTokens()
    {
        _tokens.RemoveAll(t => t.Anchor.IsDeleted);
    }

    private VariableText? TokenAt(int offset)
    {
        foreach (VariableToken token in _tokens)
        {
            if (!token.Anchor.IsDeleted && token.Anchor.Offset == offset)
                return token.Variable;
        }
        return null;
    }

    private void PushToViewModel()
    {
        if (_vm == null) return;
        List<VariableText> path = ParseDocument();
        _pushingToVM = true;
        try
        {
            _vm.SetFromEditor(path);
        }
        finally
        {
            _pushingToVM = false;
        }
    }

    private List<VariableText> ParseDocument()
    {
        string text = Editor.Document.Text;
        List<VariableText> result = new();
        StringBuilder literal = new();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == TokenChar && TokenAt(i) is VariableText variable)
            {
                result.Add(new LiteralText(literal.ToString()));
                literal.Clear();
                result.Add(variable);
            }
            else
            {
                literal.Append(text[i]);
            }
        }
        result.Add(new LiteralText(literal.ToString()));
        return result;
    }

    private TextAnchor CreateTokenAnchor(int offset)
    {
        TextAnchor anchor = Editor.Document.CreateAnchor(offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        anchor.SurviveDeletion = false;
        return anchor;
    }

    private static string StripIllegalChars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(IllegalChars) < 0) return value;
        StringBuilder sb = new(value.Length);
        foreach (char c in value)
        {
            if (c != '\r' && c != '\n' && c != TokenChar)
                sb.Append(c);
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Variable insertion
    // ------------------------------------------------------------------

    private void InsertSpecialVariable(string specialFolderName)
    {
        SpecialDIR newVar = new SpecialDIR((SpecialFolderVar)Enum.Parse(typeof(SpecialFolderVar), specialFolderName));
        InsertVariable(newVar);
    }

    private void InsertVariable(VariableText variable)
    {
        TextDocument doc = Editor.Document;
        int caret = Math.Clamp(Editor.CaretOffset, 0, doc.TextLength);
        Match match = OpenVariableRegex.Match(doc.GetText(0, caret));
        int insertPos = caret;
        _internalDocChange = true;
        try
        {
            if (match.Success)
            {
                // Swallow the "{partial" trigger text the variable is replacing
                doc.Remove(match.Index, caret - match.Index);
                insertPos = match.Index;
            }
            doc.Insert(insertPos, TokenChar.ToString());
            _tokens.Add(new VariableToken { Anchor = CreateTokenAnchor(insertPos), Variable = variable.Clone() });
            // doc.Insert() already triggered a visual line rebuild synchronously, before the token
            // above existed - so that pass rendered the raw placeholder char instead of a pill.
            // Redraw again now that the lookup will actually find it.
            Editor.TextArea.TextView.Redraw();
        }
        finally
        {
            _internalDocChange = false;
        }
        Editor.CaretOffset = insertPos + 1;
        CloseEnvPop();
        PruneDeadTokens();
        PushToViewModel();
        UpdateWatermark();
        Editor.TextArea.Focus();
    }

    // ------------------------------------------------------------------
    // Input handling
    // ------------------------------------------------------------------

    private void Editor_TextEntering(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        if (e.Text.IndexOfAny(IllegalChars) >= 0)
            e.Handled = true;
    }

    private void Editor_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Pill-aware clipboard handling (the built-in editor commands would lose the tokens)
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if ((ctrl && e.Key == Key.C) || (ctrl && !shift && e.Key == Key.Insert))
        {
            CopySelection(cut: false);
            e.Handled = true;
            return;
        }
        if ((ctrl && e.Key == Key.X) || (shift && e.Key == Key.Delete))
        {
            CopySelection(cut: true);
            e.Handled = true;
            return;
        }
        if ((ctrl && e.Key == Key.V) || (shift && e.Key == Key.Insert))
        {
            PasteAtCaret();
            e.Handled = true;
            return;
        }

        if (_envPop?.IsOpen == true && _envPopVM != null)
        {
            switch (e.Key)
            {
                case Key.Up:
                    MovePopupSelection(-1);
                    e.Handled = true;
                    return;
                case Key.Down:
                    MovePopupSelection(1);
                    e.Handled = true;
                    return;
                case Key.Return:
                case Key.Tab:
                    if (_envPopVM.SelectedItem is string name && !string.IsNullOrEmpty(name))
                    {
                        InsertSpecialVariable(name);
                        e.Handled = true;
                        return;
                    }
                    break;
                case Key.Escape:
                    CloseEnvPop();
                    e.Handled = true;
                    return;
            }
        }
        if (e.Key == Key.Return)
            e.Handled = true; // single-line editor
    }

    // ------------------------------------------------------------------
    // Pill-aware clipboard
    // ------------------------------------------------------------------

    private async void CopySelection(bool cut)
    {
        try
        {
            TextDocument doc = Editor.Document;
            int start = Editor.SelectionStart;
            int length = Editor.SelectionLength;
            if (length == 0)
            {
                // No selection: treat the whole command as the copy target
                start = 0;
                length = doc.TextLength;
            }
            if (length == 0) return;

            string raw = doc.GetText(start, length);
            List<(int Offset, VariableText Variable)> tokens = new();
            foreach (VariableToken token in _tokens)
            {
                if (!token.Anchor.IsDeleted && token.Anchor.Offset >= start && token.Anchor.Offset < start + length)
                    tokens.Add((token.Anchor.Offset - start, token.Variable.Clone()));
            }
            tokens.Sort((a, b) => a.Offset.CompareTo(b.Offset));

            // Plain-text form for pasting outside these boxes: pills become their textual value
            StringBuilder plain = new();
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == TokenChar)
                {
                    VariableText? variable = tokens.FirstOrDefault(t => t.Offset == i).Variable;
                    if (variable != null)
                        plain.Append(GetPlainText(variable));
                }
                else
                {
                    plain.Append(raw[i]);
                }
            }

            s_pillClipboard = new PillClipboardPayload(plain.ToString(), raw, tokens);
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(plain.ToString());

            if (cut)
                doc.Remove(start, length); // normal change path prunes tokens and pushes to the VM
        }
        catch
        {
            // Clipboard access can fail transiently; losing a copy beats crashing the editor
        }
    }

    private async void PasteAtCaret()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            string? text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrEmpty(text)) return;

            PillClipboardPayload? payload = s_pillClipboard;
            bool usePayload = payload != null && payload.Tokens.Count > 0 && payload.PlainText == text;
            string insertText = usePayload ? payload!.RawText : StripIllegalChars(text);
            if (insertText.Length == 0) return;

            TextDocument doc = Editor.Document;
            int insertPos;
            _internalDocChange = true;
            try
            {
                insertPos = Math.Clamp(Editor.CaretOffset, 0, doc.TextLength);
                if (Editor.SelectionLength > 0)
                {
                    insertPos = Editor.SelectionStart;
                    doc.Remove(Editor.SelectionStart, Editor.SelectionLength);
                }
                doc.Insert(insertPos, insertText);
                if (usePayload)
                {
                    foreach ((int offset, VariableText variable) in payload!.Tokens)
                        _tokens.Add(new VariableToken { Anchor = CreateTokenAnchor(insertPos + offset), Variable = variable.Clone() });
                    // doc.Insert() already triggered a visual line rebuild synchronously, before the
                    // tokens above existed - so that pass rendered raw placeholder chars instead of
                    // pills. Redraw again now that the lookups will actually find them.
                    Editor.TextArea.TextView.Redraw();
                }
            }
            finally
            {
                _internalDocChange = false;
            }
            Editor.SelectionLength = 0;
            Editor.CaretOffset = insertPos + insertText.Length;
            PruneDeadTokens();
            PushToViewModel();
            UpdateWatermark();
            Dispatcher.UIThread.Post(EvaluateEnvPop, DispatcherPriority.Background);
        }
        catch
        {
            // Clipboard access can fail transiently; a dropped paste beats crashing the editor
        }
    }

    private static string GetPlainText(VariableText variable)
    {
        return variable switch
        {
            FileVar fileVar => fileVar.Node.FullPath,
            SpecialDIR specialDir => "{" + specialDir.Value + "}",
            RelativeFileVar relativeVar => relativeVar.RelativePath ?? string.Empty,
            _ => variable.GetValue() ?? string.Empty
        };
    }

    private void MovePopupSelection(int delta)
    {
        if (_envPopVM == null) return;
        List<string> items = _envPopVM.FilteredVariableNames;
        if (items.Count == 0) return;
        int index = _envPopVM.SelectedItem != null ? items.IndexOf(_envPopVM.SelectedItem) : -1;
        index = Math.Clamp(index + delta, 0, items.Count - 1);
        _envPopVM.SelectedItem = items[index];
    }

    private void OuterBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Clicks on the pills or the add-file button shouldn't move focus into the editor
        if (e.Source is Control control && control.FindAncestorOfType<Button>(true) != null) return;
        Editor.TextArea.Focus();
    }

    // ------------------------------------------------------------------
    // '{' variable popup
    // ------------------------------------------------------------------

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        if (_envPop?.IsOpen == true && !OpenVariableRegex.IsMatch(TextBeforeCaret()))
            CloseEnvPop();
    }

    private string TextBeforeCaret()
    {
        int caret = Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength);
        return Editor.Document.GetText(0, caret);
    }

    private void EvaluateEnvPop()
    {
        if (_envPop == null || _envPopVM == null) return;
        Match match = OpenVariableRegex.Match(TextBeforeCaret());
        if (!match.Success)
        {
            CloseEnvPop();
            return;
        }
        _envPopVM.SearchTerm = match.Groups["VariableName"].Value;
        _envPopVM.SelectedItem = _envPopVM.FilteredVariableNames.FirstOrDefault();
        double caretX = 0;
        try
        {
            Editor.TextArea.TextView.EnsureVisualLines();
            Point visual = Editor.TextArea.TextView.GetVisualPosition(Editor.TextArea.Caret.Position, VisualYPosition.LineBottom);
            caretX = visual.X - Editor.TextArea.TextView.ScrollOffset.X;
        }
        catch
        {
            // Visual line not available yet - fall back to the editor's left edge
        }
        _envPop.PlacementTarget = Editor;
        _envPop.HorizontalOffset = caretX;
        _envPop.IsOpen = true;
    }

    private void CloseEnvPop()
    {
        if (_envPop != null)
            _envPop.IsOpen = false;
    }

    private void UpdateWatermark()
    {
        bool empty = Editor.Document.TextLength == 0;
        Watermark.IsVisible = empty;
        if (empty)
        {
            Watermark.Text = string.IsNullOrWhiteSpace(_vm?.PlaceholderText)
                ? "Enter a command. Variables can be added by using the { character. Files can be added on the file button on the right."
                : _vm!.PlaceholderText;
        }
    }

    // ------------------------------------------------------------------
    // Add-file window
    // ------------------------------------------------------------------

    private void OpenFileTreeModelWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        Window varTreeWindow = new VarCMDFileTreeWindowView();
        VarCMDFileTreeWindowViewModel vm = new VarCMDFileTreeWindowViewModel(_vm.TreeNodes);
        vm.DoubleClickedTreeNode += (s, args) =>
        {
            if (args != null && args is FileSystemNode fileSystemNode)
            {
                InsertVariable(new FileVar(fileSystemNode));
            }
        };
        varTreeWindow.DataContext = vm;
        Window parent = TopLevel.GetTopLevel(this) as Window;
        varTreeWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        varTreeWindow.Height = 300;
        EventHandler? layoutUpdateHandler = null;
        layoutUpdateHandler = (s, args) =>
        {
            varTreeWindow.Width = this.Bounds.Width;
            var screenPoint = this.PointToScreen(new Point(0, this.Bounds.Height));
            varTreeWindow.Position = new PixelPoint(
                (int)screenPoint.X,
                (int)screenPoint.Y
            );
        };
        parent.LayoutUpdated += layoutUpdateHandler;
        EventHandler<FocusChangedEventArgs>? handler = null;
        handler = (s, args) =>
        {
            if (varTreeWindow.IsVisible)
            {
                varTreeWindow.Close();
            }
            parent.GotFocus -= handler;
            parent.LayoutUpdated -= layoutUpdateHandler;
        };
        parent.GotFocus += handler;
        varTreeWindow.Show();
    }
}
