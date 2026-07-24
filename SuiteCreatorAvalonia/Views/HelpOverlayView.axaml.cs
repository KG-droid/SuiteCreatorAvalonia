using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SuiteCreatorAvalonia.Services;
using System;

namespace SuiteCreatorAvalonia.Views
{
    /// <summary>
    /// Full-window overlay that spotlights live controls for help walkthroughs and tours.
    /// Everything except the target is dimmed (the dim shape has an even-odd hole, so the
    /// spotlighted control itself stays fully interactive). A callout bubble with the help
    /// text is positioned beside the hole and repositions itself when the layout changes.
    /// </summary>
    public partial class HelpOverlayView : UserControl
    {
        private Control? _target;
        private bool _inspectMode;
        private Control? _inspectHover;
        private Rect? _lastHole;
        private Size _lastOverlaySize;

        private const string InspectHint = "Move the pointer to highlight parts of the app, then click a highlighted item to see what it does. Click anywhere else or press Esc to leave.";

        private const double DefaultCalloutWidth = 340;
        private const double InspectHintWidth = 640;

        private static readonly IBrush StepDimBrush = new SolidColorBrush(Color.FromArgb(0xA0, 0, 0, 0));
        private static readonly IBrush InspectDimBrush = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));

        public HelpOverlayView()
        {
            InitializeComponent();
            CloseButton.Click += (_, _) => HelpService.Instance.End();
            BackButton.Click += async (_, _) => await HelpService.Instance.BackAsync();
            NextButton.Click += async (_, _) => await HelpService.Instance.NextAsync();
            SizeChanged += (_, _) => Reposition(force: true);
            KeyDown += OnOverlayKeyDown;
            PointerMoved += OnOverlayPointerMoved;
            PointerPressed += OnOverlayPointerPressed;
        }

        /// <summary>Shows one walkthrough/tour step. A null target shows a centered callout with full dim.</summary>
        public void ShowStep(Control? target, string? title, string text, int index, int total)
        {
            _inspectMode = false;
            _inspectHover = null;
            DimPath.Fill = StepDimBrush;
            CalloutBorder.Width = DefaultCalloutWidth;
            SetTarget(target);
            CalloutTitle.Text = title;
            CalloutTitle.IsVisible = !string.IsNullOrWhiteSpace(title);
            CalloutText.Text = text;
            NavRow.IsVisible = true;
            StepCounter.Text = $"{index + 1} of {total}";
            BackButton.IsEnabled = index > 0;
            NextButton.Content = index + 1 >= total ? "Done" : "Next";
            IsVisible = true;
            Reposition(force: true);
            Focus();
        }

        /// <summary>Enters "What's this?" mode: hovering highlights annotated controls, clicking one shows its help.</summary>
        public void StartInspect()
        {
            _inspectMode = true;
            _inspectHover = null;
            SetTarget(null);
            DimPath.Fill = InspectDimBrush;
            CalloutBorder.Width = InspectHintWidth;
            CalloutTitle.Text = "What's this?";
            CalloutTitle.IsVisible = true;
            CalloutText.Text = InspectHint;
            NavRow.IsVisible = false;
            IsVisible = true;
            Reposition(force: true);
            Focus();
        }

        public void HideOverlay()
        {
            SetTarget(null);
            _inspectMode = false;
            _inspectHover = null;
            IsVisible = false;
        }

        private void SetTarget(Control? target)
        {
            if (_target != null)
                _target.LayoutUpdated -= OnTargetLayoutUpdated;
            _target = target;
            if (_target != null)
                _target.LayoutUpdated += OnTargetLayoutUpdated;
        }

        private void OnTargetLayoutUpdated(object? sender, EventArgs e) => Reposition(force: false);

        private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HelpService.Instance.End();
                e.Handled = true;
            }
        }

        private bool IsOverCallout(Point point)
        {
            Point calloutPos = new Point(Canvas.GetLeft(CalloutBorder), Canvas.GetTop(CalloutBorder));
            return new Rect(calloutPos, CalloutBorder.Bounds.Size).Contains(point);
        }

        private Rect? OverlayRectOf(Control control)
        {
            Point? topLeft = control.TranslatePoint(new Point(0, 0), this);
            if (topLeft == null || control.Bounds.Width <= 0) return null;
            return new Rect(topLeft.Value, control.Bounds.Size).Inflate(4);
        }

        private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
        {
            // Inspect mode: hovering only moves the highlight; help is revealed on click
            if (!_inspectMode) return;
            Point point = e.GetPosition(this);
            if (IsOverCallout(point)) return;

            Control? hit = HelpService.Instance.HitTestHelpTarget(point);
            if (hit == _inspectHover) return;
            _inspectHover = hit;

            Rect? rect = hit == null ? null : OverlayRectOf(hit);
            if (rect != null)
            {
                HighlightBorder.Width = rect.Value.Width;
                HighlightBorder.Height = rect.Value.Height;
                Canvas.SetLeft(HighlightBorder, rect.Value.X);
                Canvas.SetTop(HighlightBorder, rect.Value.Y);
                HighlightBorder.IsVisible = true;
            }
            else
            {
                HighlightBorder.IsVisible = false;
            }
        }

        private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_inspectMode) return;
            Point point = e.GetPosition(this);
            if (IsOverCallout(point)) return;

            Control? hit = HelpService.Instance.HitTestHelpTarget(point);
            if (hit == null)
            {
                // Clicking anywhere that isn't a highlighted control exits inspect mode
                HelpService.Instance.End();
                e.Handled = true;
                return;
            }

            CalloutBorder.Width = DefaultCalloutWidth;
            string? title = Help.GetTitle(hit);
            CalloutTitle.Text = title;
            CalloutTitle.IsVisible = !string.IsNullOrWhiteSpace(title);
            CalloutText.Text = Help.GetText(hit) ?? "";
            Rect? rect = OverlayRectOf(hit);
            if (rect != null)
            {
                CalloutBorder.Measure(new Size(CalloutBorder.Width, double.PositiveInfinity));
                Point position = PlaceCallout(rect.Value, CalloutBorder.DesiredSize, Bounds.Size);
                Canvas.SetLeft(CalloutBorder, position.X);
                Canvas.SetTop(CalloutBorder, position.Y);
            }
            e.Handled = true;
        }

        private void Reposition(bool force)
        {
            if (!IsVisible) return;
            Size overlaySize = Bounds.Size;
            if (overlaySize.Width <= 0 || overlaySize.Height <= 0) return;

            Rect? hole = null;
            if (_target != null && TopLevel.GetTopLevel(_target) == TopLevel.GetTopLevel(this))
            {
                Point? topLeft = _target.TranslatePoint(new Point(0, 0), this);
                if (topLeft != null && _target.Bounds.Width > 0)
                    hole = new Rect(topLeft.Value, _target.Bounds.Size).Inflate(4);
            }

            // Skip rebuilding geometry when nothing moved (LayoutUpdated fires on every pass)
            if (!force && hole == _lastHole && overlaySize == _lastOverlaySize) return;
            _lastHole = hole;
            _lastOverlaySize = overlaySize;

            GeometryGroup dimGeometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
            dimGeometry.Children.Add(new RectangleGeometry(new Rect(overlaySize)));
            if (hole != null)
                dimGeometry.Children.Add(new RectangleGeometry(hole.Value));
            DimPath.Data = dimGeometry;

            if (hole != null)
            {
                HighlightBorder.Width = hole.Value.Width;
                HighlightBorder.Height = hole.Value.Height;
                Canvas.SetLeft(HighlightBorder, hole.Value.X);
                Canvas.SetTop(HighlightBorder, hole.Value.Y);
                HighlightBorder.IsVisible = true;
            }
            else
            {
                HighlightBorder.IsVisible = false;
            }

            CalloutBorder.Measure(new Size(CalloutBorder.Width, double.PositiveInfinity));
            Size calloutSize = CalloutBorder.DesiredSize;
            Point position;
            if (hole == null)
            {
                // The inspect hint banner sits flush against the top of the window so it never
                // covers the controls the user is trying to point at.
                double centeredX = Math.Clamp((overlaySize.Width - calloutSize.Width) / 2, 8, Math.Max(8, overlaySize.Width - calloutSize.Width - 8));
                position = _inspectMode
                    ? new Point(centeredX, 8)
                    : new Point(centeredX, (overlaySize.Height - calloutSize.Height) / 2);
            }
            else
            {
                position = PlaceCallout(hole.Value, calloutSize, overlaySize);
            }
            Canvas.SetLeft(CalloutBorder, position.X);
            Canvas.SetTop(CalloutBorder, position.Y);
        }

        /// <summary>Places the callout below the hole if it fits, then above, right, left, else centered.</summary>
        private static Point PlaceCallout(Rect hole, Size callout, Size area)
        {
            const double gap = 12;
            const double margin = 8;
            double x = Math.Clamp(hole.X, margin, Math.Max(margin, area.Width - callout.Width - margin));
            if (hole.Bottom + gap + callout.Height + margin <= area.Height)
                return new Point(x, hole.Bottom + gap);
            if (hole.Top - gap - callout.Height >= margin)
                return new Point(x, hole.Top - gap - callout.Height);
            double y = Math.Clamp(hole.Y, margin, Math.Max(margin, area.Height - callout.Height - margin));
            if (hole.Right + gap + callout.Width + margin <= area.Width)
                return new Point(hole.Right + gap, y);
            if (hole.Left - gap - callout.Width >= margin)
                return new Point(hole.Left - gap - callout.Width, y);
            return new Point((area.Width - callout.Width) / 2, (area.Height - callout.Height) / 2);
        }
    }
}
