using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.ViewModels;
using System;
using System.Linq;

namespace SuiteCreatorAvalonia.Views;

public partial class VarCMDFileTreeWindowView : Window
{
    private bool _treePointerPressedApplied = false;

    public VarCMDFileTreeWindowView()
    {
        InitializeComponent();
        FileTreeUserControl.Loaded += OnFileTreeLoaded;
    }

    private void OnFileTreeLoaded(object? sender, EventArgs e)
    {
        if (!_treePointerPressedApplied)
        {
            TreeView fileTreeView = FileTreeUserControl.GetLogicalDescendants().OfType<TreeView>().First();
            fileTreeView.AddHandler(InputElement.PointerPressedEvent, FileTree_PointerPressed, RoutingStrategies.Tunnel);
            _treePointerPressedApplied = true;
        }
    }

    private void ClosePSWindow(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FileTree_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TreeView treeView && e.ClickCount > 1 && this.DataContext is VarCMDFileTreeWindowViewModel vcVM)
        {
            if (treeView.SelectedItems != null && treeView.SelectedItems.Count > 0)
            {
                vcVM.TriggerDoubleClickEvent(treeView.SelectedItem as FileSystemNode);
            }
        }
    }
}
