// Signing.
//
// SignaturePad is the surface: it captures strokes from a pen, a finger or
// a mouse and draws them live with the same curve code the card uses.
// SignWindow is where signing happens: the pad on its own, filling the
// screen, with the pointer kept inside the window until the person is done,
// and on the pad itself once they start to sign.
// A small tablet maps onto the whole screen, so on a pad tucked into a
// corner of the form the pen kept running off the edge; filling the screen
// gives the hand the whole tablet to sign on.
//
// Strokes are kept in pad units: the pad is always 1000 units wide and
// 1000 / 2.6 high, however large it is drawn, so a signature made on a big
// screen is the same signature on a small one.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using IdMaker.Core;

namespace IdMaker.App;

public sealed class SignaturePad : FrameworkElement
{
    public const double Units = 1000;                   // the pad's width in its own units
    private const double MinInkUnits = 2.5;             // line width at the lightest touch (or the fastest stroke)
    private const double MaxInkUnits = 7.8;
    private const double JitterPx = 1.5;                // movements smaller than this are the hand, not the signature

    private readonly List<List<InkPoint>> _strokes = new();
    private List<InkPoint>? _current;
    private bool _stylusStroke;

    public static readonly Brush PaperBrush = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static readonly Color InkColor = Color.FromRgb(0x1D, 0x1C, 0x1A);
    public static readonly Brush InkBrush = Frozen(InkColor);
    private static readonly Brush LineBrush = Frozen(Color.FromRgb(0xC9, 0xC4, 0xB8));
    private static readonly Brush HintBrush = Frozen(Color.FromRgb(0x66, 0x62, 0x5A));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public SignaturePad()
    {
        Focusable = false;
        ClipToBounds = true;
        Cursor = Cursors.Pen;
        AutomationProperties.SetName(this, "Signature pad. Sign with the pen, a finger or the mouse.");
    }

    /// <summary>When false the pad only shows a signature; it cannot be drawn on.</summary>
    public bool CanDraw { get; set; } = true;

    /// <summary>The tablet driver, for pen pressure when the pen arrives as a mouse. May be null.</summary>
    public PenTablet? Tablet { get; set; }

    /// <summary>The words shown on an empty pad.</summary>
    public string Hint { get; set; } = "Sign here";

    /// <summary>Raised after every finished stroke, and after Clear.</summary>
    public event Action? InkChanged;

    /// <summary>Raised when a pen, finger or mouse comes down on the pad to draw.</summary>
    public event Action? StrokeStarted;

    /// <summary>True once any stroke was drawn with a device that reports pressure.</summary>
    public bool SawPressure { get; private set; }

    public bool HasInk => _strokes.Any(s => s.Count > 0);

    public DrawnSignature? Value
    {
        get => HasInk ? new DrawnSignature(_strokes.Select(s => (IReadOnlyList<InkPoint>)s.ToList()).ToList()) : null;
        set
        {
            _strokes.Clear();
            _current = null;
            if (value is not null) _strokes.AddRange(FitToPad(value).Strokes.Select(s => s.ToList()));
            InvalidateVisual();
        }
    }

    public void Clear()
    {
        _strokes.Clear();
        _current = null;
        ReleaseMouseCapture();
        ReleaseStylusCapture();
        InvalidateVisual();
        InkChanged?.Invoke();
    }

    // Strokes from somewhere else (the example, an older pad size) are
    // scaled to sit inside the pad; strokes made here already do.
    private static DrawnSignature FitToPad(DrawnSignature sig)
    {
        double maxX = 1, maxY = 1;
        foreach (var s in sig.Strokes) foreach (var p in s) { maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y); }
        var padH = Units / SignatureInk.PadAspect;
        if (maxX <= Units && maxY <= padH) return sig;
        return SignatureInk.Rescale(sig, Math.Min(Units * 0.9 / maxX, padH * 0.9 / maxY));
    }

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 360 : available.Width;
        var h = w / SignatureInk.PadAspect;
        if (!double.IsInfinity(available.Height) && h > available.Height) { h = available.Height; w = h * SignatureInk.PadAspect; }
        return new Size(w, h);
    }

    private double Scale => ActualWidth / Units;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(PaperBrush, null, bounds);
        if (ActualWidth <= 0) return;

        // the line to sign on
        var y = ActualHeight * 0.74;
        var pen = new Pen(LineBrush, Math.Max(1, ActualHeight / 180));
        pen.Freeze();
        dc.DrawLine(pen, new Point(ActualWidth * 0.08, y), new Point(ActualWidth * 0.92, y));

        var all = _current is null ? _strokes : _strokes.Append(_current).ToList();
        if (!all.Any(s => s.Count > 0))
        {
            var text = new FormattedText(Hint, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), Math.Max(13, ActualHeight / 9), HintBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, y - text.Height - ActualHeight * 0.06));
            return;
        }
        var drawn = new DrawnSignature(all.Select(s => (IReadOnlyList<InkPoint>)s).ToList());
        CardRenderer.DrawInk(dc, SignatureInk.Segments(drawn, MinInkUnits, MaxInkUnits), InkBrush, 0, 0, Scale, Scale);
    }

    /* ---- strokes -------------------------------------------------- */

    private InkPoint ToPad(Point p, double pressure) => new(
        Math.Clamp(p.X, 0, ActualWidth) / Scale,
        Math.Clamp(p.Y, 0, ActualHeight) / Scale,
        pressure);

    private void Begin(Point p, double pressure)
    {
        _current = new List<InkPoint> { ToPad(p, pressure) };
        if (pressure >= 0) SawPressure = true;
        InvalidateVisual();
        StrokeStarted?.Invoke();
    }

    private void Extend(Point p, double pressure)
    {
        if (_current is null) return;
        var next = ToPad(p, pressure);
        var last = _current[^1];
        var moved = Math.Sqrt((next.X - last.X) * (next.X - last.X) + (next.Y - last.Y) * (next.Y - last.Y)) * Scale;
        if (moved < JitterPx)
        {
            // standing still but pressing harder still thickens the line
            if (pressure > last.Pressure) _current[^1] = last with { Pressure = pressure };
            return;
        }
        _current.Add(next);
        InvalidateVisual();
    }

    private void End()
    {
        if (_current is null) return;
        _strokes.Add(_current);
        _current = null;
        InvalidateVisual();
        InkChanged?.Invoke();
    }

    /* ---- pen and finger (Windows Ink) ------------------------------ */

    private static double PressureOf(StylusPoint p) =>
        p.Description.HasProperty(StylusPointProperties.NormalPressure) ? p.PressureFactor : -1;

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        if (!CanDraw) return;
        var points = e.GetStylusPoints(this);
        if (points.Count == 0) return;
        _stylusStroke = true;
        CaptureStylus();
        Begin(points[0].ToPoint(), PressureOf(points[0]));
        for (var i = 1; i < points.Count; i++) Extend(points[i].ToPoint(), PressureOf(points[i]));
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        if (_current is null || !_stylusStroke) return;
        // every packet since the last event, not just the latest: a fast pen reports far more often than the screen redraws
        foreach (var p in e.GetStylusPoints(this)) Extend(p.ToPoint(), PressureOf(p));
        e.Handled = true;
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        if (!_stylusStroke) return;
        _stylusStroke = false;
        ReleaseStylusCapture();
        End();
        e.Handled = true;
    }

    protected override void OnLostStylusCapture(StylusEventArgs e)
    {
        if (_stylusStroke) { _stylusStroke = false; End(); }
    }

    /* ---- mouse, and a pen that arrives as a mouse ------------------- */

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!CanDraw || e.StylusDevice is not null || _stylusStroke) return;       // a pen already handled above
        CaptureMouse();
        Begin(e.GetPosition(this), Tablet?.CurrentPressure ?? -1);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_current is null || _stylusStroke || e.StylusDevice is not null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseMouseCapture(); End(); return; }
        Extend(e.GetPosition(this), Tablet?.CurrentPressure ?? -1);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_current is null || _stylusStroke) return;
        ReleaseMouseCapture();
        End();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (!_stylusStroke) End();
    }
}

/// <summary>The pad on its own, filling the screen.</summary>
public sealed class SignWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClipCursor(ref NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "ClipCursor")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReleaseCursor(IntPtr none);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    private readonly SignaturePad _pad = new() { Hint = "Sign here" };
    private readonly TextBlock _status = new();
    private readonly Button _done;
    private readonly Button _clear;
    private readonly Border _padFrame;
    private PenTablet? _tablet;
    private bool _holding;                                    // signing has started: the pointer is held on the pad

    /// <summary>What the status line says while the pointer is held on the pad.</summary>
    public const string HoldingStatus = "Signing. Press Enter when finished, Backspace to start again, or Esc to let the pointer go.";

    /// <summary>True while the pointer is held on the pad.</summary>
    public bool Holding => _holding;

    internal SignaturePad Pad => _pad;

    /// <summary>The signature, once Done was pressed. Null when signing was cancelled.</summary>
    public DrawnSignature? Result { get; private set; }

    public SignWindow(string? signerName, DrawnSignature? existing)
    {
        Title = "Sign";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        ShowInTaskbar = false;
        Theme.Paint(this, BackgroundProperty, "Color.Tray");
        FontFamily = new FontFamily("Segoe UI");
        UseLayoutRounding = true;

        var heading = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(signerName) ? "Sign here" : $"{signerName}, sign here",
            FontSize = 28, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _status.FontSize = 15;
        Theme.Paint(heading, TextBlock.ForegroundProperty, "Color.TrayInk");
        Theme.Paint(_status, TextBlock.ForegroundProperty, "Color.TrayMuted");
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.Margin = new Thickness(0, 8, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.TextAlignment = TextAlignment.Center;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _clear = MakeButton("Clear", "Button.OnTray", (_, _) => { _pad.Clear(); _pad.Focus(); });
        var cancel = MakeButton("Cancel", "Button.OnTray", (_, _) => Close());
        cancel.IsCancel = true;                                       // Escape
        _done = MakeButton("Use this signature", "Button.Primary", (_, _) => Finish());
        _done.IsDefault = true;                                       // Enter

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(_clear);
        buttons.Children.Add(cancel);
        buttons.Children.Add(_done);

        // the ring shows while the pointer is held on the pad; it is always there, clear, so the pad does not move when it appears
        _padFrame = new Border
        {
            Child = _pad, CornerRadius = new CornerRadius(14), Background = SignaturePad.PaperBrush,
            BorderThickness = new Thickness(3), BorderBrush = Brushes.Transparent,
            Padding = new Thickness(0), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
        top.Children.Add(heading);
        top.Children.Add(_status);

        var root = new DockPanel { Margin = new Thickness(48, 36, 48, 36), LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(_padFrame);
        Content = root;

        _pad.Value = existing;
        _pad.InkChanged += Refresh;
        _pad.StrokeStarted += HoldOnPad;
        PreviewKeyDown += KeysWhileHolding;
        PreviewMouseDown += ClicksWhileHolding;
        Refresh();

        SourceInitialized += (_, _) =>
        {
            _tablet = PenTablet.TryOpen(new WindowInteropHelper(this).Handle);
            _pad.Tablet = _tablet;
            Refresh();
        };
        Activated += (_, _) => { _tablet?.SetActive(true); KeepPointerInside(); };
        Deactivated += (_, _) => { _tablet?.SetActive(false); ReleaseCursor(IntPtr.Zero); };
        Closed += (_, _) => { ReleaseCursor(IntPtr.Zero); _tablet?.Dispose(); _tablet = null; };
        ContentRendered += (_, _) => KeepPointerInside();
        SizeChanged += (_, _) => KeepPointerInside();
    }

    private static Button MakeButton(string text, string style, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Style = Theme.Style(style), Margin = new Thickness(8, 0, 8, 0), MinWidth = 150, MinHeight = 52, FontSize = 17 };
        b.Click += click;
        return b;
    }

    // The pointer stays on this screen while signing: a pen that slides off
    // the pad cannot land on another window, or another monitor. Once the
    // pen has come down on the pad it stays on the pad itself: a tablet
    // whose driver acts as a mouse drifted off it mid-signature, and the
    // next tap pressed Cancel or Clear. The keyboard finishes from there.
    private void KeepPointerInside()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !IsActive) return;
        if (_holding && PadOnScreen() is { } pad) { ClipCursor(ref pad); return; }
        if (GetWindowRect(handle, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top) ClipCursor(ref rect);
    }

    // The pad's box in screen pixels, one pixel in from each edge so the pointer stays over it. Null when it has no size.
    private NativeRect? PadOnScreen()
    {
        if (_pad.ActualWidth <= 0 || _pad.ActualHeight <= 0 || PresentationSource.FromVisual(_pad) is null) return null;
        var from = _pad.PointToScreen(new Point(0, 0));                       // device pixels, whatever the scaling
        var to = _pad.PointToScreen(new Point(_pad.ActualWidth, _pad.ActualHeight));
        var rect = new NativeRect
        {
            // SAFETY: screen coordinates are a few thousand pixels at most, far inside int
            Left = (int)Math.Ceiling(Math.Min(from.X, to.X)) + 1,
            Top = (int)Math.Ceiling(Math.Min(from.Y, to.Y)) + 1,
            Right = (int)Math.Floor(Math.Max(from.X, to.X)) - 1,
            Bottom = (int)Math.Floor(Math.Max(from.Y, to.Y)) - 1,
        };
        return rect.Right > rect.Left && rect.Bottom > rect.Top ? rect : null;
    }

    private void HoldOnPad()
    {
        if (_holding) return;
        _holding = true;
        Theme.Paint(_padFrame, Border.BorderBrushProperty, "Color.TrayInk");
        Keyboard.ClearFocus();                                // so Enter and Space cannot press a focused button
        Refresh();
        KeepPointerInside();
    }

    private void LetGo()
    {
        if (!_holding) return;
        _holding = false;
        _padFrame.BorderBrush = Brushes.Transparent;
        Refresh();
        KeepPointerInside();
    }

    // While the pointer is held, only these keys count:
    //   Enter      use this signature
    //   Backspace  start again (Delete too)
    //   Esc        let the pointer go, and stay on this screen
    private void KeysWhileHolding(object sender, KeyEventArgs e)
    {
        if (!_holding) return;
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Enter when _pad.HasInk: Finish(); break;
            case Key.Back or Key.Delete: _pad.Clear(); break;
            case Key.Escape: LetGo(); break;
        }
    }

    // A touch or a pen that reaches past the pad while it is held presses nothing.
    private void ClicksWhileHolding(object sender, MouseButtonEventArgs e)
    {
        if (!_holding) return;
        var p = e.GetPosition(_pad);
        if (p.X < 0 || p.Y < 0 || p.X > _pad.ActualWidth || p.Y > _pad.ActualHeight) e.Handled = true;
    }

    private void Refresh()
    {
        _done.IsEnabled = _pad.HasInk;
        _clear.IsEnabled = _pad.HasInk;
        _status.Text = _holding ? HoldingStatus : TabletStatus(_tablet, _pad.SawPressure);
    }

    /// <summary>One line on what the pen tablet is doing, in words for the person holding it.</summary>
    public static string TabletStatus(PenTablet? tablet, bool sawPressure)
    {
        if (sawPressure || tablet is not null)
        {
            var name = tablet is { DeviceName.Length: > 0 } ? tablet.DeviceName : "Pen tablet";
            return $"{name} connected. Press harder for a thicker line.";
        }
        return PenTablet.DriverInstalled
            ? "Sign with the pen or the mouse. If the pen tablet is plugged in and nothing happens, unplug it and plug it back in."
            : "No pen tablet driver found, so sign with the mouse or a finger. The installer can add the XP-Pen driver.";
    }

    private void Finish()
    {
        Result = _pad.Value;
        _holding = false;
        Close();
    }
}
