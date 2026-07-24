using SuiteCreatorModels.Enums;

namespace SuiteCreatorAvalonia.Models.Common
{
    public abstract class VariableText
    {
        public abstract VariableText Clone();

        public virtual void UpdateFrom(VariableText other)
        {
            // Base does nothing; overridden in derived classes
        }

        public virtual string? GetValue()
        {
            // This method should be overridden in derived classes to return the actual value
            throw new NotImplementedException("GetValue must be implemented in derived classes.");
        }
    }

    public partial class LiteralText : VariableText
    {
        public string Value { get; set; }

        public LiteralText(string value)
        {
            Value = value;
        }

        public override VariableText Clone()
        {
            return new LiteralText(Value);
        }

        public override void UpdateFrom(VariableText varBase)
        {
            if (varBase is LiteralText cmd)
            {
                Value = cmd.Value;
            }
        }
        public override string? GetValue()
        {
            return string.IsNullOrWhiteSpace(Value) ? null : Value;
        }
    }

    public partial class RelativeFileVar : VariableText
    {
        public string? RelativePath { get; set; }

        public override VariableText Clone()
        {
            return new RelativeFileVar(RelativePath);
        }

        public override void UpdateFrom(VariableText varBase)
        {
            if (varBase is RelativeFileVar relativeVar)
            {
                RelativePath = relativeVar.RelativePath;
            }
        }

        public RelativeFileVar(string? relativePath)
        {
            RelativePath = relativePath;
        }

        public override string? GetValue()
        {
            return string.IsNullOrWhiteSpace(RelativePath) ? null : RelativePath;
        }
    }

    public class SpecialDIR : VariableText
    {
        public SpecialFolderVar Value { get; set; }

        public SpecialDIR(SpecialFolderVar value)
        {
            Value = value;
        }

        public override VariableText Clone()
        {
            return new SpecialDIR(Value);
        }

        public override void UpdateFrom(VariableText varBase)
        {
            if (varBase is SpecialDIR sd)
            {
                Value = sd.Value;
            }
        }

        public override string? GetValue()
        {
            // Cast to System.Environment.SpecialFolder
            return Environment.GetFolderPath((Environment.SpecialFolder)Value);
        }
    }
}
