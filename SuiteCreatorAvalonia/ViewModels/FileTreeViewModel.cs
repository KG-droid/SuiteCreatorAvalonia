using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SuiteCreatorAvalonia.Services.NodeFileSystem;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class FileTreeViewModel : ViewModelBase
    {
        private CancellationTokenSource? _fileOperationTS;

        private ObservableCollection<FileSystemNode> _treeNodes;

        public ObservableCollection<FileSystemNode> TreeNodes
        {
            get => _treeNodes;
        }

        [ObservableProperty]
        private ObservableCollection<FileSystemNode> _selectedFileTreeNodes = new();

        [ObservableProperty]
        private bool _isSingleDIRSelected = false;

        [ObservableProperty]
        private bool _isItemsSelected = false;

        [ObservableProperty]
        private bool _isFilesSelected = false;

        [ObservableProperty]
        private bool _isSingleFileSelected = false;

        [ObservableProperty]
        private bool _isLoadingFiles = false;

        [ObservableProperty]
        private bool _isControlsVisible = true;

        [ObservableProperty]
        private string? _progressMessage;

        [ObservableProperty]
        private double? _progressPercentage;

        [ObservableProperty]
        private string? _error;

        public FileTreeViewModel() : this(new ObservableCollection<FileSystemNode> { new FileSystemNode("Root", new ObservableCollection<FileSystemNode>()) }) { }

        public FileTreeViewModel(ObservableCollection<FileSystemNode> fileTree)
        {
            if (fileTree == null) throw new ArgumentNullException(nameof(fileTree), "The provided TreeNode is null, cannot create the TreeView");
            _treeNodes = fileTree;
            _treeNodes.CollectionChanged += ((sender, e) =>
            {
                if (_treeNodes.Count > 0)
                {
                    SelectedFileTreeNodes.Clear();
                    SelectedFileTreeNodes.Add(_treeNodes.First());
                }
            });
            SelectedFileTreeNodes.CollectionChanged += ((sender, e) =>
            {
                if (sender is ObservableCollection<FileSystemNode> value)
                {
                    IsSingleDIRSelected = value.Count == 1 && !value.First().IsFile;
                    IsFilesSelected = value.Count > 0 && value.Any(node => node.IsFile);
                    IsItemsSelected = value.Count > 0 && value.Any(node => node.Name != TreeNodes.First().Name);
                    IsSingleFileSelected = value.Count == 1 && value.Any(node => node.IsFile);
                }
            });
            SelectedFileTreeNodes.Add(_treeNodes.First());
        }

        [RelayCommand]
        public async Task BrowsePackageFilesDir()
        {
            IEnumerable<string>? result = await this.OpenFolderDialogAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = "Browse for a directory to import" });
            if (result != null)
                await ImportDirectory(result.First());
        }

        [RelayCommand]
        public async Task BrowsePackageFiles()
        {
            IEnumerable<string>? results = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = true, Title = "Browse for file/s to import" });
            if (results != null)
                ImportFiles(results);
        }

        [RelayCommand]
        public void CancelImportOperation()
        {
            _fileOperationTS.Cancel();
            _fileOperationTS.Dispose();
            IsLoadingFiles = false;
            ProgressMessage = string.Empty;
        }

        public void ImportFiles(IEnumerable<string> files)
        {
            IsLoadingFiles = true;
            foreach (string path in files)
            {
                AddNodesToTree(new FileSystemNode(path));
            }
        }

        public async Task ImportDirectory(string directoryPath)
        {
            IsLoadingFiles = true;
            if (string.IsNullOrWhiteSpace(directoryPath)) { return; }
            List<FileSystemNode> import = new();
            Progress<NodeProgress> progress = new Progress<NodeProgress>(prog =>
            {
                ProgressMessage = prog.Message;
                ProgressPercentage = prog.Percentage;
            });
            _fileOperationTS = new CancellationTokenSource();
            try
            {
                import = await GetNodesFromDirectoryAsync(directoryPath, progress, _fileOperationTS.Token);
            }
            catch (OperationCanceledException)
            {
                IsLoadingFiles = false;
                ProgressMessage = null;
                return;
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to process files for file tree", ex, "FileTree");
                Error = $"Failed to process files: {ex.Message}, {ex.InnerException}";
                return;
            }
            if (import.Count > 0)
            {
                AddNodesToTree(import, true);
            }
        }

        public void AddNodesToTree(IEnumerable<FileSystemNode> nodes, bool dontSort = false)
        {
            if (nodes == null) return;
            try
            {
                IsLoadingFiles = true;
                if (SelectedFileTreeNodes == null)
                {
                    SelectedFileTreeNodes = new();
                }
                if (SelectedFileTreeNodes.Count < 1)
                {
                    if (TreeNodes == null)
                    {
                        throw new Exception("FileTreeViewModel TreeNodes is null, cannot add nodes to the tree. Make sure you create an instance of VM with a valid ObservableCollection<FileSystemNode> tree in the parameters");
                    }
                    SelectedFileTreeNodes.Add(TreeNodes.First());
                }
                if (SelectedFileTreeNodes.Count > 1)
                {
                    List<FileSystemNode>? dirNodes = SelectedFileTreeNodes.Where(n => !n.IsFile).ToList();
                    if (dirNodes != null)
                    {
                        if (dirNodes.Count > 1) return; // More than one directory selected, we dunno which to add to
                        for (int i = SelectedFileTreeNodes.Count - 1; i > 0; i--) // Deselect all but the single DIR
                        {
                            if (SelectedFileTreeNodes[i] != dirNodes.First())
                            {
                                SelectedFileTreeNodes.RemoveAt(i);
                            }
                        }
                    }
                }

                List<FileSystemNode> nodesAdded = new();
                ObservableCollection<FileSystemNode> selectedSubNodes = SelectedFileTreeNodes.First().SubNodes;

                // Add all new nodes first, checking for duplicates
                foreach (var node in nodes)
                {
                    if (selectedSubNodes == null)
                        selectedSubNodes = SelectedFileTreeNodes.First().SubNodes = new();

                    // Compare FullPath to FullPath
                    if (!selectedSubNodes.Any(n => n.FullPath.Equals(node.FullPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        selectedSubNodes.Add(node);
                        nodesAdded.Add(node);
                    }
                }

                // Sort
                if (!dontSort)
                {
                    var orderedList = selectedSubNodes
                        .OrderBy(info => info.IsFile)
                        .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    selectedSubNodes.Clear();

                    foreach (var orderedNode in orderedList)
                        selectedSubNodes.Add(orderedNode);
                }

                // Apply events to track sub node changes
                nodesAdded.ForEach(node =>
                {
                    if (node.SubNodes != null)
                        node.SubNodes.CollectionChanged += ((sender, e) =>
                        {
                            OnPropertyChanged(nameof(TreeNodes));
                        });
                });

            }
            catch (Exception ex)
            {
                AppLog.Error("Error adding nodes to file tree", ex, "FileTree");
                Error = $"Error adding nodes to tree: {ex.Message}, {ex.InnerException}";
            }
            finally
            {
                IsLoadingFiles = false;
            }
        }

        public void AddNodesToTree(FileSystemNode node)
        {
            AddNodesToTree(new List<FileSystemNode> { node });
        }

        public void ClearAllTreeNodes()
        {
            SelectedFileTreeNodes.Clear();
            FileSystemNode newRoot = new FileSystemNode(_treeNodes.First());
            newRoot.SubNodes.Clear();
            _treeNodes.Clear();
            _treeNodes.Add(newRoot);
            SelectedFileTreeNodes.Add(_treeNodes.First());
        }

        [RelayCommand]
        public void DeleteSelected()
        {
            if (SelectedFileTreeNodes.Count > 0)
            {
                foreach (FileSystemNode selectedNode in SelectedFileTreeNodes.ToList())
                {
                    RemoveNodeFromTree(_treeNodes, selectedNode);
                }
                SelectedFileTreeNodes.Clear();
            }
        }

        private void RemoveNodeFromTree(ObservableCollection<FileSystemNode> nodes, FileSystemNode targetNode)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == targetNode)
                {
                    nodes.RemoveAt(i);
                    return;
                }
                if (nodes[i].SubNodes != null)
                {
                    RemoveNodeFromTree(nodes[i].SubNodes, targetNode);
                }
            }
        }

        public void AddNewDir(string newName)
        {
            if (SelectedFileTreeNodes.Count < 1 || SelectedFileTreeNodes.Count > 1 || string.IsNullOrWhiteSpace(newName)) { return; }
            FileSystemNode newDir = new(newName, new ObservableCollection<FileSystemNode>());
            ObservableCollection<FileSystemNode> subNodes = SelectedFileTreeNodes.First().SubNodes;
            if (!subNodes.Any(n => n.Name == newDir.Name))
            {
                // Find the correct index to insert the new directory
                int insertIndex = 0;
                for (int i = 0; i < subNodes.Count; i++)
                {
                    if (subNodes[i].IsFile || string.Compare(subNodes[i].Name, newDir.Name, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        break;
                    }
                    insertIndex++;
                }
                subNodes.Insert(insertIndex, newDir);
            }
        }

        [RelayCommand]
        public void ShowFileInExplorer()
        {
            if (SelectedFileTreeNodes.Count < 1 || SelectedFileTreeNodes.Count > 1) { return; }
            string? path = SelectedFileTreeNodes.First().FullPath;
            if (string.IsNullOrWhiteSpace(path)) { return; }
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }
}
