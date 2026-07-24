using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using System;
using System.Collections.ObjectModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class VarCMDFileTreeWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private FileTreeViewModel? _fileTreeVM;

        // Custom event declaration
        public event EventHandler<FileSystemNode?>? DoubleClickedTreeNode;

        public void TriggerDoubleClickEvent(FileSystemNode selectedNode)
        {
            DoubleClickedTreeNode?.Invoke(this, selectedNode);
        }

        public VarCMDFileTreeWindowViewModel() : this(
            new ObservableCollection<FileSystemNode> { new FileSystemNode("VarTextRoot", new ObservableCollection<FileSystemNode>()) })
        {
        }

        public VarCMDFileTreeWindowViewModel(ObservableCollection<FileSystemNode> fileTree)
        {
            FileTreeVM = new(fileTree);
        }
    }
}
