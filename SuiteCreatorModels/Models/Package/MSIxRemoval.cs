using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Package
{
    public class MSIxRemoval : PackageBase
    {
        public bool IsSpecificVersionRemoval { get; set; }
        public string? PFN { get; set; }

        public override MSIxRemoval Clone()
        {
            return new MSIxRemoval
            {
                Id = Id,
                Name = Name,
                IsSpecificVersionRemoval = IsSpecificVersionRemoval,
                PFN = PFN,
                RequirementRuleSetId = RequirementRuleSetId,
            };
        }

        public override void UpdateFrom(Stage stage)
        {
            if (stage == null) return;
            if (stage is MSIxRemoval msixrem)
            {
                Id = msixrem.Id;
                Name = msixrem.Name;
                IsSpecificVersionRemoval = msixrem.IsSpecificVersionRemoval;
                PFN = msixrem.PFN;
                RequirementRuleSetId = msixrem.RequirementRuleSetId;
            }
        }

        public override string? Validate()
        {
            if (string.IsNullOrWhiteSpace(PFN))
            {
                return "A Package Family Name, or Package Full Name must be provided";
            }
            return null;
        }
    }
}