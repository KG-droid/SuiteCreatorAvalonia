using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SuiteCreatorAvalonia.ViewModels;
using System.Linq;

namespace SuiteCreatorAvalonia.Views;

public partial class FileTreeView : UserControl
{
    public FileTreeView()
    {
        InitializeComponent();
    }

    private void AddNewDIR_Click(object? sender, RoutedEventArgs e)
    {
        AddNewDIR(sender);
    }

    private void AddNewDIR_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
            AddNewDIR(sender);
    }

    private void AddNewDIR(object sender)
    {
        if (DataContext is FileTreeViewModel VM)
        {
            TextBox txtBox;
            Button? parentBtn;
            if (sender is Button button)
            {
                txtBox = button.GetLogicalSiblings().OfType<TextBox>().Where(t => t.Tag != null && (string)t.Tag == "NewNameTxt").FirstOrDefault();
                parentBtn = button.FindLogicalAncestorOfType<Button>();
            }
            else if (sender is TextBox)
            {
                txtBox = sender as TextBox;
                parentBtn = txtBox.FindLogicalAncestorOfType<Button>();
            }
            else { return; }
            if (string.IsNullOrWhiteSpace(txtBox.Text)) { return; }
            VM.AddNewDir(txtBox.Text);
            txtBox.Text = string.Empty;
            if (parentBtn != null)
            {
                parentBtn.Flyout?.Hide();
                Border? controlBorder = parentBtn.FindLogicalAncestorOfType<Border>();
                if (controlBorder != null)
                {
                    controlBorder.ContextFlyout?.Hide();
                }
            }
        }
    }

    private void ClearTreeNodes_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && DataContext is FileTreeViewModel VM)
        {
            VM.ClearAllTreeNodes();
            Button? parentBtn = button.FindLogicalAncestorOfType<Button>();
            if (parentBtn != null)
            {
                parentBtn.Flyout?.Hide();
            }
        }
    }

    private void DeleteTreeNodes_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && DataContext is FileTreeViewModel VM)
        {
            VM.DeleteSelected();
            Button? parentBtn = button.FindLogicalAncestorOfType<Button>();
            if (parentBtn != null)
            {
                parentBtn.Flyout?.Hide();
            }
        }
    }

    private void TreeView_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TreeView treeView)
        {
            TreeViewItem? firstItem = treeView.GetLogicalChildren().OfType<TreeViewItem>().FirstOrDefault();
            if (firstItem != null)
                firstItem.IsExpanded = true;
        }
    }
}