using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>
    /// Attached properties that annotate live controls with help content.
    /// Any control with Help.Text (or Help.TourTag / Help.PageSummary) registers itself
    /// with <see cref="HelpService"/> while it is in the visual tree, so help walkthroughs
    /// always spotlight the real, current UI instead of screenshots.
    /// </summary>
    public class Help : AvaloniaObject
    {
        /// <summary>Help body text shown when this control is spotlighted.</summary>
        public static readonly AttachedProperty<string?> TextProperty =
            AvaloniaProperty.RegisterAttached<Help, Control, string?>("Text");

        /// <summary>Optional title for the help callout (falls back to no title).</summary>
        public static readonly AttachedProperty<string?> TitleProperty =
            AvaloniaProperty.RegisterAttached<Help, Control, string?>("Title");

        /// <summary>Explicit ordering for "Help on this page" walkthroughs. Lower first; ties fall back to on-screen position.</summary>
        public static readonly AttachedProperty<int> OrderProperty =
            AvaloniaProperty.RegisterAttached<Help, Control, int>("Order");

        /// <summary>Stable identifier so guided tours can find this control regardless of layout.</summary>
        public static readonly AttachedProperty<string?> TourTagProperty =
            AvaloniaProperty.RegisterAttached<Help, Control, string?>("TourTag");

        /// <summary>Set on a page's root element: a summary shown as the first step of "Help on this page".</summary>
        public static readonly AttachedProperty<string?> PageSummaryProperty =
            AvaloniaProperty.RegisterAttached<Help, Control, string?>("PageSummary");

        private static readonly AttachedProperty<bool> IsHookedProperty =
            AvaloniaProperty.RegisterAttached<Help, Control, bool>("IsHooked");

        static Help()
        {
            TextProperty.Changed.AddClassHandler<Control>(OnAnnotationChanged);
            TourTagProperty.Changed.AddClassHandler<Control>(OnAnnotationChanged);
            PageSummaryProperty.Changed.AddClassHandler<Control>(OnAnnotationChanged);
        }

        public static void SetText(Control element, string? value) => element.SetValue(TextProperty, value);
        public static string? GetText(Control element) => element.GetValue(TextProperty);

        public static void SetTitle(Control element, string? value) => element.SetValue(TitleProperty, value);
        public static string? GetTitle(Control element) => element.GetValue(TitleProperty);

        public static void SetOrder(Control element, int value) => element.SetValue(OrderProperty, value);
        public static int GetOrder(Control element) => element.GetValue(OrderProperty);

        public static void SetTourTag(Control element, string? value) => element.SetValue(TourTagProperty, value);
        public static string? GetTourTag(Control element) => element.GetValue(TourTagProperty);

        public static void SetPageSummary(Control element, string? value) => element.SetValue(PageSummaryProperty, value);
        public static string? GetPageSummary(Control element) => element.GetValue(PageSummaryProperty);

        /// <summary>Convenience for code-built controls (e.g. event cards): sets title + text in one call.</summary>
        public static void Annotate(Control element, string? title, string text)
        {
            SetTitle(element, title);
            SetText(element, text);
        }

        private static void OnAnnotationChanged(Control control, AvaloniaPropertyChangedEventArgs e)
        {
            bool shouldTrack = !string.IsNullOrWhiteSpace(GetText(control))
                || !string.IsNullOrWhiteSpace(GetTourTag(control))
                || !string.IsNullOrWhiteSpace(GetPageSummary(control));
            bool hooked = control.GetValue(IsHookedProperty);

            if (shouldTrack && !hooked)
            {
                control.SetValue(IsHookedProperty, true);
                control.AttachedToVisualTree += OnTargetAttached;
                control.DetachedFromVisualTree += OnTargetDetached;
                if (TopLevel.GetTopLevel(control) != null)
                    HelpService.Instance.Register(control);
            }
            else if (!shouldTrack && hooked)
            {
                control.SetValue(IsHookedProperty, false);
                control.AttachedToVisualTree -= OnTargetAttached;
                control.DetachedFromVisualTree -= OnTargetDetached;
                HelpService.Instance.Unregister(control);
            }
        }

        private static void OnTargetAttached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is Control control)
                HelpService.Instance.Register(control);
        }

        private static void OnTargetDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is Control control)
                HelpService.Instance.Unregister(control);
        }
    }
}
