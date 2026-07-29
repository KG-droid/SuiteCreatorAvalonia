using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System;
using System.Diagnostics;
using System.Globalization;

namespace SuiteCreatorControls.Controls
{
    /// <summary>
    /// A custom-drawn "always alive" progress bar: a flowing accent gradient, a pulsing glow at
    /// the leading edge, and subtle particles drifting towards the head. Progress changes (and
    /// the optional percentage label)
    /// animate smoothly rather than jumping, and reaching 100% cross-fades the whole bar to
    /// <see cref="CompleteColor"/> with a soft pulse radiating out. It's drawn per-frame in
    /// <see cref="Render"/> because these layered gradient/glow effects aren't expressible with
    /// XAML Style.Animations.
    /// </summary>
    public class ShimmerProgress : Control
    {
        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<ShimmerProgress, double>(nameof(Value));

        public static readonly StyledProperty<bool> IsIndeterminateProperty =
            AvaloniaProperty.Register<ShimmerProgress, bool>(nameof(IsIndeterminate));

        public static readonly StyledProperty<IBrush?> TrackBrushProperty =
            AvaloniaProperty.Register<ShimmerProgress, IBrush?>(nameof(TrackBrush));

        public static readonly StyledProperty<IBrush?> AccentProperty =
            AvaloniaProperty.Register<ShimmerProgress, IBrush?>(nameof(Accent));

        public static readonly StyledProperty<Color> CompleteColorProperty =
            AvaloniaProperty.Register<ShimmerProgress, Color>(nameof(CompleteColor), Color.FromRgb(0x3F, 0xD3, 0x5E));

        public static readonly StyledProperty<bool> ShowPercentageProperty =
            AvaloniaProperty.Register<ShimmerProgress, bool>(nameof(ShowPercentage), true);

        /// <summary>Progress from 0 to 100. Changes ease towards the new value over ~250ms.</summary>
        public double Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public bool IsIndeterminate
        {
            get => GetValue(IsIndeterminateProperty);
            set => SetValue(IsIndeterminateProperty, value);
        }

        public IBrush? TrackBrush
        {
            get => GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        /// <summary>
        /// The single live accent colour every layer's shades are derived from (darker tail, brighter head,
        /// glow, particles), so the whole effect always tracks the colour the user picked in settings
        /// (see ThemeManager.SetAccentColor) — bind to a theme resource such as AccentVariant2.
        /// </summary>
        public IBrush? Accent
        {
            get => GetValue(AccentProperty);
            set => SetValue(AccentProperty, value);
        }

        /// <summary>Colour the bar cross-fades to when it reaches 100%.</summary>
        public Color CompleteColor
        {
            get => GetValue(CompleteColorProperty);
            set => SetValue(CompleteColorProperty, value);
        }

        /// <summary>Draws the animated percentage centred in the fill (hidden automatically when the bar is too small).</summary>
        public bool ShowPercentage
        {
            get => GetValue(ShowPercentageProperty);
            set => SetValue(ShowPercentageProperty, value);
        }

        // Tuning constants — durations in seconds, sizes in control-space pixels (or fractions of bar height).
        private const double ValueEaseSeconds = 0.25;
        private const double FlowWaveLength = 90; // px per repeating brightness wave in the flowing gradient
        private const double FlowSpeed = 55; // px/s the flow pattern slides left-to-right
        private const int FlowStopsPerWave = 8;
        private const double HeadPulseSeconds = 1.4;
        private const int ParticleCount = 14;
        private const double CompleteFadeSeconds = 0.35;
        private const double CompletePulseSeconds = 0.9;
        private const double IndeterminatePeriodSeconds = 1.8;

        private readonly Stopwatch _stopwatch = new();
        private bool _isAttached;

        // Smooth value animation state: the displayed value chases Value with a 250ms ease-out.
        private double _displayValue;
        private double _easeFromValue;
        private double _easeTargetValue;
        private double _easeStartTime;

        private bool _isComplete;
        private double _completeStartTime;

        static ShimmerProgress()
        {
            AffectsRender<ShimmerProgress>(ValueProperty, IsIndeterminateProperty, TrackBrushProperty,
                AccentProperty, CompleteColorProperty, ShowPercentageProperty);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _stopwatch.Restart();
            _displayValue = Math.Clamp(Value, 0, 100);
            _easeFromValue = _displayValue;
            _easeTargetValue = _displayValue;
            _easeStartTime = 0;
            _isAttached = true;
            RequestNextFrame();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _isAttached = false;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>
        /// Drives the continuous animation off the compositor's own frame callback rather than a
        /// <see cref="Avalonia.Threading.DispatcherTimer"/>. A plain OS timer object is subject to Windows'
        /// timer coalescing for background/non-foreground processes (observed collapsing a 16ms interval
        /// down to ~170ms — an ~11x hit — in a standalone popup process that's never the focused window),
        /// while the render-loop callback tracks actual frame presentation and is far less affected.
        /// </summary>
        private void RequestNextFrame()
        {
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
        }

        private void OnFrame(TimeSpan _)
        {
            if (!_isAttached)
            {
                return;
            }
            InvalidateVisual();
            RequestNextFrame();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ValueProperty)
            {
                _easeFromValue = _displayValue;
                _easeTargetValue = Math.Clamp(change.GetNewValue<double>(), 0, 100);
                _easeStartTime = _stopwatch.Elapsed.TotalSeconds;
            }
        }

        public override void Render(DrawingContext context)
        {
            double width = Bounds.Width;
            double height = Bounds.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            double time = _stopwatch.Elapsed.TotalSeconds;
            double cornerRadius = height / 2.0;
            var trackRect = new RoundedRect(new Rect(0, 0, width, height), cornerRadius);

            UpdateDisplayValue(time);

            // Completion cross-fade: 0 while running, easing to 1 shortly after hitting 100%.
            bool complete = !IsIndeterminate && _displayValue >= 99.95;
            if (complete && !_isComplete)
            {
                _completeStartTime = time;
            }
            _isComplete = complete;
            double completeBlend = complete
                ? Math.Clamp((time - _completeStartTime) / CompleteFadeSeconds, 0, 1)
                : 0;

            Color accent = (Accent as ISolidColorBrush)?.Color ?? Colors.DodgerBlue;
            accent = LerpColor(accent, CompleteColor, completeBlend);
            Color tail = AdjustLightness(accent, -0.14); // darker left end of the gradient
            Color head = AdjustLightness(accent, 0.16); // brighter towards the leading edge
            Color glow = AdjustLightness(accent, 0.28); // brightest shade, used for glows/particles

            context.DrawRectangle(TrackBrush ?? Brushes.Transparent, null, trackRect);
            context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)), 1), trackRect);

            double fillWidth;
            double fillStart = 0;
            if (IsIndeterminate)
            {
                // A sweeping segment stands in for the fill so all the same layers still animate.
                double segmentWidth = Math.Max(width * 0.3, height * 2);
                double sweepT = (time % IndeterminatePeriodSeconds) / IndeterminatePeriodSeconds;
                fillStart = -segmentWidth + sweepT * (width + segmentWidth);
                fillWidth = segmentWidth;
            }
            else
            {
                fillWidth = _displayValue / 100.0 * width;
                if (fillWidth > 0)
                {
                    fillWidth = Math.Max(fillWidth, height); // keep the fill a full pill even at low values
                }
            }

            if (fillWidth <= 0)
            {
                return;
            }

            var fillRect = new Rect(fillStart, 0, fillWidth, height);
            using (context.PushClip(trackRect))
            using (context.PushClip(new RoundedRect(fillRect, cornerRadius)))
            {
                DrawFlowingGradient(context, fillRect, time, tail, accent, head);
                DrawGloss(context, fillRect);
                // Indeterminate's segment width is constant frame-to-frame, so it's a safe wrap length;
                // determinate's fillWidth is mid-ease for ~250ms after every Value change, so the stable
                // track width is used there instead (see DrawParticles).
                double particleWrapWidth = IsIndeterminate ? fillWidth : width;
                DrawParticles(context, fillRect, time, glow, particleWrapWidth);
            }

            // Glowing head at the leading edge, clipped to the track so it spills softly into the
            // unfilled part. Fades out as the completion state takes over.
            double headOpacity = 1 - completeBlend;
            if (headOpacity > 0)
            {
                using (context.PushClip(trackRect))
                using (context.PushOpacity(headOpacity))
                {
                    DrawHead(context, new Point(fillStart + fillWidth - cornerRadius, height / 2.0), height, time, glow);
                }
            }

            if (!IsIndeterminate && ShowPercentage && height >= 14)
            {
                DrawPercentage(context, fillWidth, height);
            }

            // Single soft pulse radiating out when the bar completes.
            if (complete)
            {
                double pulseAge = time - _completeStartTime;
                if (pulseAge < CompletePulseSeconds)
                {
                    double pulseT = pulseAge / CompletePulseSeconds;
                    double inflate = 2 + 16 * EaseOutCubic(pulseT);
                    byte alpha = (byte)((1 - pulseT) * 110);
                    var pulseRect = new Rect(-inflate, -inflate, width + inflate * 2, height + inflate * 2);
                    var pulsePen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, glow.R, glow.G, glow.B)), 2);
                    context.DrawRectangle(null, pulsePen, new RoundedRect(pulseRect, cornerRadius + inflate));
                }
            }
        }

        /// <summary>Eases the displayed value towards the bound Value over <see cref="ValueEaseSeconds"/>.</summary>
        private void UpdateDisplayValue(double time)
        {
            double t = Math.Clamp((time - _easeStartTime) / ValueEaseSeconds, 0, 1);
            _displayValue = _easeFromValue + (_easeTargetValue - _easeFromValue) * EaseOutCubic(t);
        }

        /// <summary>
        /// The base fill: dark tail to bright head, with a train of soft brightness waves baked into a
        /// wide gradient rect that slides right continuously — the "flowing" part of the effect.
        /// </summary>
        private static void DrawFlowingGradient(DrawingContext context, Rect fillRect, double time, Color tail, Color mid, Color head)
        {
            var baseBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(tail, 0),
                    new GradientStop(mid, 0.6),
                    new GradientStop(head, 1),
                },
            };
            context.FillRectangle(baseBrush, fillRect);

            // Sliding brightness waves: periodic, so sliding by one wavelength wraps seamlessly.
            int waveRepeats = (int)Math.Ceiling((fillRect.Width + FlowWaveLength) / FlowWaveLength) + 1;
            double patternWidth = waveRepeats * FlowWaveLength;
            double offset = (time * FlowSpeed) % FlowWaveLength;
            double rectX = fillRect.X + offset - FlowWaveLength;

            int totalStops = waveRepeats * FlowStopsPerWave;
            var flowStops = new GradientStops();
            for (int stopIndex = 0; stopIndex <= totalStops; stopIndex++)
            {
                double positionPx = stopIndex * (FlowWaveLength / FlowStopsPerWave);
                double waveT = positionPx % FlowWaveLength / FlowWaveLength;
                double intensity = 0.5 + 0.5 * Math.Sin(waveT * Math.PI * 2);
                byte alpha = (byte)(intensity * 46);
                flowStops.Add(new GradientStop(Color.FromArgb(alpha, head.R, head.G, head.B), positionPx / patternWidth));
            }

            var flowBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = flowStops,
            };
            context.FillRectangle(flowBrush, new Rect(rectX, fillRect.Y, patternWidth, fillRect.Height));
        }

        /// <summary>A faint white sheen over the top half so the fill reads as a lit, rounded surface.</summary>
        private static void DrawGloss(DrawingContext context, Rect fillRect)
        {
            var glossBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(40, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55),
                },
            };
            context.FillRectangle(glossBrush, fillRect);
        }

        /// <summary>
        /// Small twinkling dots drifting towards the head, seeded deterministically per index. The wrap
        /// length is the stable control width rather than the (possibly still-easing) fill width — using
        /// an animating divisor in the modulo below would make particles jump backward every frame the
        /// fill width was mid-ease, reading as the drift reversing whenever Value changes rapidly.
        /// </summary>
        private static void DrawParticles(DrawingContext context, Rect fillRect, double time, Color glow, double trackWidth)
        {
            double travel = trackWidth + 20;
            for (int i = 0; i < ParticleCount; i++)
            {
                double y = fillRect.Y + fillRect.Height * (0.2 + 0.6 * Rand(i, 1));
                double speed = 22 + 46 * Rand(i, 2);
                double size = 0.8 + 1.5 * Rand(i, 3);
                double x = fillRect.X - 10 + (Rand(i, 4) * 1000 + time * speed) % travel;

                double twinkle = 0.5 + 0.5 * Math.Sin(time * (1.5 + Rand(i, 5) * 2) + i * 2.1);
                byte alpha = (byte)((0.12 + 0.3 * twinkle) * 255);
                Color color = i % 3 == 0 ? Colors.White : glow;
                var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
                context.DrawEllipse(brush, null, new Point(x, y), size, size);
            }
        }

        /// <summary>The pulsing glow at the leading edge of the fill.</summary>
        private static void DrawHead(DrawingContext context, Point center, double height, double time, Color glow)
        {
            double pulse = 0.88 + 0.12 * Math.Sin(time * Math.PI * 2 / HeadPulseSeconds);

            double outerRadius = height * 1.5 * pulse;
            var outerBrush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(150 * pulse), glow.R, glow.G, glow.B), 0),
                    new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1),
                },
            };
            context.DrawEllipse(outerBrush, null, center, outerRadius, outerRadius);
        }

        /// <summary>The animated percentage, centred in the fill when there's room for it.</summary>
        private void DrawPercentage(DrawingContext context, double fillWidth, double height)
        {
            var typeface = new Typeface(TextElement.GetFontFamily(this) ?? FontFamily.Default,
                FontStyle.Normal, FontWeight.SemiBold);
            double fontSize = Math.Clamp(height * 0.45, 10, 20);
            var text = new FormattedText($"{Math.Round(_displayValue)}%", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, fontSize, Brushes.White);

            // Needs to fit inside the fill with clearance for the rounded ends and the head orb.
            if (text.Width + height * 1.5 > fillWidth)
            {
                return;
            }
            context.DrawText(text, new Point((fillWidth - text.Width) / 2.0, (height - text.Height) / 2.0));
        }

        /// <summary>Deterministic pseudo-random in [0,1) from an index and salt, stable across frames.</summary>
        private static double Rand(int index, int salt)
        {
            double value = Math.Sin(index * 12.9898 + salt * 78.233) * 43758.5453;
            return value - Math.Floor(value);
        }

        private static double EaseOutCubic(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return 1 - Math.Pow(1 - t, 3);
        }

        private static Color AdjustLightness(Color color, double delta)
        {
            HslColor hsl = color.ToHsl();
            double newLightness = Math.Clamp(hsl.L + delta, 0, 1);
            return new HslColor(hsl.A, hsl.H, hsl.S, newLightness).ToRgb();
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte A = (byte)(a.A + (b.A - a.A) * t);
            byte R = (byte)(a.R + (b.R - a.R) * t);
            byte G = (byte)(a.G + (b.G - a.G) * t);
            byte B = (byte)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(A, R, G, B);
        }
    }
}
