using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Services
{
    internal class NodeFileSystem
    {
        long currentFileCount = 0;
        internal class NodeProgress
        {
            internal double Percentage { get; set; }
            internal string? Message { get; set; }
            public NodeProgress(double percentage, string? message)
            {
                Percentage = percentage;
                Message = message;
            }
        }

        internal static async Task<List<FileSystemNode>> GetNodesFromDirectoryAsync(string directoryPath, IProgress<NodeProgress> progressMsg, CancellationToken cancellationToken)
        {
            IEnumerable<string> allFiles = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories);
            int fileCount = allFiles.Count();
            var processedCount = new Progress<double>(count => progressMsg.Report(new NodeProgress((100.0 / fileCount) * count, $"Processed {count} out of {fileCount} files")));
            NodeFileSystem nodeFS = new();
            return await nodeFS.GetNodesFromDirectory(directoryPath, processedCount, cancellationToken);
        }

        private async Task<List<FileSystemNode>> GetNodesFromDirectory(string directoryPath, IProgress<double> processedCount, CancellationToken cancellationToken)
        {
            return await Task.Run(async () =>
            {
                List<FileSystemNode> nodes = new List<FileSystemNode>();
                DirectoryInfo directoryInfo = new DirectoryInfo(directoryPath);
                if (!directoryInfo.Exists) { return nodes; }

                IEnumerable<FileSystemInfo> infos = directoryInfo
                    .GetFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = false })
                    .OrderBy(info => info is not DirectoryInfo) // Directories first
                    .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase); // Alphabetical order A-Z

                foreach (FileSystemInfo fileSystemInfo in infos)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileSystemNode node;
                    if (fileSystemInfo is DirectoryInfo dInfo)
                    {
                        node = new FileSystemNode(fileSystemInfo.FullName, new ObservableCollection<FileSystemNode>());
                        List<FileSystemNode> subnodes = await GetNodesFromDirectory(dInfo.FullName, processedCount, cancellationToken);
                        if (subnodes.Count > 0)
                        {
                            node.SubNodes = new ObservableCollection<FileSystemNode>(subnodes);
                        }
                    }
                    else
                    {
                        node = new FileSystemNode(fileSystemInfo.FullName);
                        currentFileCount++;
                        processedCount.Report(currentFileCount);
                    }
                    nodes.Add(node);
                }
                return nodes;
            }, cancellationToken);
        }
    }
}
