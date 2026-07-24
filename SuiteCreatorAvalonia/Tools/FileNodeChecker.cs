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

        private static bool NodeAndChildrenExist(FileSystemNode node)
        {
            if (node.IsFile)
            {
                if (!File.Exists(node.FullPath))
                    return false;
            }
            else
            {
                if (!Directory.Exists(node.FullPath))
                    return false;
                if (node.SubNodes != null)
                {
                    foreach (var sub in node.SubNodes)
                    {
                        if (!NodeAndChildrenExist(sub))
                            return false;
                    }
                }
            }
            return true;
        }
    }
}
