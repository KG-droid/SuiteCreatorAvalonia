namespace SuiteCreatorAvalonia.Models.Rules
{
    public class RuleOperator : RuleBase
    {
        public override RuleBase Clone()
        {
            return new RuleOperator
            {
                Id = Id,
                Type = Type,
            };
        }

        public override void UpdateFrom(RuleBase rulebase)
        {
            if (rulebase is not RuleOperator ruleOp) return;
            {
                Id = ruleOp.Id;
                Type = ruleOp.Type;
            }
        }
    }
}
