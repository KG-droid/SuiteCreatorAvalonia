namespace SuiteCreatorAvalonia.Models.Rules
{
    public enum RuleType
    {
        Registry,
        File,
        MSI,
        MSIx,
        PowerShell,
        OR,
        GroupOpen,
        GroupClose
    }  

    public abstract class RuleBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public RuleType Type { get; set; }
        public abstract RuleBase Clone();
        public abstract void UpdateFrom(RuleBase other);
    }
}
