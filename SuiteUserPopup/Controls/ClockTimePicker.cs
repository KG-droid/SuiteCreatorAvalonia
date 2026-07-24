using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace SuiteUserPopup.Controls;

public class ClockTimePicker : TemplatedControl
{
    private enum ClockMode
    {
        Hour,
        Minute
    }

    private const string ToggleButtonPartName = "PART_ToggleButton";
    private const string PopupPartName = "PART_Popup";
    private const string ClockCanvasPartName = "PART_ClockCanvas";
    private const string HandPartName = "PART_Hand";
    private const string HandKnobPartName = "PART_HandKnob";
    private const string HourModeButtonPartName = "PART_HourModeButton";
    private const string MinuteModeButtonPartName = "PART_MinuteModeButton";
    private const string AmButtonPartName = "PART_AmButton";
    private const string PmButtonPartName = "PART_PmButton";
    private const string DoneButtonPartName = "PART_DoneButton";

    private const double CanvasSize = 200;
    private const double CanvasCenter = CanvasSize / 2;
    private const double NumberRadius = 78;

    public static readonly StyledProperty<TimeSpan> SelectedTimeProperty =
        AvaloniaProperty.Register<ClockTimePicker, TimeSpan>(nameof(SelectedTime), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> DisplayTextProperty =
        AvaloniaProperty.Register<ClockTimePicker, string>(nameof(DisplayText), "12:00 AM");

    public static readonly StyledProperty<string> HourDisplayProperty =
        AvaloniaProperty.Register<ClockTimePicker, string>(nameof(HourDisplay), "12");

    public static readonly StyledProperty<string> MinuteDisplayProperty =
        AvaloniaProperty.Register<ClockTimePicker, string>(nameof(MinuteDisplay), "00");

    public TimeSpan SelectedTime
    {
        get => GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    public string DisplayText
    {
        get => GetValue(DisplayTextProperty);
        private set => SetValue(DisplayTextProperty, value);
    }

    public string HourDisplay
    {
        get => GetValue(HourDisplayProperty);
        private set => SetValue(HourDisplayProperty, value);
    }

    public string MinuteDisplay
    {
        get => GetValue(MinuteDisplayProperty);
        private set => SetValue(MinuteDisplayProperty, value);
    }

    private int _hour12 = 12;
    private int _minute;
    private bool _isPm;
    private ClockMode _mode = ClockMode.Hour;
    private bool _suppressExternalSync;
    private bool _isPointerDown;

    private ToggleButton? _toggleButton;
    private Popup? _popup;
    private Canvas? _clockCanvas;
    private Line? _hand;
    private Ellipse? _handKnob;
    private Button? _hourModeButton;
    private Button? _minuteModeButton;
    private ToggleButton? _amButton;
    private ToggleButton? _pmButton;
    private Button? _doneButton;
    private readonly List<TextBlock> _numberLabels = new();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        DetachHandlers();

        _toggleButton = e.NameScope.Find<ToggleButton>(ToggleButtonPartName);
        _popup = e.NameScope.Find<Popup>(PopupPartName);
        _clockCanvas = e.NameScope.Find<Canvas>(ClockCanvasPartName);
        _hand = e.NameScope.Find<Line>(HandPartName);
        _handKnob = e.NameScope.Find<Ellipse>(HandKnobPartName);
        _hourModeButton = e.NameScope.Find<Button>(HourModeButtonPartName);
        _minuteModeButton = e.NameScope.Find<Button>(MinuteModeButtonPartName);
        _amButton = e.NameScope.Find<ToggleButton>(AmButtonPartName);
        _pmButton = e.NameScope.Find<ToggleButton>(PmButtonPartName);
        _doneButton = e.NameScope.Find<Button>(DoneButtonPartName);

        if (_popup != null && _toggleButton != null)
        {
            _popup.PlacementTarget = _toggleButton;
            _popup.Placement = PlacementMode.Top;
        }

        AttachHandlers();

        SyncFromSelectedTime();
        RebuildFace();
    }

    private void AttachHandlers()
    {
        if (_toggleButton != null) _toggleButton.IsCheckedChanged += OnToggleButtonCheckedChanged;
        if (_clockCanvas != null)
        {
            _clockCanvas.PointerPressed += OnClockPointerPressed;
            _clockCanvas.PointerMoved += OnClockPointerMoved;
            _clockCanvas.PointerReleased += OnClockPointerReleased;
        }
        if (_hourModeButton != null) _hourModeButton.Click += OnHourModeClick;
        if (_minuteModeButton != null) _minuteModeButton.Click += OnMinuteModeClick;
        if (_amButton != null) _amButton.Click += OnAmClick;
        if (_pmButton != null) _pmButton.Click += OnPmClick;
        if (_doneButton != null) _doneButton.Click += OnDoneClick;
        if (_popup != null) _popup.Closed += OnPopupClosed;
    }

    private void DetachHandlers()
    {
        if (_toggleButton != null) _toggleButton.IsCheckedChanged -= OnToggleButtonCheckedChanged;
        if (_clockCanvas != null)
        {
            _clockCanvas.PointerPressed -= OnClockPointerPressed;
            _clockCanvas.PointerMoved -= OnClockPointerMoved;
            _clockCanvas.PointerReleased -= OnClockPointerReleased;
        }
        if (_hourModeButton != null) _hourModeButton.Click -= OnHourModeClick;
        if (_minuteModeButton != null) _minuteModeButton.Click -= OnMinuteModeClick;
        if (_amButton != null) _amButton.Click -= OnAmClick;
        if (_pmButton != null) _pmButton.Click -= OnPmClick;
        if (_doneButton != null) _doneButton.Click -= OnDoneClick;
        if (_popup != null) _popup.Closed -= OnPopupClosed;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedTimeProperty && !_suppressExternalSync)
        {
            SyncFromSelectedTime();
            RebuildFace();
        }
    }

    // ── Popup toggling ──────────────────────────────────────────────────────

    private void OnToggleButtonCheckedChanged(object? sender, RoutedEventArgs e)
    {
        bool isOpen = _toggleButton?.IsChecked == true;

        if (_popup != null) _popup.IsOpen = isOpen;

        if (isOpen)
        {
            _mode = ClockMode.Hour;
            RebuildFace();
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (_toggleButton != null && _toggleButton.IsChecked == true)
        {
            _toggleButton.IsChecked = false;
        }
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        if (_popup != null) _popup.IsOpen = false;
    }

    // ── Mode / AM-PM switching ──────────────────────────────────────────────

    private void OnHourModeClick(object? sender, RoutedEventArgs e)
    {
        _mode = ClockMode.Hour;
        RebuildFace();
    }

    private void OnMinuteModeClick(object? sender, RoutedEventArgs e)
    {
        _mode = ClockMode.Minute;
        RebuildFace();
    }

    private void OnAmClick(object? sender, RoutedEventArgs e)
    {
        _isPm = false;
        CommitSelectedTime();
        RebuildFace();
    }

    private void OnPmClick(object? sender, RoutedEventArgs e)
    {
        _isPm = true;
        CommitSelectedTime();
        RebuildFace();
    }

    // ── Pointer interaction on the clock face ───────────────────────────────

    private void OnClockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_clockCanvas == null) return;

        _isPointerDown = true;
        e.Pointer.Capture(_clockCanvas);
        UpdateValueFromPointer(e.GetPosition(_clockCanvas));
    }

    private void OnClockPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerDown || _clockCanvas == null) return;

        UpdateValueFromPointer(e.GetPosition(_clockCanvas));
    }

    private void OnClockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerDown || _clockCanvas == null) return;

        _isPointerDown = false;
        e.Pointer.Capture(null);
        UpdateValueFromPointer(e.GetPosition(_clockCanvas));

        if (_mode == ClockMode.Hour)
        {
            _mode = ClockMode.Minute;
            RebuildFace();
        }
    }

    private void UpdateValueFromPointer(Point position)
    {
        double dx = position.X - CanvasCenter;
        double dy = position.Y - CanvasCenter;
        double angle = Math.Atan2(dx, -dy);
        if (angle < 0) angle += 2 * Math.PI;

        if (_mode == ClockMode.Hour)
        {
            int value = (int)Math.Round(angle / (2 * Math.PI) * 12) % 12;
            _hour12 = value == 0 ? 12 : value;
        }
        else
        {
            int value = (int)Math.Round(angle / (2 * Math.PI) * 60) % 60;
            _minute = value;
        }

        CommitSelectedTime();
        UpdateHand();
    }

    // ── State sync ───────────────────────────────────────────────────────────

    private void SyncFromSelectedTime()
    {
        int hour24 = ((SelectedTime.Hours % 24) + 24) % 24;
        _isPm = hour24 >= 12;
        int h = hour24 % 12;
        _hour12 = h == 0 ? 12 : h;
        _minute = Math.Clamp(SelectedTime.Minutes, 0, 59);

        RefreshDisplayStrings();
    }

    private void RefreshDisplayStrings()
    {
        DisplayText = $"{_hour12:00}:{_minute:00} {(_isPm ? "PM" : "AM")}";
        HourDisplay = _hour12.ToString();
        MinuteDisplay = _minute.ToString("00");
    }

    private void CommitSelectedTime()
    {
        int hour24 = (_hour12 % 12) + (_isPm ? 12 : 0);
        var newTime = new TimeSpan(hour24, _minute, 0);

        _suppressExternalSync = true;
        SelectedTime = newTime;
        _suppressExternalSync = false;

        RefreshDisplayStrings();
    }

    // ── Clock face rendering ─────────────────────────────────────────────────

    private void RebuildFace()
    {
        if (_clockCanvas == null) return;

        foreach (TextBlock label in _numberLabels)
        {
            _clockCanvas.Children.Remove(label);
        }
        _numberLabels.Clear();

        for (int i = 0; i < 12; i++)
        {
            int value = _mode == ClockMode.Hour ? (i == 0 ? 12 : i) : i * 5;
            string text = _mode == ClockMode.Hour ? value.ToString() : value.ToString("00");

            (double x, double y) = GetPoint(i, 12, NumberRadius);

            var label = new TextBlock
            {
                Text = text,
                FontSize = 14,
                Width = 26,
                Height = 20,
                TextAlignment = TextAlignment.Center,
                Tag = value
            };
            label.Classes.Add("clock-number");
            Canvas.SetLeft(label, x - (label.Width / 2));
            Canvas.SetTop(label, y - (label.Height / 2));

            _clockCanvas.Children.Add(label);
            _numberLabels.Add(label);
        }

        if (_hourModeButton != null)
        {
            _hourModeButton.FontWeight = _mode == ClockMode.Hour ? FontWeight.Bold : FontWeight.Normal;
        }

        if (_minuteModeButton != null)
        {
            _minuteModeButton.FontWeight = _mode == ClockMode.Minute ? FontWeight.Bold : FontWeight.Normal;
        }

        if (_amButton != null) _amButton.IsChecked = !_isPm;
        if (_pmButton != null) _pmButton.IsChecked = _isPm;

        UpdateHand();
    }

    private void UpdateHand()
    {
        int steps = _mode == ClockMode.Hour ? 12 : 60;
        int value = _mode == ClockMode.Hour ? (_hour12 % 12) : _minute;

        (double x, double y) = GetPoint(value, steps, NumberRadius);

        if (_hand != null)
        {
            _hand.StartPoint = new Point(CanvasCenter, CanvasCenter);
            _hand.EndPoint = new Point(x, y);
        }

        if (_handKnob != null)
        {
            Canvas.SetLeft(_handKnob, x - (_handKnob.Width / 2));
            Canvas.SetTop(_handKnob, y - (_handKnob.Height / 2));
        }

        int highlighted = _mode == ClockMode.Hour ? _hour12 : RoundToNearestFive(_minute);
        foreach (TextBlock label in _numberLabels)
        {
            bool isSelected = label.Tag is int tag && tag == highlighted;
            label.Classes.Set("selected", isSelected);
        }
    }

    private static int RoundToNearestFive(int minute)
    {
        return ((int)Math.Round(minute / 5.0) * 5) % 60;
    }

    private static (double x, double y) GetPoint(int value, int totalSteps, double radius)
    {
        double angle = 2 * Math.PI * value / totalSteps;
        double x = CanvasCenter + (radius * Math.Sin(angle));
        double y = CanvasCenter - (radius * Math.Cos(angle));
        return (x, y);
    }
}
