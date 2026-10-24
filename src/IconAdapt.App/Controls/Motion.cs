using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IconAdapt.App.Controls;

/// <summary>
/// Short, interruptible motion for UI chrome. Bind the attached target properties;
/// Motion owns the corresponding actual properties. Never changes icon pixel geometry.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty OpacityProperty = DependencyProperty.RegisterAttached(
        "Opacity", typeof(double), typeof(Motion), new PropertyMetadata(1d, OnOpacityChanged),
        value => value is double number && double.IsFinite(number) && number is >= 0 and <= 1);
    public static readonly DependencyProperty OffsetXProperty = DependencyProperty.RegisterAttached(
        "OffsetX", typeof(double), typeof(Motion), new PropertyMetadata(0d, OnOffsetChanged),
        value => value is double number && double.IsFinite(number));
    public static readonly DependencyProperty AngleProperty = DependencyProperty.RegisterAttached(
        "Angle", typeof(double), typeof(Motion), new PropertyMetadata(0d, OnAngleChanged),
        value => value is double number && double.IsFinite(number));
    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached(
        "Reveal", typeof(bool), typeof(Motion), new PropertyMetadata(true, OnRevealChanged));
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(true,
            FrameworkPropertyMetadataOptions.Inherits, OnEnabledChanged));

    public static double GetOpacity(DependencyObject element) => (double)element.GetValue(OpacityProperty);
    public static void SetOpacity(DependencyObject element, double value) => element.SetValue(OpacityProperty, value);
    public static double GetOffsetX(DependencyObject element) => (double)element.GetValue(OffsetXProperty);
    public static void SetOffsetX(DependencyObject element, double value) => element.SetValue(OffsetXProperty, value);
    public static double GetAngle(DependencyObject element) => (double)element.GetValue(AngleProperty);
    public static void SetAngle(DependencyObject element, double value) => element.SetValue(AngleProperty, value);
    public static bool GetReveal(DependencyObject element) => (bool)element.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject element, bool value) => element.SetValue(RevealProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool CanAnimate(UIElement element) => element is FrameworkElement { IsLoaded: true } &&
        GetIsEnabled(element) && SystemParameters.ClientAreaAnimation;

    private enum Channel { Opacity, Offset, Angle, Reveal }
    private sealed class State(FrameworkElement owner)
    {
        public FrameworkElement Owner { get; } = owner;
        public HashSet<Channel> Configured { get; } = [];
        public Dictionary<Channel, Running> Animations { get; } = [];
    }
    private sealed class Running(Action stop, Action finish)
    {
        public Action Stop { get; } = stop;
        public Action Finish { get; } = finish;
    }

    private static readonly ConditionalWeakTable<FrameworkElement, State> States = new();
    private static readonly HashSet<State> Active = [];
    private static readonly object ActiveLock = new();

    private static State Configure(FrameworkElement element, Channel channel)
    {
        var state = States.GetValue(element, owner =>
        {
            owner.Loaded += OnLoaded;
            owner.Unloaded += OnUnloaded;
            return new State(owner);
        });
        state.Configured.Add(channel);
        return state;
    }

    private static void OnOpacityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var state = Configure(element, Channel.Opacity);
        double target = (double)args.NewValue;
        Animate(state, Channel.Opacity, element.Opacity, target, 120,
            animation => element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace),
            value => element.SetValue(UIElement.OpacityProperty, value),
            () => element.SetValue(UIElement.OpacityProperty, target));
    }

    /// <summary>Gently reveals a newly selected source; no scale, translation or resampling.</summary>
    public static void FadeIn(UIElement owner)
    {
        if (owner is not FrameworkElement element) return;
        var state = Configure(element, Channel.Opacity);
        double target = GetOpacity(element);
        double from = state.Animations.ContainsKey(Channel.Opacity) ? element.Opacity : target * .78;
        Animate(state, Channel.Opacity, from, target, 140,
            animation => element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace),
            value => element.SetValue(UIElement.OpacityProperty, value),
            () => element.SetValue(UIElement.OpacityProperty, target));
    }

    private static void OnOffsetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var state = Configure(element, Channel.Offset);
        var transform = Translation(element);
        double target = (double)args.NewValue;
        Animate(state, Channel.Offset, transform.X, target, 170,
            animation => transform.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace),
            value => transform.SetValue(TranslateTransform.XProperty, value),
            () => transform.SetValue(TranslateTransform.XProperty, target));
    }

    private static void OnAngleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var state = Configure(element, Channel.Angle);
        var transform = Rotation(element);
        double target = (double)args.NewValue;
        Animate(state, Channel.Angle, transform.Angle, target, 150,
            animation => transform.BeginAnimation(RotateTransform.AngleProperty, animation, HandoffBehavior.SnapshotAndReplace),
            value => transform.SetValue(RotateTransform.AngleProperty, value),
            () => transform.SetValue(RotateTransform.AngleProperty, target));
    }

    // Offset and angle belong on separate dedicated chrome elements, not existing transform groups.
    private static TranslateTransform Translation(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform { IsFrozen: false } translation) return translation;
        var replacement = new TranslateTransform();
        element.SetValue(UIElement.RenderTransformProperty, replacement);
        return replacement;
    }
    private static RotateTransform Rotation(FrameworkElement element)
    {
        if (element.RenderTransform is RotateTransform { IsFrozen: false } rotation) return rotation;
        var replacement = new RotateTransform();
        element.SetValue(UIElement.RenderTransformProperty, replacement);
        return replacement;
    }

    private static void OnRevealChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        var state = Configure(element, Channel.Reveal);
        bool reveal = (bool)args.NewValue;
        element.SetValue(UIElement.ClipToBoundsProperty, true);
        if (!CanAnimate(element))
        {
            Cancel(state, Channel.Reveal);
            SetRevealTarget(element, reveal);
            return;
        }

        double from = element.Visibility == Visibility.Collapsed ? 0 :
            double.IsFinite(element.Height) ? element.Height : element.ActualHeight;
        Cancel(state, Channel.Reveal);
        element.SetValue(UIElement.VisibilityProperty, Visibility.Visible);
        double target = 0;
        if (reveal)
        {
            element.SetValue(FrameworkElement.HeightProperty, double.NaN);
            double width = element.ActualWidth > 0 ? element.ActualWidth + element.Margin.Left + element.Margin.Right :
                (element.Parent as FrameworkElement)?.ActualWidth ?? double.PositiveInfinity;
            if (width <= 0) width = double.PositiveInfinity;
            element.Measure(new Size(width, double.PositiveInfinity));
            target = Math.Max(0, element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom);
        }
        element.SetValue(FrameworkElement.HeightProperty, Math.Max(0, from));
        Animate(state, Channel.Reveal, Math.Max(0, from), target, 180,
            animation => element.BeginAnimation(FrameworkElement.HeightProperty, animation, HandoffBehavior.SnapshotAndReplace),
            value => element.SetValue(FrameworkElement.HeightProperty, value),
            () => SetRevealTarget(element, reveal));
    }

    private static void SetRevealTarget(FrameworkElement element, bool reveal)
    {
        element.SetValue(UIElement.ClipToBoundsProperty, true);
        element.SetValue(FrameworkElement.HeightProperty, reveal ? double.NaN : 0d);
        element.SetValue(UIElement.VisibilityProperty, reveal ? Visibility.Visible : Visibility.Collapsed);
    }

    private static void Animate(State state, Channel channel, double from, double to, int milliseconds,
        Action<AnimationTimeline?> apply, Action<double> setBase, Action setTarget)
    {
        Cancel(state, channel);
        if (!CanAnimate(state.Owner) || Math.Abs(from - to) < .001 || !double.IsFinite(from) || !double.IsFinite(to))
        {
            setTarget();
            return;
        }
        // A replacement clock may not evaluate until the next dispatcher tick. Preserve the
        // currently displayed value as its base so interrupting motion cannot expose an old endpoint.
        setBase(from);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        // Clear the animation before writing its real final base value. SetCurrentValue while
        // animated can be only a transient overlay and disappear when the clock is removed.
        var running = new Running(() => apply(null), () => { apply(null); setTarget(); });
        animation.Completed += (_, _) => Complete(state, channel, running);
        state.Animations[channel] = running;
        Register(state);
        apply(animation);
    }

    private static void Cancel(State state, Channel channel)
    {
        if (!state.Animations.Remove(channel, out var running)) return;
        running.Stop();
        UnregisterIfIdle(state);
    }
    private static void Complete(State state, Channel channel, Running running)
    {
        if (!state.Animations.TryGetValue(channel, out var current) || !ReferenceEquals(current, running)) return;
        state.Animations.Remove(channel);
        running.Finish();
        UnregisterIfIdle(state);
    }
    private static void FinishAll(State state)
    {
        foreach (var entry in state.Animations.ToArray()) Complete(state, entry.Key, entry.Value);
    }

    private static void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element || !States.TryGetValue(element, out var state)) return;
        FinishAll(state);
        foreach (var channel in state.Configured)
        {
            switch (channel)
            {
                case Channel.Opacity: element.SetValue(UIElement.OpacityProperty, GetOpacity(element)); break;
                case Channel.Offset: Translation(element).SetValue(TranslateTransform.XProperty, GetOffsetX(element)); break;
                case Channel.Angle: Rotation(element).SetValue(RotateTransform.AngleProperty, GetAngle(element)); break;
                case Channel.Reveal: SetRevealTarget(element, GetReveal(element)); break;
            }
        }
    }
    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element && States.TryGetValue(element, out var state)) FinishAll(state);
    }
    private static void OnEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is false && sender is FrameworkElement element && States.TryGetValue(element, out var state))
            FinishAll(state);
    }

    private static void Register(State state)
    {
        lock (ActiveLock)
        {
            if (Active.Count == 0) SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
            Active.Add(state);
        }
    }
    private static void UnregisterIfIdle(State state)
    {
        if (state.Animations.Count != 0) return;
        lock (ActiveLock)
        {
            Active.Remove(state);
            if (Active.Count == 0) SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        }
    }
    private static void OnSystemPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.PropertyName) && args.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        State[] snapshot;
        lock (ActiveLock) snapshot = Active.ToArray();
        foreach (var state in snapshot)
        {
            void Refresh() { if (!CanAnimate(state.Owner)) FinishAll(state); }
            if (state.Owner.Dispatcher.CheckAccess()) Refresh();
            else state.Owner.Dispatcher.BeginInvoke(DispatcherPriority.Send, (Action)Refresh);
        }
    }
}
