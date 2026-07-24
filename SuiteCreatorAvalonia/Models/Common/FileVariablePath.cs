using SuiteCreatorAvalonia.Models.Common.TreeNodes;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class FileVar : VariableText
    {
        public FileSystemNode Node { get; set; }

        public FileVar(FileSystemNode node)
        {
            Node = node;
        }

        public override string? GetValue()
        {
            return Node.FullPath;
        }

        public override VariableText Clone()
        {
            return new FileVar(Node); // Assumes FileSystemNode is immutable or copying is unnecessary
        }

        public override void UpdateFrom(VariableText other)
        {
            if (other is FileVar fv)
            {
                Node = fv.Node;
            }
        }
    }
}
