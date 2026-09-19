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

        private bool _rebuilding;

        public RichTextBlock()
        {
            ActualThemeVariantChanged += (_, _) => Rebuild();
        }

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
            if (_rebuilding)
                return;

            if (change.Property == MarkupProperty)
            {
                Rebuild();
            }
            else if (change.Property == TextProperty && Inlines is not { Count: > 0 } && !string.IsNullOrEmpty(Markup))
            {
                // TextBlock throws away its inlines whenever its own Text changes, so put the markup back.
                Rebuild();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (Inlines is not { Count: > 0 })
                Rebuild();
        }

        private void Rebuild()
        {
            if (_rebuilding)
                return;

            _rebuilding = true;
            try
            {
                if (Text is not null)
                    SetCurrentValue(TextProperty, null);

                InlineCollection inlines = Inlines ??= new InlineCollection();
                inlines.Clear();
                foreach (Inline inline in RichTextMarkup.ToInlines(Markup))
                    inlines.Add(inline);
            }
            finally
            {
                _rebuilding = false;
            }

            InvalidateMeasure();
        }
    }
}
