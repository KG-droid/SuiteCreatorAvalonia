using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Package
{
    public abstract class PackageBase : Stage
    {
        public Guid? RequirementRuleSetId { get; set; }
        public override abstract PackageBase Clone();
        public override abstract void UpdateFrom(Stage other);
        public virtual string? Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "A name is required.";
            }
            return null;
        }
    }
}
