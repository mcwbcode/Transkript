using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Transkript;

public partial class OverlayWindow : Window
{
    // ── Constants ────────────────────────────────────────────────────────────

    private const int    Bars     = 20;
    private const double BarW     = 4;
    private const double BarGap   = 3;
    private const double CanvasH  = 30;
    private const double MinH     = 2.5;
    private const int    Fps      = 60;

    // ── State ────────────────────────────────────────────────────────────────

    private readonly Rectangle[]    _bars          = new Rectangle[Bars];
    private readonly double[]       _currentH      = new double[Bars];
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch      _sw            = new();

    public Func<float[]>? GetLevels;

    // ── Constructor ──────────────────────────────────────────────────────────

    public OverlayWindow()
    {
        InitializeComponent();
        PlaceAtBottomCenter();
        CreateBars();

        // Initialize heights to idle baseline so first frame is smooth
        for (int i = 0; i < Bars; i++)
            _currentH[i] = IdleTarget(i, 0);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / Fps)
        };
        _timer.Tick += OnTick;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void ShowOverlay()
    {
        PlaceAtBottomCenter();
        _sw.Restart();
        Show();

        Pill.Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Pill.BeginAnimation(OpacityProperty, fade);

        _timer.Start();
    }

    public void HideOverlay()
    {
        _timer.Stop();
        _sw.Stop();

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => Hide();
        Pill.BeginAnimation(OpacityProperty, fade);
    }

    // ── Frame update ─────────────────────────────────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        float[]? levels = GetLevels?.Invoke();
        double   t      = _sw.Elapsed.TotalSeconds;

        for (int i = 0; i < Bars; i++)
        {
            // Target height: audio-driven or idle wave
            double target = ResolveTarget(i, t, levels);

            // Asymmetric lerp: snap up fast, fall slowly (organic VU feel)
            double lerp     = target > _currentH[i] ? 0.40 : 0.18;
            _currentH[i]   += (target - _currentH[i]) * lerp;

            double h = Math.Max(MinH, _currentH[i]);

            // Opacity: dim at rest, bright when active (mirrors CSS opacity .5→1)
            double norm      = Math.Clamp((h - MinH) / (CanvasH * 0.7 - MinH), 0, 1);
            _bars[i].Opacity = 0.40 + 0.60 * norm;

            _bars[i].Height  = h;
            Canvas.SetTop(_bars[i], (CanvasH - h) / 2.0);
        }
    }

    // ── Target resolution ────────────────────────────────────────────────────

    private static double ResolveTarget(int i, double t, float[]? levels)
    {
        bool hasSignal = levels != null && i < levels.Length && levels[i] > 0.015f;

        if (hasSignal)
        {
            // Real audio level — scale to canvas, slight boost for visual impact
            return Math.Clamp(levels![i] * CanvasH * 1.15, MinH, CanvasH);
        }

        return IdleTarget(i, t);
    }

    /// <summary>
    /// Travelling sine wave identical in feel to the CSS animation on the website.
    /// Two overlapping waves with different speeds create the organic ripple.
    /// </summary>
    private static double IdleTarget(int i, double t)
    {
        double phase = (double)i / (Bars - 1) * Math.PI * 2.0;

        // Slow base wave + faster harmonic = richer motion
        double wave1 = (Math.Sin(t * 3.2 - phase) + 1.0) / 2.0;      // 0→1
        double wave2 = (Math.Sin(t * 5.1 - phase * 1.4) + 1.0) / 4.0; // 0→0.5

        double amplitude = (wave1 + wave2) / 1.5; // 0→1, normalized
        amplitude = amplitude * amplitude;          // quadratic → sharper peaks

        // Middle bars slightly taller at rest (bell curve envelope)
        double bellEnv = Math.Sin(Math.PI * i / (Bars - 1));
        double maxH    = CanvasH * (0.35 + 0.30 * bellEnv);

        return MinH + amplitude * (maxH - MinH);
    }

    // ── Setup ────────────────────────────────────────────────────────────────

    private void CreateBars()
    {
        for (int i = 0; i < Bars; i++)
        {
            var rect = new Rectangle
            {
                Width   = BarW,
                Height  = MinH,
                Fill    = new SolidColorBrush(Colors.White),
                RadiusX = 2,
                RadiusY = 2,
                Opacity = 0.40
            };
            Canvas.SetLeft(rect, i * (BarW + BarGap));
            Canvas.SetTop(rect, (CanvasH - MinH) / 2.0);
            WaveCanvas.Children.Add(rect);
            _bars[i] = rect;
        }
    }

    private void PlaceAtBottomCenter()
    {
        var w = SystemParameters.WorkArea;
        Left = w.Left + (w.Width  - Width)  / 2;
        Top  = w.Top  +  w.Height - Height  - 24;
    }
}
