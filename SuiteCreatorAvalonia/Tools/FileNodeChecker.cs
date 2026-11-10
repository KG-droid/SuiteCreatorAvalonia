using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using System.Collections.Generic;
using System.IO;

namespace SuiteCreatorAvalonia.Tools
{
    internal class FileNodeChecker
    {
        public static bool AllTreeNodeFilesExist(IEnumerable<FileSystemNode> treeNodes)
        {
            if (treeNodes == null)
                return true;
            foreach (var node in treeNodes)
            {
                if (!NodeAndChildrenExist(node))
                    return false;
            }
            return true;
        }

        // Folders in the tree can be purely organizational (e.g. added via "New Folder" with no
        // backing path on disk), so only files are checked for existence - folders are just recursed into.
        private static bool NodeAndChildrenExist(FileSystemNode node)
        {
            if (node.IsFile)
            {
                if (!File.Exists(node.FullPath))
                    return false;
            }
            else if (node.SubNodes != null)
            {
                foreach (var sub in node.SubNodes)
                {
                    if (!NodeAndChildrenExist(sub))
                        return false;
                }
            }
            return true;
        }

        public static List<string> GetMissingPaths(IEnumerable<FileSystemNode>? treeNodes)
        {
            List<string> missing = new();
            if (treeNodes == null)
                return missing;
            foreach (var node in treeNodes)
            {
                CollectMissingPaths(node, missing);
            }
            return missing;
        }

        private static void CollectMissingPaths(FileSystemNode node, List<string> missing)
        {
            if (node.IsFile)
            {
                if (!File.Exists(node.FullPath))
                    missing.Add(node.FullPath);
            }
            else if (node.SubNodes != null)
            {
                foreach (var sub in node.SubNodes)
                {
                    CollectMissingPaths(sub, missing);
                }
            }
        }
    }
}
