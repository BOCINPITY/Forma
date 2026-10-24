using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IconAdapt.App.ViewModels;

namespace IconAdapt.App.Controls;

/// <summary>A read-only scene of the batch selection. It never accesses the desktop or wallpaper.</summary>
public partial class DesktopPreview : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable<ShortcutItem>), typeof(DesktopPreview), new PropertyMetadata(null, OnItemsSourceChanged));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(ShortcutItem), typeof(DesktopPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));
    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(nameof(PixelSize), typeof(int), typeof(DesktopPreview), new PropertyMetadata(48), value => value is int size && size is 32 or 48 or 64 or 96);
    public static readonly DependencyProperty SizeCommandProperty = DependencyProperty.Register(nameof(SizeCommand), typeof(ICommand), typeof(DesktopPreview));
    public static readonly DependencyProperty IsAdaptedProperty = DependencyProperty.Register(nameof(IsAdapted), typeof(bool), typeof(DesktopPreview), new PropertyMetadata(true));
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.Register(nameof(IsDark), typeof(bool), typeof(DesktopPreview), new PropertyMetadata(false));

    public IEnumerable<ShortcutItem>? ItemsSource { get => (IEnumerable<ShortcutItem>?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public ShortcutItem? SelectedItem { get => (ShortcutItem?)GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public int PixelSize { get => (int)GetValue(PixelSizeProperty); set => SetValue(PixelSizeProperty, value); }
    public ICommand? SizeCommand { get => (ICommand?)GetValue(SizeCommandProperty); set => SetValue(SizeCommandProperty, value); }
    public bool IsAdapted { get => (bool)GetValue(IsAdaptedProperty); set => SetValue(IsAdaptedProperty, value); }
    public bool IsDark { get => (bool)GetValue(IsDarkProperty); set => SetValue(IsDarkProperty, value); }
    public ObservableCollection<ShortcutItem> SelectedItems { get; } = [];
    public event RoutedEventHandler? LibraryRequested;

    private INotifyCollectionChanged? observedCollection;
    private readonly HashSet<ShortcutItem> observedItems = [];
    private bool syncingSelection;

    public DesktopPreview()
    {
        InitializeComponent();
        Loaded += (_, _) => { Attach(); RefreshItems(); };
        Unloaded += (_, _) => Detach();
    }

    private static void OnItemsSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var preview = (DesktopPreview)sender;
        preview.Detach();
        if (preview.IsLoaded) preview.Attach();
        preview.RefreshItems();
    }

    private static void OnSelectedItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((DesktopPreview)sender).SyncFocus();

    private void Attach()
    {
        Detach();
        observedCollection = ItemsSource as INotifyCollectionChanged;
        if (observedCollection != null) observedCollection.CollectionChanged += OnCollectionChanged;
        ObserveItems();
    }

    private void ObserveItems()
    {
        var current = ItemsSource?.ToHashSet() ?? [];
        foreach (var item in observedItems.Where(x => !current.Contains(x)).ToArray())
        {
            item.PropertyChanged -= OnItemChanged;
            observedItems.Remove(item);
        }
        foreach (var item in current)
            if (observedItems.Add(item)) item.PropertyChanged += OnItemChanged;
    }

    private void Detach()
    {
        if (observedCollection != null) observedCollection.CollectionChanged -= OnCollectionChanged;
        observedCollection = null;
        foreach (var item in observedItems) item.PropertyChanged -= OnItemChanged;
        observedItems.Clear();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ObserveItems();
        RefreshItems();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShortcutItem.IsSelected)) RefreshItems();
    }

    private void RefreshItems()
    {
        var wanted = ItemsSource?.Where(x => x.IsSelected).ToArray() ?? [];
        syncingSelection = true;
        try
        {
            for (int index = SelectedItems.Count - 1; index >= 0; index--)
                if (!wanted.Contains(SelectedItems[index])) SelectedItems.RemoveAt(index);
            for (int index = 0; index < wanted.Length; index++)
            {
                if (index < SelectedItems.Count && ReferenceEquals(SelectedItems[index], wanted[index])) continue;
                int old = SelectedItems.IndexOf(wanted[index]);
                if (old >= 0) SelectedItems.Move(old, index);
                else SelectedItems.Insert(index, wanted[index]);
            }
        }
        finally { syncingSelection = false; }
        SyncFocus();
    }

    private void SyncFocus()
    {
        if (DesktopItems == null) return;
        syncingSelection = true;
        try { DesktopItems.SelectedItem = SelectedItem != null && SelectedItems.Contains(SelectedItem) ? SelectedItem : null; }
        finally { syncingSelection = false; }
    }

    private void OnSceneSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        // Removing a batch check must not clear the inspector's independent focus.
        if (!syncingSelection && DesktopItems.SelectedItem is ShortcutItem item)
            SetCurrentValue(SelectedItemProperty, item);
    }

    private void ShowOriginal(object sender, RoutedEventArgs args) => SetCurrentValue(IsAdaptedProperty, false);
    private void ShowAdapted(object sender, RoutedEventArgs args) => SetCurrentValue(IsAdaptedProperty, true);
    private void ShowLight(object sender, RoutedEventArgs args) => SetCurrentValue(IsDarkProperty, false);
    private void ShowDark(object sender, RoutedEventArgs args) => SetCurrentValue(IsDarkProperty, true);
    private void ReturnToLibrary(object sender, RoutedEventArgs args) => LibraryRequested?.Invoke(this, args);
}

/// <summary>Soft vector scenery rendered locally; independent of user wallpaper and icon rendering.</summary>
public sealed class DesktopBackdrop : FrameworkElement
{
    public static readonly DependencyProperty IsDarkProperty = DependencyProperty.Register(nameof(IsDark), typeof(bool), typeof(DesktopBackdrop), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool IsDark { get => (bool)GetValue(IsDarkProperty); set => SetValue(IsDarkProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var bounds = new Rect(0, 0, width, height);
        drawing.PushClip(new RectangleGeometry(bounds, 12, 12));
        drawing.DrawRectangle(new LinearGradientBrush(ColorOf(IsDark ? "#182B48" : "#DFECF7"), ColorOf(IsDark ? "#324260" : "#D8DDF0"), 55), null, bounds);
        var glow = new RadialGradientBrush(ColorOf(IsDark ? "#355A76" : "#FFF2DC"), Colors.Transparent);
        drawing.DrawEllipse(glow, null, new Point(width * .83, height * .18), width * .9, height * .85);
        var wave = new StreamGeometry();
        using (var geometry = wave.Open())
        {
            geometry.BeginFigure(new Point(0, height * .72), true, true);
            geometry.BezierTo(new Point(width * .35, height * .40), new Point(width * .59, height * 1.1), new Point(width, height * .55), true, false);
            geometry.LineTo(new Point(width, height), true, false);
            geometry.LineTo(new Point(0, height), true, false);
        }
        drawing.DrawGeometry(new LinearGradientBrush(ColorOf(IsDark ? "#354361" : "#CADBEC"), ColorOf(IsDark ? "#182C49" : "#E1E9F4"), 90), null, wave);
        drawing.Pop();
    }

    private static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
}
