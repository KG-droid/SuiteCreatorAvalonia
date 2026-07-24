using Avalonia.Markup.Xaml;
using System;

namespace SuiteCreatorAvalonia.Helpers
{
    public class EnumValuesExtension : MarkupExtension
    {
        public Type EnumType { get; set; }

        public EnumValuesExtension() { }

        public EnumValuesExtension(Type enumType)
        {
            EnumType = enumType;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return Enum.GetValues(EnumType);
        }
    }
}
