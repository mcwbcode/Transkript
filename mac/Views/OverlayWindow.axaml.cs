using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace Transkript.Views;

public partial class OverlayWindow : Window
{
    private const int    Bars    = 20;
    private const double BarW    = 4;
    private const double BarGap  = 3;
    private const double CanvasH = 30;
    private const double MinH    = 2.5;
    private const int    Fps     = 60;

    private readonly Rectangle[]    _bars     = new Rectangle[Bars];
    private readonly double[]       _currentH = new double[Bars];
    private readonly DispatcherTimer _timer;
    private DateTime _startTime;

    public Func<float[]>? GetLevels;

    public OverlayWindow()
    {
        InitializeComponent();
        PlaceAtBottomCenter();
        CreateBars();

        for (int i = 0; i < Bars; i++)
            _currentH[i] = IdleTarget(i, 0);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / Fps) };
        _timer.Tick += OnTick;
    }

    // ── ObjC P/Invoke for window level ───────────────────────────────────────
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr obj, IntPtr sel);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidI(IntPtr obj, IntPtr sel, nint a);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    private const nint NSStatusWindowLevel = 25;

    private void ForceToFront()
    {
        var handle = TryGetPlatformHandle();
        if (handle == null) return;
        IntPtr nsWindow = handle.Handle;
        SendVoidI(nsWindow, Sel("setLevel:"), NSStatusWindowLevel);
        Send(nsWindow, Sel("orderFrontRegardless"));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void ShowOverlay()
    {
        PlaceAtBottomCenter();
        _startTime = DateTime.UtcNow;
        Show();
        ForceToFront();
        Pill.Opacity = 1;
        _timer.Start();
    }

    public void HideOverlay()
    {
        _timer.Stop();
        Pill.Opacity = 0;

        // Wait for fade-out transition, then hide
        var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); Hide(); };
        hideTimer.Start();
    }

    // ── Animation frame ───────────────────────────────────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        float[]? levels = GetLevels?.Invoke();
        double   t      = (DateTime.UtcNow - _startTime).TotalSeconds;

        for (int i = 0; i < Bars; i++)
        {
            double target = ResolveTarget(i, t, levels);
            double lerp   = target > _currentH[i] ? 0.40 : 0.18;
            _currentH[i] += (target - _currentH[i]) * lerp;

            double h    = Math.Max(MinH, _currentH[i]);
            double norm = Math.Clamp((h - MinH) / (CanvasH * 0.7 - MinH), 0, 1);
            _bars[i].Opacity = 0.40 + 0.60 * norm;
            _bars[i].Height  = h;
            Canvas.SetTop(_bars[i], (CanvasH - h) / 2.0);
        }
    }

    private static double ResolveTarget(int i, double t, float[]? levels)
    {
        bool hasSignal = levels != null && i < levels.Length && levels[i] > 0.015f;
        if (hasSignal)
            return Math.Clamp(levels![i] * CanvasH * 1.15, MinH, CanvasH);
        return IdleTarget(i, t);
    }

    private static double IdleTarget(int i, double t)
    {
        double phase = (double)i / (Bars - 1) * Math.PI * 2.0;
        double wave1 = (Math.Sin(t * 3.2 - phase) + 1.0) / 2.0;
        double wave2 = (Math.Sin(t * 5.1 - phase * 1.4) + 1.0) / 4.0;
        double amplitude = (wave1 + wave2) / 1.5;
        amplitude = amplitude * amplitude;
        double bellEnv = Math.Sin(Math.PI * i / (Bars - 1));
        double maxH    = CanvasH * (0.35 + 0.30 * bellEnv);
        return MinH + amplitude * (maxH - MinH);
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

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
        var screen = Screens.Primary;
        if (screen == null) return;

        var wa      = screen.WorkingArea;
        double scale = screen.Scaling;
        int winPxW  = (int)(Width  * scale);
        int winPxH  = (int)(Height * scale);

        Position = new PixelPoint(
            wa.X + (wa.Width  - winPxW) / 2,
            wa.Y +  wa.Height - winPxH - (int)(24 * scale));
    }
}
