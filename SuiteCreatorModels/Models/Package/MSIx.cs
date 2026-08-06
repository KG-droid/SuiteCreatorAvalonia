using SuiteCreatorAvalonia.Models.Common;

namespace SuiteCreatorAvalonia.Models.Package
{
    public class MSIx : PackageBase
    {
        public string? MSIxFile { get; set; }
        public bool IsForceCloseFamily { get; set; }
        public bool IsForceThisVersion { get; set; }
        public bool IsDeferInUse { get; set; }
        public bool HasDependents { get; set; }
        public List<string>? Dependents { get; set; }
        public bool RemoveOnSuiteRemoval { get; set; } = true;
        public MSIx? Rollback { get; set; }

        protected MSIx(MSIx source)
        {
            Id = source.Id;
            Name = source.Name;
            MSIxFile = source.MSIxFile;
            IsForceCloseFamily = source.IsForceCloseFamily;
            IsForceThisVersion = source.IsForceThisVersion;
            IsDeferInUse = source.IsDeferInUse;
            HasDependents = source.HasDependents;
            Dependents = source.Dependents != null ? new(source.Dependents) : null;
            RemoveOnSuiteRemoval = source.RemoveOnSuiteRemoval;
            Rollback = source.Rollback?.Clone();
            RequirementRuleSetId = source.RequirementRuleSetId;
        }

        public MSIx() : base() { }

        public override MSIx Clone()
        {
            return new MSIx(this);
        }

        public override void UpdateFrom(Stage stage)
        {
            if (stage == null) return;
            if (stage is MSIx msix)
            {
                Id = msix.Id;
                Name = msix.Name;
                MSIxFile = msix.MSIxFile;
                IsForceCloseFamily = msix.IsForceCloseFamily;
                IsForceThisVersion = msix.IsForceThisVersion;
                IsDeferInUse = msix.IsDeferInUse;
                HasDependents = msix.HasDependents;
                Dependents = msix.Dependents != null ? new(msix.Dependents) : null;
                RemoveOnSuiteRemoval = msix.RemoveOnSuiteRemoval;
                Rollback = msix.Rollback?.Clone();
                RequirementRuleSetId = msix.RequirementRuleSetId;
            }
        }

        public override string? Validate()
        {
            if (string.IsNullOrWhiteSpace(MSIxFile))
            {
                return "An MSIx File is required.";
            }
            if (Dependents != null)
            {
                foreach (string dependent in Dependents)
                {
                    if (!File.Exists(dependent))
                    {
                        return "All dependents must have a valid name.";
                    }
                }
            }
            if (Rollback != null && !string.IsNullOrWhiteSpace(Rollback.MSIxFile))
            {
                var rollbackValidation = Rollback.Validate();
                if (rollbackValidation != null)
                {
                    return "Package Rollback: " + rollbackValidation;
                }
            }
            return null;
        }
    }
}
