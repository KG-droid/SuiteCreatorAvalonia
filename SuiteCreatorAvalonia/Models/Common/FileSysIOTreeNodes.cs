using Material.Icons;
using Newtonsoft.Json;
using SuiteCreatorAvalonia.Tools;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteCreatorAvalonia.Models.Common.TreeNodes
{
    public class FileSystemNode
    {
        public ObservableCollection<FileSystemNode>? SubNodes { get; set; }
        public string? Name
        {
            get
            {
                var fileName = Path.GetFileName(FullPath);
                return fileName;
            }
        }
        public string FullPath { get; set; }
        internal Bitmap? Icon
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FullPath))
                {
                    return null;
                }
                else
                {
                    return WindowsShell.GetIconFromShell(FullPath, IsFile);
                }
            }
        }
        internal MaterialIconKind? IconKind
        {
            get
            {
                if (Icon == null)
                    return IsFile ? MaterialIconKind.File : MaterialIconKind.Folder;
                else
                    return null;
            }
        }
        public bool IsFile { get; private set; }

        public FileSystemNode()
        {

        }

        // Clone constructor
        public FileSystemNode(FileSystemNode node)
        {
            FullPath = node.FullPath;
            IsFile = node.IsFile;
            SubNodes = node.SubNodes;
        }

        // File constructor
        public FileSystemNode(string fullPath)
        {
            FullPath = fullPath;
            IsFile = true;
            SubNodes = null;
        }

        // Folder constructor
        public FileSystemNode(string fullPath, ObservableCollection<FileSystemNode> subNodes)
        {
            FullPath = fullPath;
            SubNodes = subNodes;
            IsFile = false;
        }

        // Json constructor
        [JsonConstructor]
        public FileSystemNode(ObservableCollection<FileSystemNode>? subNodes, string fullPath, bool isFile)
        {
            FullPath = fullPath;
            SubNodes = subNodes;
            IsFile = isFile;
        }

        public FileSystemNode Clone()
        {
            var clonedNode = new FileSystemNode
            {
                FullPath = FullPath,
                IsFile = IsFile,
                SubNodes = SubNodes != null
                    ? new ObservableCollection<FileSystemNode>(SubNodes.Select(n => n.Clone()))
                    : null
            };
            return clonedNode;
        }

        public void UpdateFrom(FileSystemNode node)
        {
            if (node == null) return;
            FullPath = node.FullPath;
            IsFile = node.IsFile;
            SubNodes = node.SubNodes != null
                    ? new ObservableCollection<FileSystemNode>(SubNodes.Select(n => n.Clone()))
                    : null;
        }
    }
}