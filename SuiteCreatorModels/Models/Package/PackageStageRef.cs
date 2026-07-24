using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Package
{
    public class PackageStageRef : Stage
    {
        public override PackageStageRef Clone()
        {
            return new PackageStageRef
            {
                Id = Id,
                Name = Name,
            };
        }
    }
}
