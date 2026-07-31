using Material.Icons;
using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Rules
{
    public class ComparatorMapping
    {
        public static Dictionary<RuleType, List<DetectionTypes>> RuleTypeToDetection = new()
        {
            {
                RuleType.Registry, new List<DetectionTypes>
                {
                    DetectionTypes.Exists,
                    DetectionTypes.NotExists,
                    DetectionTypes.String,
                    DetectionTypes.Number,
                    DetectionTypes.StringVersion,
                    DetectionTypes.Binary,
                }
            },
            {
                RuleType.File, new List<DetectionTypes>
                {
                    DetectionTypes.Exists,
                    DetectionTypes.NotExists,
                    DetectionTypes.FileVersion,
                    DetectionTypes.ProductVersion,
                }
            },
            {
                RuleType.MSI, new List<DetectionTypes>
                {
                    DetectionTypes.Version,
                    DetectionTypes.Installed,
                    DetectionTypes.NotInstalled,
                }
            },
            {
                RuleType.MSIx, new List<DetectionTypes>
                {
                    DetectionTypes.Version,
                    DetectionTypes.Installed,
                    DetectionTypes.NotInstalled,
                }
            },
            {
                RuleType.PowerShell, new List<DetectionTypes>
                {
                    DetectionTypes.String,
                    DetectionTypes.Boolean,
                }
            },
        };

        public static Dictionary<DetectionTypes, List<ComparatorPlus>?> DetectionToComparator = new()
        {

            {
                DetectionTypes.Binary, new List<ComparatorPlus>
                {
                    ComparatorPlus.Equal,
                    ComparatorPlus.NotEqual
                }
            },
            {
                DetectionTypes.Boolean, new List<ComparatorPlus>
                {
                    ComparatorPlus.True,
                    ComparatorPlus.False
                }
            },
            {
                DetectionTypes.Exists, null
            },
            {
                DetectionTypes.NotExists, null
            },
            {
                DetectionTypes.Installed, null
            },
            {
                DetectionTypes.NotInstalled, null
            },
            {
                DetectionTypes.FileVersion, new List<ComparatorPlus>
                {
                    ComparatorPlus.Equal,
                    ComparatorPlus.NotEqual,
                    ComparatorPlus.GreaterThan,
                    ComparatorPlus.GreaterThanOrEqual,
                    ComparatorPlus.LessThan,
                    ComparatorPlus.LessThanOrEqual
                }
            },
            {
                DetectionTypes.String, new List<ComparatorPlus>
                {
                    ComparatorPlus.Equal,
                    ComparatorPlus.NotEqual
                }
            },
            {
                DetectionTypes.Number, new List<ComparatorPlus>
                {
                    ComparatorPlus.Equal,
                    ComparatorPlus.NotEqual,
                    ComparatorPlus.GreaterThan,
                    ComparatorPlus.GreaterThanOrEqual,
                    ComparatorPlus.LessThan,
                    ComparatorPlus.LessThanOrEqual
                }
            },
            {   DetectionTypes.ProductVersion, new List<ComparatorPlus>
                {
                    ComparatorPlus.Equal,
                    ComparatorPlus.NotEqual,
                    ComparatorPlus.GreaterThan,
                    ComparatorPlus.GreaterThanOrEqual,
                    ComparatorPlus.LessThan,
                    ComparatorPlus.LessThanOrEqual
                }
            },
            {
                DetectionTypes.Version, new List<ComparatorPlus>
                {
                    ComparatorPlus.Equal,
                    ComparatorPlus.NotEqual,
                    ComparatorPlus.GreaterThan,
                    ComparatorPlus.GreaterThanOrEqual,
                    ComparatorPlus.LessThan,
                    ComparatorPlus.LessThanOrEqual
                }
            },
            {
                DetectionTypes.StringVersion, new List<ComparatorPlus>
                {
                    ComparatorPlus.Equal,
                    ComparatorPlus.NotEqual,
                    ComparatorPlus.GreaterThan,
                    ComparatorPlus.GreaterThanOrEqual,
                    ComparatorPlus.LessThan,
                    ComparatorPlus.LessThanOrEqual
                }
            },
        };

        public static Dictionary<ComparatorPlus, MaterialIconKind> ComparatorToIconKind = new()
        {
            { ComparatorPlus.Equal, MaterialIconKind.CodeEqual },
            { ComparatorPlus.NotEqual, MaterialIconKind.CodeNotEqual },
            { ComparatorPlus.GreaterThan, MaterialIconKind.CodeGreaterThan },
            { ComparatorPlus.GreaterThanOrEqual, MaterialIconKind.CodeGreaterThanOrEqual },
            { ComparatorPlus.LessThan, MaterialIconKind.CodeLessThan },
            { ComparatorPlus.LessThanOrEqual, MaterialIconKind.CodeLessThanOrEqual },
            { ComparatorPlus.True, MaterialIconKind.Check },
            { ComparatorPlus.False, MaterialIconKind.Close }
        };
    }
}
