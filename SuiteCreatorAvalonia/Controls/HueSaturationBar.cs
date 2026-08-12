using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SuiteCreatorAvalonia.Controls;

/// <summary>
/// A horizontal hue/saturation picker bar: hue maps across the X axis, saturation across the Y axis,
/// both restricted to the given Min/Max ranges so only that band of the colour map is ever shown or
/// selectable. Used in place of Avalonia's built-in ColorSpectrum (which always renders as a square)
/// for the Settings accent-colour pickers, which need a wide, short bar instead.
/// </summary>
public class HueSaturationBar : Control
{
    public static readonly StyledProperty<HsvColor> HsvColorProperty =
        AvaloniaProperty.Register<HueSaturationBar, HsvColor>(
            nameof(HsvColor),
            new HsvColor(1, 0, 0, 1),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinHueProperty =
        AvaloniaProperty.Register<HueSaturationBar, double>(nameof(MinHue), 0);

    public static readonly StyledProperty<double> MaxHueProperty =
        AvaloniaProperty.Register<HueSaturationBar, double>(nameof(MaxHue), 359);

    public static readonly StyledProperty<double> MinSaturationProperty =
        AvaloniaProperty.Register<HueSaturationBar, double>(nameof(MinSaturation), 0);

    public static readonly StyledProperty<double> MaxSaturationProperty =
        AvaloniaProperty.Register<HueSaturationBar, double>(nameof(MaxSaturation), 100);

    public HsvColor HsvColor
    {
        get => GetValue(HsvColorProperty);
        set => SetValue(HsvColorProperty, value);
    }

    public double MinHue
    {
        get => GetValue(MinHueProperty);
        set => SetValue(MinHueProperty, value);
    }

    public double MaxHue
    {
        get => GetValue(MaxHueProperty);
        set => SetValue(MaxHueProperty, value);
    }

    public double MinSaturation
    {
        get => GetValue(MinSaturationProperty);
        set => SetValue(MinSaturationProperty, value);
    }

    public double MaxSaturation
    {
        get => GetValue(MaxSaturationProperty);
        set => SetValue(MaxSaturationProperty, value);
    }

    private WriteableBitmap? _bitmap;
    private Size _bitmapSize;
    private bool _capturing;

    static HueSaturationBar()
    {
        AffectsRender<HueSaturationBar>(HsvColorProperty, MinHueProperty, MaxHueProperty, MinSaturationProperty, MaxSaturationProperty);
    }

    public HueSaturationBar()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MinHueProperty || change.Property == MaxHueProperty ||
            change.Property == MinSaturationProperty || change.Property == MaxSaturationProperty)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        EnsureBitmap(bounds.Size);
        if (_bitmap is not null)
        {
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), bounds.WithX(0).WithY(0));
        }

        DrawThumb(context, bounds);
    }

    private void EnsureBitmap(Size size)
    {
        var pixelWidth = Math.Max(1, (int)Math.Round(size.Width));
        var pixelHeight = Math.Max(1, (int)Math.Round(size.Height));

        if (_bitmap is not null && _bitmapSize.Width == pixelWidth && _bitmapSize.Height == pixelHeight)
        {
            return;
        }

        _bitmap?.Dispose();
        _bitmapSize = new Size(pixelWidth, pixelHeight);
        _bitmap = new WriteableBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        var minHue = MinHue;
        var maxHue = Math.Max(MaxHue, minHue + 0.01);
        var minSat = MinSaturation;
        var maxSat = Math.Max(MaxSaturation, minSat + 0.01);

        using var buffer = _bitmap.Lock();
        unsafe
        {
            var ptr = (byte*)buffer.Address;
            for (var y = 0; y < pixelHeight; y++)
            {
                // Top of the bar = maximum saturation, bottom = minimum.
                var saturation = pixelHeight <= 1
                    ? maxSat
                    : maxSat - (maxSat - minSat) * (y / (double)(pixelHeight - 1));

                var rowStart = ptr + y * buffer.RowBytes;
                for (var x = 0; x < pixelWidth; x++)
                {
                    var hue = pixelWidth <= 1
                        ? minHue
                        : minHue + (maxHue - minHue) * (x / (double)(pixelWidth - 1));

                    var color = new HsvColor(1, hue, saturation / 100.0, 1).ToRgb();
                    var offset = rowStart + x * 4;
                    offset[0] = color.B;
                    offset[1] = color.G;
                    offset[2] = color.R;
                    offset[3] = 255;
                }
            }
        }
    }

    private void DrawThumb(DrawingContext context, Rect bounds)
    {
        var minHue = MinHue;
        var maxHue = Math.Max(MaxHue, minHue + 0.01);
        var minSat = MinSaturation;
        var maxSat = Math.Max(MaxSaturation, minSat + 0.01);

        var hsv = HsvColor;
        var huePct = Clamp((hsv.H - minHue) / (maxHue - minHue), 0, 1);
        var satPct = Clamp((hsv.S * 100 - minSat) / (maxSat - minSat), 0, 1);

        var x = bounds.X + huePct * bounds.Width;
        var y = bounds.Y + (1 - satPct) * bounds.Height;

        var rgb = hsv.ToRgb();
        var isDark = (rgb.R * 0.299 + rgb.G * 0.587 + rgb.B * 0.114) < 140;
        var ringBrush = isDark ? Brushes.White : Brushes.Black;

        const double radius = 7;
        context.DrawEllipse(null, new Pen(ringBrush, 2), new Point(x, y), radius, radius);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.Black, 0.3), 1), new Point(x, y), radius + 1.5, radius + 1.5);
    }

    private static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _capturing = true;
        e.Pointer.Capture(this);
        UpdateFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_capturing)
        {
            UpdateFromPoint(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_capturing)
        {
            _capturing = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void UpdateFromPoint(Point point)
    {
        var bounds = Bounds;
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            return;
        }

        var huePct = Clamp(point.X / bounds.Width, 0, 1);
        var satPct = Clamp(1 - point.Y / bounds.Height, 0, 1);

        var minHue = MinHue;
        var maxHue = Math.Max(MaxHue, minHue + 0.01);
        var minSat = MinSaturation;
        var maxSat = Math.Max(MaxSaturation, minSat + 0.01);

        var hue = minHue + huePct * (maxHue - minHue);
        var saturation = (minSat + satPct * (maxSat - minSat)) / 100.0;

        var current = HsvColor;
        SetCurrentValue(HsvColorProperty, new HsvColor(current.A, hue, saturation, current.V));
    }
}
