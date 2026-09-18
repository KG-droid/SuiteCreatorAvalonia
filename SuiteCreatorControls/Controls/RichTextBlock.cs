using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using SuiteCreatorControls.Text;
using System;

namespace SuiteCreatorControls.Controls
{
    /// <summary>
    /// A TextBlock that renders <see cref="RichTextMarkup"/> (the popup message format) as styled inlines.
    /// Anything not covered by a tag inherits the TextBlock's own font size and foreground.
    /// </summary>
    public class RichTextBlock : TextBlock
    {
        public static readonly StyledProperty<string?> MarkupProperty =
            AvaloniaProperty.Register<RichTextBlock, string?>(nameof(Markup));

        // Keep the stock TextBlock theme/styles (foreground etc.) applying to this subclass.
        protected override Type StyleKeyOverride => typeof(TextBlock);

        public string? Markup
        {
            get => GetValue(MarkupProperty);
            set => SetValue(MarkupProperty, value);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == MarkupProperty)
                Rebuild();
        }

        private void Rebuild()
        {
            InlineCollection inlines = Inlines ??= new InlineCollection();
            inlines.Clear();
            foreach (Inline inline in RichTextMarkup.ToInlines(Markup))
                inlines.Add(inline);
        }
    }
}
