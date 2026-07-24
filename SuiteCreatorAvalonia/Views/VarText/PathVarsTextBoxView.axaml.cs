using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorModels.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SuiteCreatorAvalonia.Views;

public partial class PathVarsTextBoxView : UserControl
{
    private EnvDIRVarPopupView _envPop;
    private EnvDIRVarPopupViewModel _envPopVM;
    private int _currentTxtCaret = 0;
    private int _previousTxtCaret = 0;
    private LiteralText _varCMDForCaret;
    private Control currentControl;
    private bool isLoadingVisualTree = false;
    private Window? _subscribedWindow;
    private EventHandler<WindowResizedEventArgs>? _windowResizedHandler;
    private readonly HashSet<TextBox> _attachedTextBoxes = new();

    public PathVarsTextBoxView()
    {
        InitializeComponent();
        DataContextChanged += (s, e) =>
        {
            if (DataContext is PathVarsTextBoxViewModel pVM)
            {
                SetupEnvPop(pVM);
            }
        };
    }

    private void SetupEnvPop(PathVarsTextBoxViewModel pVM)
    {
        if (pVM.DontShowUserVars)
        {
            _envPopVM = new(true);
        }
        else
        {
            _envPopVM = new(false);
        }
        _envPopVM.VariableSelected += (s, e) =>
        {
            if (e != null)
            {
                EnterVariableIntoText(e);
            }
        };
        _envPop = new EnvDIRVarPopupView { DataContext = _envPopVM };

        // Unsubscribe from previous window if any
        if (_subscribedWindow != null && _windowResizedHandler != null)
        {
            _subscribedWindow.Resized -= _windowResizedHandler;
            _subscribedWindow = null;
            _windowResizedHandler = null;
        }

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _windowResizedHandler = (s, args) =>
            {
                if (_envPop.IsOpen)
                {
                    _envPop.IsOpen = false;
                }
            };
            window.Resized += _windowResizedHandler;
            _subscribedWindow = window;
        }
    }

    private void CMD_AttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox textBox && DataContext is PathVarsTextBoxViewModel pVM && textBox.DataContext is VariableText varContext)
        {
            isLoadingVisualTree = true;

            // Remove the watermark from the first textbox if there is a variable in the PathVar control
            // The ItemsControl is efficient and doesn't repaint all the controls when you add an item.
            IEnumerable<TextBox>? siblings = textBox.FindAncestorOfType<ItemsControl>()?.GetLogicalDescendants().OfType<TextBox>();
            if (siblings != null)
            {
                if (siblings.Count() >= 1)
                {
                    foreach (TextBox sibling in siblings)
                    {
                        sibling.PlaceholderText = null;
                    }
                }
            }

            if (_varCMDForCaret == null || _varCMDForCaret == varContext)
            {
                textBox.CaretIndex = _currentTxtCaret;
                textBox.Focus();
                currentControl = textBox;
            }

            // Only attach PropertyChanged once per TextBox to avoid handler accumulation
            if (!_attachedTextBoxes.Contains(textBox))
            {
                _attachedTextBoxes.Add(textBox);
                textBox.DetachedFromVisualTree += (s, args) => _attachedTextBoxes.Remove(textBox);
                textBox.PropertyChanged += (s, args) =>
                {
                    // Keep record of caret for when amending variables
                    if (!isLoadingVisualTree && sender is TextBox propChangedTxtBox && propChangedTxtBox.DataContext is LiteralText varContext)
                    {
                        if (args.Property.Name == nameof(TextBox.CaretIndex))
                        {
                            currentControl = propChangedTxtBox;
                            if (_varCMDForCaret == varContext)
                            {
                                _previousTxtCaret = _currentTxtCaret;
                            }
                            else
                            {
                                _varCMDForCaret = varContext;
                                _previousTxtCaret = propChangedTxtBox.CaretIndex;
                            }
                            _currentTxtCaret = propChangedTxtBox.CaretIndex;
                        }
                        if (args.Property.Name == nameof(TextBox.Text))
                        {
                            // Trigger CMD changed on attached VM
                            if (DataContext is PathVarsTextBoxViewModel pVM)
                            {
                                pVM.TriggerCMDChanged();
                            }
                        }
                    }

                };
            }

            if (pVM.InternalVariablePath.Count == 1 && string.IsNullOrWhiteSpace(textBox.Text))
            {
                // Allow for custom watermarks
                if (string.IsNullOrWhiteSpace(pVM.PlaceholderText))
                    textBox.PlaceholderText = "Enter a command. Variables can be added by using the { character";
                else
                    textBox.PlaceholderText = pVM.PlaceholderText;
            }
            if (pVM.InternalVariablePath.IndexOf(varContext) == pVM.InternalVariablePath.Count - 1)
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    // Set focus to the end of the last text box
                    textBox.Focus();
                    textBox.CaretIndex = textBox.Text.Length;
                }
                isLoadingVisualTree = false;
            }
        }
    }

    private void EnterVariableIntoText(string specialFolderName)
    {
        SpecialDIR newVar = new SpecialDIR((SpecialFolderVar)Enum.Parse(typeof(SpecialFolderVar), specialFolderName));
        EnterVariableIntoText(newVar);
    }

    private void EnterVariableIntoText(VariableText var)
    {
        if (currentControl is TextBox textBx)
        {
            string? txtBeforeCaret = null;
            string? txtAfterCaret = null;
            if (!string.IsNullOrWhiteSpace(textBx.Text))
            {
                txtBeforeCaret = textBx.Text?.Substring(0, textBx.CaretIndex);
                txtAfterCaret = textBx.Text?.Substring(textBx.CaretIndex);
                string amendedTxt = Regex.Replace(txtBeforeCaret, @"{[^{}\s\n]*$", "");
                textBx.Text = Regex.Replace(textBx.Text, $@"^{Regex.Escape(txtBeforeCaret)}", amendedTxt);
                if (!string.IsNullOrWhiteSpace(txtAfterCaret))
                    textBx.Text = Regex.Replace(textBx.Text, $@"{Regex.Escape(txtAfterCaret)}$", "");
            }
            PathVarsTextBoxView? parentPathVarsTxtBx = textBx.GetLogicalAncestors().OfType<PathVarsTextBoxView>().FirstOrDefault();
            if (parentPathVarsTxtBx != null && parentPathVarsTxtBx.DataContext is PathVarsTextBoxViewModel pVM)
            {
                if (textBx.DataContext is VariableText boundCmd)
                {
                    int txtIndex = pVM.InternalVariablePath.IndexOf(boundCmd);
                    pVM.InternalVariablePath.Insert(txtIndex + 1, var);
                    LiteralText newCMDAfterVar = new LiteralText(string.Empty);
                    if (!string.IsNullOrWhiteSpace(txtAfterCaret))
                        newCMDAfterVar.Value = txtAfterCaret;
                    pVM.InternalVariablePath.Insert(txtIndex + 2, newCMDAfterVar);
                    _varCMDForCaret = newCMDAfterVar;
                    _currentTxtCaret = 0;
                    _previousTxtCaret = 0;
                    pVM.TriggerCMDChanged();
                }
            }
        }
    }

    private void VarTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (
            sender is TextBox textBox &&
            !string.IsNullOrWhiteSpace(textBox.Text) &&
            Regex.IsMatch(textBox.Text.Substring(0, textBox.CaretIndex), @"{[^{}\s\n]*$")
        )
        {
            // Find the TextPresenter in the TextBox's visual tree
            var textPresenter = textBox.GetVisualDescendants()
                                        .OfType<TextPresenter>()
                                        .FirstOrDefault();
            if (textPresenter == null) return;

            // The TextPresenter contains a TextLayout, which we can use to get the caret position
            var textLayout = textPresenter.TextLayout;
            if (textLayout == null) return;
            var caretIndex = textBox.CaretIndex;
            var point = textLayout.HitTestTextPosition(caretIndex).BottomLeft;

            // Adjust for any scroll offset
            var scrollViewer = textBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            double scrollOffset = scrollViewer?.Offset.X ?? 0;
            double caretX = point.X - scrollOffset + textBox.Padding.Left;

            // Configure and show the popup
            _envPop.PlacementTarget = textBox;
            _envPop.HorizontalOffset = caretX;
            _envPop.IsOpen = true;

            // Set the matching folder vars
            Match variableNameSearchTerm = Regex.Match(textBox.Text.Substring(0, textBox.CaretIndex), @"{(?'VariableName'[^{}\s\n]*)$");
            ((EnvDIRVarPopupViewModel)_envPop.DataContext).SearchTerm = variableNameSearchTerm.Groups["VariableName"].Value;
            _envPopVM.SelectedItem = _envPopVM.FilteredVariableNames.FirstOrDefault();

            // Configure events to close the popup when needed
            if (textBox.Tag == null || !(bool)textBox.Tag)
            {
                textBox.TextChanged += (s, args) =>
                {
                    if (
                        sender is TextBox textBox &&
                        (
                            string.IsNullOrWhiteSpace(textBox.Text) ||
                            !Regex.IsMatch(textBox.Text.Substring(0, textBox.CaretIndex), @"{[^{}\s\n]*$")
                        )
                    )
                    {
                        _envPop.IsOpen = false;
                    }
                };
            }
            textBox.Tag = true;
        }
    }

    private void VarTextBox_KeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (sender is TextBox txtBox && txtBox.DataContext is LiteralText txtCMD && this.DataContext is PathVarsTextBoxViewModel pVM)
        {
            switch (e.Key)
            {
                case Key.Left:
                    if (
                        _previousTxtCaret == 0 && _currentTxtCaret == 0 &&
                        pVM.InternalVariablePath[Math.Max(0, pVM.InternalVariablePath.IndexOf(txtCMD) - 2)] is LiteralText prevCMD &&
                        pVM.InternalVariablePath.IndexOf(txtCMD) != 0
                    )
                    {
                        _varCMDForCaret = prevCMD;
                        _currentTxtCaret = prevCMD.Value.Length;
                        _previousTxtCaret = prevCMD.Value.Length;
                        RefocusOnStoredCaret();
                    }
                    else if (_currentTxtCaret == 0 && _previousTxtCaret == 1)
                    {
                        _previousTxtCaret = 0;
                    }
                    break;
                case Key.Right:
                    if (
                        (string.IsNullOrEmpty(txtBox.Text) || _currentTxtCaret == txtBox.Text.Length) &&
                        _previousTxtCaret == txtBox.Text?.Length &&
                        pVM.InternalVariablePath[Math.Min(pVM.InternalVariablePath.Count - 1, pVM.InternalVariablePath.IndexOf(txtCMD) + 2)] is LiteralText nextCMD &&
                        pVM.InternalVariablePath.IndexOf(txtCMD) != pVM.InternalVariablePath.Count - 1
                    )
                    {
                        _currentTxtCaret = 0;
                        _previousTxtCaret = 0;
                        _varCMDForCaret = nextCMD;
                        RefocusOnStoredCaret();
                    }
                    else if (_currentTxtCaret == txtBox.Text?.Length && _previousTxtCaret == txtBox.Text?.Length - 1)
                    {
                        _previousTxtCaret = txtBox.Text.Length;
                    }
                    break;
                case Key.Back:
                case Key.Delete:
                    if (txtBox.CaretIndex == 0 && _previousTxtCaret == 0)
                    {
                        DeletePriorVariable(txtCMD);
                    }
                    if (txtBox.CaretIndex == 0 && _previousTxtCaret == 1)
                    {
                        _previousTxtCaret = 0;
                    }
                    break;
                case Key.Up:
                    if (_envPop.IsOpen)
                    {
                        var listBox = _envPop.GetLogicalDescendants().OfType<ListBox>().FirstOrDefault();
                        if (listBox != null && listBox.SelectedIndex > 0)
                        {
                            listBox.SelectedIndex--;
                        }
                    }
                    break;
                case Key.Down:
                    {
                        var listBox = _envPop.GetLogicalDescendants().OfType<ListBox>().FirstOrDefault();
                        if (listBox != null && listBox.SelectedIndex < listBox.Items.Count - 1)
                        {
                            listBox.SelectedIndex++;
                        }
                    }
                    break;
                case Key.Return:
                    {
                        if (!_envPop.IsOpen) return;
                        var listBox = _envPop.GetLogicalDescendants().OfType<ListBox>().FirstOrDefault();
                        if (listBox != null && listBox.SelectedItem != null)
                        {
                            EnterVariableIntoText(listBox.SelectedItem.ToString());
                        }
                    }
                    break;
                case Key.Escape:
                    {
                        if (_envPop.IsOpen)
                            _envPop.IsOpen = false;
                    }
                    break;
            }
        }
    }

    private void RefocusOnStoredCaret()
    {
        // Keep focus after adding variables
        TextBox? matchingTxtBox = PathVars_ItemsControl.GetLogicalDescendants()
            .OfType<TextBox>()
            .Where(x => x.DataContext == _varCMDForCaret)
            .FirstOrDefault();
        if (matchingTxtBox != null)
        {
            matchingTxtBox.Focus();
            matchingTxtBox.CaretIndex = _currentTxtCaret;
            currentControl = matchingTxtBox;
        }
    }

    private void DeletePriorVariable(LiteralText currentVariable)
    {
        if (this.DataContext is PathVarsTextBoxViewModel pVM)
        {
            VariableText varBefore = pVM.InternalVariablePath[Math.Max(0, pVM.InternalVariablePath.IndexOf(currentVariable) - 1)];
            if (varBefore is not LiteralText)
            {
                pVM.InternalVariablePath.Remove(varBefore);
                varBefore = pVM.InternalVariablePath[Math.Max(0, pVM.InternalVariablePath.IndexOf(currentVariable) - 1)];
                if (varBefore is LiteralText cmdBeforeDeleted)
                {
                    _varCMDForCaret = cmdBeforeDeleted;
                    _previousTxtCaret = string.IsNullOrEmpty(cmdBeforeDeleted.Value) ? 0 : cmdBeforeDeleted.Value.Length;
                    _currentTxtCaret = _previousTxtCaret;
                    cmdBeforeDeleted.Value += currentVariable.Value;
                }
                pVM.InternalVariablePath.Remove(currentVariable);
                pVM.TriggerCMDChanged();
                RefocusOnStoredCaret();
            }
        }
    }

    private void OpenFileTreeModelWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is PathVarsTextBoxViewModel pVM)
        {
            Window varTreeWindow = new VarCMDFileTreeWindowView();
            VarCMDFileTreeWindowViewModel vm = new VarCMDFileTreeWindowViewModel(pVM.TreeNodes);
            vm.DoubleClickedTreeNode += (s, args) =>
            {
                if (args != null && args is FileSystemNode fileSystemNode)
                {
                    EnterVariableIntoText(new FileVar(fileSystemNode));
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
}
