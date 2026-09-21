// The ThinkingOrb control. One shared clock keeps every mounted orb in
// phase; a single timer invalidates the live ones. Orbs that are not
// effectively visible are skipped, so a collapsed panel or a hidden tab
// costs nothing. Reduced-motion users get a static representative frame
// that still follows the live theme.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ThinkingOrbs;

/// <summary>Which substrate the orb inks for.</summary>
public enum OrbTheme
{
    /// <summary>Follow <see cref="StyledElement.ActualThemeVariant"/>.</summary>
    Auto,
    /// <summary>Light ink, for dark backgrounds.</summary>
    Dark,
    /// <summary>Dark ink, for light backgrounds.</summary>
    Light
}

/// <summary>
/// A dotted thought-orb loading indicator. Nine hand-tuned animated states,
/// each shipped at two purpose-tuned sizes.
/// </summary>
public class ThinkingOrb : Control
{
    /// <summary>Which animation to show.</summary>
    public static readonly StyledProperty<OrbState> StateProperty =
        AvaloniaProperty.Register<ThinkingOrb, OrbState>(nameof(State));

    /// <summary>Tuned size preset — 64 or 20 device-independent pixels.</summary>
    public static readonly StyledProperty<OrbSize> SizeProperty =
        AvaloniaProperty.Register<ThinkingOrb, OrbSize>(nameof(Size), OrbSize.Px64);

    /// <summary>Theme mode; <see cref="OrbTheme.Auto"/> follows the host.</summary>
    public static readonly StyledProperty<OrbTheme> ThemeProperty =
        AvaloniaProperty.Register<ThinkingOrb, OrbTheme>(nameof(Theme));

    /// <summary>Multiplier on top of the preset's baked speed.</summary>
    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<ThinkingOrb, double>(nameof(Speed), 1.0);

    /// <summary>Freeze the animation on the current frame.</summary>
    public static readonly StyledProperty<bool> IsPausedProperty =
        AvaloniaProperty.Register<ThinkingOrb, bool>(nameof(IsPaused));

    public OrbState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public OrbSize Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public OrbTheme Theme
    {
        get => GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    public double Speed
    {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public bool IsPaused
    {
        get => GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    // --- the shared clock -------------------------------------------------
    // All instances read one stopwatch, so several orbs on screen stay in
    // phase, and one timer drives them all.

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly List<ThinkingOrb> Live = [];
    private static DispatcherTimer? _timer;

    /// <summary>
    /// Pin the shared clock to a fixed instant. Set this to make renders
    /// reproducible — snapshot captures, golden frames, screenshots in docs —
    /// and back to null to resume.
    /// </summary>
    public static double? FrozenTime { get; set; }

    /// <summary>Seconds on the shared clock — the argument a mode frame takes, before the speed multiplier.</summary>
    public static double Now => FrozenTime ?? Clock.Elapsed.TotalSeconds;

    private double? _frozenAt;

    static ThinkingOrb()
    {
        AffectsRender<ThinkingOrb>(StateProperty, SizeProperty, ThemeProperty, SpeedProperty);
        AffectsMeasure<ThinkingOrb>(SizeProperty);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPausedProperty)
        {
            _frozenAt = IsPaused ? Now : null;
            InvalidateVisual();
        }
        else if (change.Property == StateProperty)
        {
            AutomationProperties.SetName(this, Presets.Labels[State]);
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AutomationProperties.SetName(this, Presets.Labels[State]);
        ActualThemeVariantChanged += OnThemeChanged;
        Live.Add(this);
        if (_timer != null || ReducedMotion) return;
        // ponytail: a plain timer, not vsync-locked. Swap in the compositor's
        // frame callback if the ~16ms cadence ever shows as judder.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, Tick);
        _timer.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ActualThemeVariantChanged -= OnThemeChanged;
        Live.Remove(this);
        if (Live.Count != 0) return;
        _timer?.Stop();
        _timer = null;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private static void Tick(object? sender, EventArgs e)
    {
        foreach (var orb in Live)
            if (orb.IsEffectivelyVisible && !orb.IsPaused)
                orb.InvalidateVisual();
    }

    /// <inheritdoc />
    protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize) =>
        new((int)Size, (int)Size);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var size = (int)Size;
        var (mode, baseSpeed, opts) = Presets.Resolve(State, Size);

        // reduced motion -> one static, deterministic frame (raw 0.6s, not
        // scaled by speed — same instant the web component freezes on)
        var t = ReducedMotion ? 0.6 : (_frozenAt ?? Now) * baseSpeed * Speed;
        var frame = Modes.Frames[mode](size, t, opts);

        var dark = Theme switch
        {
            OrbTheme.Dark => true,
            OrbTheme.Light => false,
            _ => ActualThemeVariant == ThemeVariant.Dark
        };

        // lines first, so nodes sit on top of their edges
        foreach (var l in frame.Lines)
            context.DrawLine(new ImmutablePen(Ink(l.White, l.A, dark), l.W),
                new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));

        foreach (var d in frame.Dots)
            context.DrawEllipse(Ink(d.White, d.A, dark), null, new Point(d.X, d.Y), d.R, d.R);
    }

    // Matte grayscale ink. On dark substrates the value is mirrored
    // (1 - white) so near dots read bright — the same depth language on an
    // inverted substrate.
    private static readonly Dictionary<int, ImmutableSolidColorBrush> Brushes = [];

    private static ImmutableSolidColorBrush Ink(double white, double alpha, bool dark)
    {
        var w = Math.Min(1, Math.Max(0, white));
        var g = (byte)Engine.JsRound((dark ? 1 - w : w) * 255);
        var a = (byte)Engine.JsRound(Math.Min(1, Math.Max(0, alpha)) * 255);
        var key = (a << 8) | g;
        if (Brushes.TryGetValue(key, out var brush)) return brush;
        brush = new ImmutableSolidColorBrush(Color.FromArgb(a, g, g, g));
        Brushes[key] = brush;
        return brush;
    }

    // --- reduced motion ---------------------------------------------------

    private static bool? _reducedMotion;

    /// <summary>
    /// Whether the OS asks for reduced motion. Windows reports it through
    /// SPI_GETCLIENTAREAANIMATION; elsewhere we animate.
    /// </summary>
    public static bool ReducedMotion => _reducedMotion ??= DetectReducedMotion();

    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint param, ref int pv, uint winIni);

    private static bool DetectReducedMotion()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var animate = 1;
            return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, ref animate, 0) && animate == 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
