using Avalonia.Markup.Xaml;
using System;
using System.Linq;

namespace SuiteCreatorAvalonia.Helpers
{
    public class RecordValuesExtension : MarkupExtension
    {
        public Type RecordType { get; set; }

        public RecordValuesExtension() { }

        public RecordValuesExtension(Type recordType)
        {
            RecordType = recordType;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (RecordType == null)
                throw new InvalidOperationException("RecordType must be set.");

            // Get all public static properties of the type
            var values = RecordType
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(p => p.PropertyType == RecordType)
                .Select(p => p.GetValue(null))
                .ToArray();

            // If no properties found, try public static fields
            if (values.Length == 0)
            {
                values = RecordType
                    .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(f => f.FieldType == RecordType)
                    .Select(f => f.GetValue(null))
                    .ToArray();
            }

            return values;
        }
    }
}
