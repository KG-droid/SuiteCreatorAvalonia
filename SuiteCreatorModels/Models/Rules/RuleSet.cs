using Newtonsoft.Json;
using SuiteTools;
using System.Collections.ObjectModel;

namespace SuiteCreatorAvalonia.Models.Rules
{
    public class RuleSet
    {
        // Sentinel offered alongside real RuleSets in a Condition picker so a user can explicitly
        // pick "no condition" rather than that being an unreachable/ambiguous ComboBox state. Never
        // stored on a Schedule itself - selecting it is translated back to a real null Condition.
        public static readonly RuleSet None = new RuleSet("(No Condition)") { Id = Guid.Empty };

        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Name { get; set; }
        public ObservableCollection<RuleBase> Rules { get; set; } = new();

        [JsonConstructor]
        public RuleSet() { }

        public RuleSet(string? name)
        {
            Name = name;
        }

        public RuleSet Clone()
        {
            var clonedRules = new ObservableCollection<RuleBase>();
            foreach (var rule in Rules)
            {
                clonedRules.Add(rule.Clone());
            }
            return new RuleSet(Name)
            {
                Id = Id,
                Name = Name,
                Rules = clonedRules
            };
        }

        public void UpdateFrom(RuleSet other)
        {
            if (other == null) return;
            Id = other.Id;
            Name = other.Name;
            Rules.Clear();
            foreach (var rule in other.Rules)
            {
                Rules.Add(rule.Clone());
            }
        }

        public string? Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "RuleSet has no name";
            }
            if (Rules == null || Rules.Count < 1)
            {
                return "RuleSet doesnt have any configured rules";
            }

            for (int i = 0; i < Rules.Count; i++)
            {
                RuleBase currentRuleBase = Rules[i];
                if (currentRuleBase is RuleOperator)
                {
                    if (i != Rules.Count - 1)
                    {
                        RuleBase nextRule = Rules[i + 1];
                        if (nextRule is RuleOperator)
                        {
                            return "A Ruleset cannot have operators (OR/Group) next to each other";
                        }
                    }
                }
                if (currentRuleBase is Rule rule)
                {
                    string? valResult = rule.Validate();
                    if (valResult != null)
                    {
                        return valResult;
                    }
                }
            }
            return null;
        }

        public RuleResult ParseRuleSet()
        {
            RuleResult results = new();

            if (Rules == null || Rules.Count == 0)
            {
                results.Summary = "No rules configured";
                return results;
            }

            List<string> tokens = new();
            List<string> summaries = new();
            bool lastWasValueEnd = false;

            foreach (RuleBase ruleBase in Rules)
            {
                if (ruleBase is RuleOperator rOp)
                {
                    switch (rOp.Type)
                    {
                        case RuleType.OR:
                            tokens.Add("||");
                            lastWasValueEnd = false;
                            break;
                        case RuleType.GroupOpen:
                            if (lastWasValueEnd)
                                tokens.Add("&&");
                            tokens.Add("(");
                            lastWasValueEnd = false;
                            break;
                        case RuleType.GroupClose:
                            tokens.Add(")");
                            lastWasValueEnd = true;
                            break;
                        default:
                            throw new Exception($"Unknown operator type: {rOp.Type}");
                    }
                    continue;
                }

                if (ruleBase is Rule rule)
                {
                    if (lastWasValueEnd)
                        tokens.Add("&&");

                    RuleResult ruleResult = rule.IsMet();
                    summaries.Add(ruleResult.Summary ?? string.Empty);
                    tokens.Add(ruleResult.IsMet ? "true" : "false");
                    lastWasValueEnd = true;
                }
            }

            results.IsMet = EvaluateBoolExpression(tokens);
            results.Summary = string.Join("; ", summaries);
            return results;
        }

        private static bool EvaluateBoolExpression(List<string> tokens)
        {
            int pos = 0;
            return ParseOrExpr(tokens, ref pos);
        }

        private static bool ParseOrExpr(List<string> tokens, ref int pos)
        {
            bool left = ParseAndExpr(tokens, ref pos);
            while (pos < tokens.Count && tokens[pos] == "||")
            {
                pos++;
                bool right = ParseAndExpr(tokens, ref pos);
                left = left || right;
            }
            return left;
        }

        private static bool ParseAndExpr(List<string> tokens, ref int pos)
        {
            bool left = ParseFactorExpr(tokens, ref pos);
            while (pos < tokens.Count && tokens[pos] == "&&")
            {
                pos++;
                bool right = ParseFactorExpr(tokens, ref pos);
                left = left && right;
            }
            return left;
        }

        private static bool ParseFactorExpr(List<string> tokens, ref int pos)
        {
            if (pos >= tokens.Count)
                throw new InvalidOperationException("Unexpected end of boolean expression");

            string token = tokens[pos];
            if (token == "(")
            {
                pos++;
                bool value = ParseOrExpr(tokens, ref pos);
                if (pos >= tokens.Count || tokens[pos] != ")")
                    throw new InvalidOperationException("Missing closing parenthesis in boolean expression");
                pos++;
                return value;
            }
            if (token == "true") { pos++; return true; }
            if (token == "false") { pos++; return false; }
            throw new InvalidOperationException($"Unexpected token in boolean expression: {token}");
        }
    }
}
