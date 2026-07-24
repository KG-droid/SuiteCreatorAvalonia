using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace SuiteUserPopup.Controls;

public class ScrollSelector : TemplatedControl
{
    private const string PreviousButtonPartName = "PART_PreviousButton";
    private const string NextButtonPartName = "PART_NextButton";
    private const string ItemsViewportPartName = "PART_ItemsViewport";

    public static readonly StyledProperty<IList<Control>?> ItemsProperty =
        AvaloniaProperty.Register<ScrollSelector, IList<Control>?>(nameof(Items));

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<ScrollSelector, Orientation>(nameof(Orientation), Orientation.Vertical);

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<ScrollSelector, int>(nameof(SelectedIndex), 0);

    public static readonly StyledProperty<Control?> SelectedItemProperty =
        AvaloniaProperty.Register<ScrollSelector, Control?>(nameof(SelectedItem));

    public IList<Control>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public Control? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private Panel? _itemsPanel;
    private Control? _itemsViewport;
    private Button? _previousButton;
    private Button? _nextButton;
    private readonly TranslateTransform _itemsPanelTranslateTransform = new();
    private bool _isCenteringScheduled;
    private bool _shouldSelectMiddleItem;
    private static readonly TimeSpan NavigationAnimationDuration = TimeSpan.FromSeconds(0.5);
    private const double SelectedScale = 1.0;
    private const double AdjacentScale = 0.75;
    private const double FarScale = 0.5;
    private const double SelectedOpacity = 1.0;
    private const double AdjacentOpacity = 0.6;
    private const double FarOpacity = 0.35;

    public ScrollSelector()
    {
        _itemsPanelTranslateTransform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = NavigationAnimationDuration,
                Easing = new CubicEaseOut()
            },
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = NavigationAnimationDuration,
                Easing = new CubicEaseOut()
            }
        };

        AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Bubble, true);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_itemsPanel != null)
        {
            _itemsPanel.PointerPressed -= OnPointerPressed;
        }

        if (_previousButton != null)
        {
            _previousButton.Click -= OnPreviousButtonClick;
        }

        if (_nextButton != null)
        {
            _nextButton.Click -= OnNextButtonClick;
        }

        _itemsPanel = e.NameScope.Find<Panel>("PART_ItemsPanel");
        _itemsViewport = e.NameScope.Find<Control>(ItemsViewportPartName);
        _previousButton = e.NameScope.Find<Button>(PreviousButtonPartName);
        _nextButton = e.NameScope.Find<Button>(NextButtonPartName);

        if (_itemsPanel != null)
        {
            _itemsPanel.RenderTransformOrigin = RelativePoint.TopLeft;
            _itemsPanel.RenderTransform = _itemsPanelTranslateTransform;
            _itemsPanel.PointerPressed += OnPointerPressed;
        }

        if (_previousButton != null)
        {
            _previousButton.Click += OnPreviousButtonClick;
        }

        if (_nextButton != null)
        {
            _nextButton.Click += OnNextButtonClick;
        }

        RebuildItems();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsProperty)
        {
            IList<Control>? oldItems = change.GetOldValue<IList<Control>?>();
            IList<Control>? newItems = change.GetNewValue<IList<Control>?>();

            UnsubscribeFromItemsCollectionChanged(oldItems);
            SubscribeToItemsCollectionChanged(newItems);

            _shouldSelectMiddleItem = true;
            RebuildItems();
        }
        else if (change.Property == OrientationProperty)
        {
            RebuildItems();
        }
        else if (change.Property == SelectedIndexProperty)
        {
            UpdateSelectedItem();
            ApplyScaling();
            UpdateNavigationButtons();
            ScheduleCenterSelectedItem();
        }
        else if (change.Property == BoundsProperty)
        {
            ScheduleCenterSelectedItem();
        }
    }

    // ── Interaction ──────────────────────────────────────────────────────────

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        double delta = e.Delta.Y;
        if (delta < 0)
            MoveNext();
        else
            MovePrevious();

        e.Handled = true;
    }

    private Point _pressedPoint;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedPoint = e.GetPosition(_itemsPanel);

        if (_itemsPanel != null)
        {
            _itemsPanel.PointerReleased += OnPointerReleased;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_itemsPanel != null)
        {
            _itemsPanel.PointerReleased -= OnPointerReleased;
        }

        Point releasedPoint = e.GetPosition(_itemsPanel);
        double delta = Orientation == Orientation.Vertical
            ? releasedPoint.Y - _pressedPoint.Y
            : releasedPoint.X - _pressedPoint.X;

        if (Math.Abs(delta) > 20)
        {
            if (delta < 0)
                MoveNext();
            else
                MovePrevious();
        }
    }

    private void OnPreviousButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MovePrevious();
    }

    private void OnNextButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        MoveNext();
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    public void MoveNext()
    {
        IList<Control>? items = Items;
        if (items == null || items.Count == 0) return;
        if (SelectedIndex < items.Count - 1)
            SelectedIndex++;
    }

    public void MovePrevious()
    {
        if (SelectedIndex > 0)
            SelectedIndex--;
    }

    // ── Layout / Styling ─────────────────────────────────────────────────────

    private void RebuildItems()
    {
        if (_itemsPanel == null) return;

        // Sync StackPanel orientation
        if (_itemsPanel is StackPanel sp)
        {
            sp.Orientation = Orientation;
            sp.HorizontalAlignment = Orientation == Orientation.Horizontal
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Center;
            sp.VerticalAlignment = Orientation == Orientation.Vertical
                ? VerticalAlignment.Top
                : VerticalAlignment.Center;
        }

        _itemsPanel.Children.Clear();

        IList<Control>? items = Items;
        if (items == null) return;

        foreach (Control item in items)
        {
            item.RenderTransformOrigin = RelativePoint.Center;
            _itemsPanel.Children.Add(item);
        }

        if (items.Count > 0)
        {
            if (_shouldSelectMiddleItem)
            {
                SelectedIndex = items.Count / 2;
            }
            else
            {
                SelectedIndex = Math.Clamp(SelectedIndex, 0, items.Count - 1);
            }
        }

        _shouldSelectMiddleItem = false;

        UpdateSelectedItem();
        ApplyScaling();
        UpdateNavigationButtons();
        ScheduleCenterSelectedItem();
    }

    private void SubscribeToItemsCollectionChanged(IList<Control>? items)
    {
        if (items is INotifyCollectionChanged notifyCollectionChanged)
        {
            notifyCollectionChanged.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void UnsubscribeFromItemsCollectionChanged(IList<Control>? items)
    {
        if (items is INotifyCollectionChanged notifyCollectionChanged)
        {
            notifyCollectionChanged.CollectionChanged -= OnItemsCollectionChanged;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _shouldSelectMiddleItem = true;
        RebuildItems();
    }

    private void UpdateSelectedItem()
    {
        IList<Control>? items = Items;
        if (items == null || items.Count == 0)
        {
            SelectedItem = null;
            return;
        }

        int idx = Math.Clamp(SelectedIndex, 0, items.Count - 1);
        SelectedItem = items[idx];
    }

    private void ApplyScaling()
    {
        IList<Control>? items = Items;
        if (items == null || _itemsPanel == null) return;

        int selected = SelectedIndex;

        for (int i = 0; i < items.Count; i++)
        {
            Control item = items[i];
            int distance = Math.Abs(i - selected);

            double scale;
            double opacity;

            switch (distance)
            {
                case 0:
                    scale = SelectedScale;
                    opacity = SelectedOpacity;
                    break;
                case 1:
                    scale = AdjacentScale;
                    opacity = AdjacentOpacity;
                    break;
                default:
                    scale = FarScale;
                    opacity = FarOpacity;
                    break;
            }

            item.RenderTransform = new Avalonia.Media.ScaleTransform(scale, scale);
            item.Opacity = opacity;
            item.IsVisible = true;
        }
    }

    private void ScheduleCenterSelectedItem()
    {
        if (_isCenteringScheduled)
        {
            return;
        }

        _isCenteringScheduled = true;

        Dispatcher.UIThread.Post(() =>
        {
            _isCenteringScheduled = false;
            CenterSelectedItem();
        }, DispatcherPriority.Render);
    }

    private void CenterSelectedItem()
    {
        Panel? itemsPanel = _itemsPanel;
        Control? itemsViewport = _itemsViewport;
        IList<Control>? items = Items;

        if (itemsPanel == null || itemsViewport == null || items == null || items.Count == 0)
        {
            _itemsPanelTranslateTransform.X = 0;
            _itemsPanelTranslateTransform.Y = 0;
            return;
        }

        int selectedIndex = Math.Clamp(SelectedIndex, 0, items.Count - 1);
        Control selectedItem = items[selectedIndex];
        Rect viewportBounds = itemsViewport.Bounds;
        Rect itemBounds = selectedItem.Bounds;

        if (Orientation == Orientation.Horizontal)
        {
            if (viewportBounds.Width <= 0 || itemBounds.Width <= 0)
            {
                return;
            }

            double viewportCenter = viewportBounds.Width / 2;
            double itemCenter = itemBounds.X + (itemBounds.Width / 2);

            _itemsPanelTranslateTransform.X = viewportCenter - itemCenter;
            _itemsPanelTranslateTransform.Y = 0;
        }
        else
        {
            if (viewportBounds.Height <= 0 || itemBounds.Height <= 0)
            {
                return;
            }

            double viewportCenter = viewportBounds.Height / 2;
            double itemCenter = itemBounds.Y + (itemBounds.Height / 2);

            _itemsPanelTranslateTransform.X = 0;
            _itemsPanelTranslateTransform.Y = viewportCenter - itemCenter;
        }
    }

    private void UpdateNavigationButtons()
    {
        IList<Control>? items = Items;
        bool hasItems = items != null && items.Count > 0;

        if (_previousButton != null)
        {
            _previousButton.IsEnabled = hasItems && SelectedIndex > 0;
        }

        if (_nextButton != null)
        {
            _nextButton.IsEnabled = hasItems && items != null && SelectedIndex < items.Count - 1;
        }
    }
}
