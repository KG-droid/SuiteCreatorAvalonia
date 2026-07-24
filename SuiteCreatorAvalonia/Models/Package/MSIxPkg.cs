using SuiteCreatorAvalonia.Models.Common;
using System.IO;

namespace SuiteCreatorAvalonia.Models.Package
{
    public partial class MSIxPkg : MSIx
    {
        private string? _msixPath;
        public string? MSIxPath 
        {
            get { return _msixPath; }
            set { 
                _msixPath = value; 
                MSIxFile = Path.GetFileName(value ?? string.Empty);
            }
        }

        protected MSIxPkg(MSIxPkg source) : base(source)
        {
            MSIxPath = source.MSIxPath;
        }

        public MSIxPkg() : base() {}

        public override MSIxPkg Clone()
        {
            return new MSIxPkg(this);
        }

        public override void UpdateFrom(Stage stage)
        {
            base.UpdateFrom(stage);
            if (stage is MSIxPkg msixPkg)
            {
                MSIxPath = msixPkg.MSIxPath;
            }
        }

        public override string? Validate()
        {
            if (!File.Exists(MSIxPath))
            {
                return $"The MSIx: {MSIxPath}, cannot be found.";
            }
            return base.Validate();
        }
    }
}
