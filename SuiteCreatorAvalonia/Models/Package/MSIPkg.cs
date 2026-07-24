using SuiteCreatorAvalonia.Models.Common;
using System.IO;
using System.Linq;

namespace SuiteCreatorAvalonia.Models.Package
{
    public partial class MSIPkg : MSI
    {
        public string? MSIPath { get; set; }
        public string? TransformsPath { get; set; }
        public string? PatchPath { get; set; }
        public bool IncludeAdjacentFiles { get; set; }

        protected MSIPkg(MSIPkg source) : base(source)
        {
            MSIPath = source.MSIPath;
            TransformsPath = source.TransformsPath;
            PatchPath = source.PatchPath;
            IncludeAdjacentFiles = source.IncludeAdjacentFiles;
        }

        public MSIPkg() : base() {}

        public override MSIPkg Clone()
        {
            return new MSIPkg(this);
        }

        public override void UpdateFrom(Stage stage)
        {
            base.UpdateFrom(stage); 
            if (stage is MSIPkg msi)
            {
                MSIPath = msi.MSIPath;
                TransformsPath = msi.TransformsPath;
                PatchPath = msi.PatchPath;
                IncludeAdjacentFiles = msi.IncludeAdjacentFiles;
            }
        }

        public override string? Validate()
        {
            if (string.IsNullOrWhiteSpace(MSIPath))
            {
                return "An MSI Path is required.";
            }
            if (!string.IsNullOrWhiteSpace(TransformsPath) && !File.Exists(TransformsPath))
            {
                return "Specified Transforms does not exist.";
            }
            if (!File.Exists(MSIPath))
            {
                return "The specified MSI file does not exist.";
            }
            if (!string.IsNullOrWhiteSpace(PatchPath) && !Directory.Exists(PatchPath))
            {
                return "Patch Path directory does not exist.";
            }
            return base.Validate();
        }
    }
}
