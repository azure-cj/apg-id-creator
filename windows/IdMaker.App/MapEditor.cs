// Dragging the placeholders.
//
// An overlay that sits exactly on top of the card preview, with one handle
// per placeholder. Drag the middle to move it, the corner to resize it; the
// card underneath redraws as it goes, so the design is checked against the
// real thing rather than against a wireframe.
//
// Everything is held in millimetres — the unit the card is printed in. The
// overlay is always drawn 10 units to the millimetre and scaled with the
// card, so a layout dragged on a laptop prints the same as one dragged on a
// large monitor.
//
// Keyboard works too: Tab to a handle, arrow keys move it, Shift and arrow
// keys resize it. Nudging by a quarter of a millimetre is easier typed than
// dragged.

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IdMaker.Core;

namespace IdMaker.App;

public sealed class MapEditor : Canvas
{
    public const double UnitsPerMm = 10;
    private const double NudgeMm = 0.25;
    private const double GripSize = 22;

    private static readonly (BoxKey Key, string Label)[] Placeholders =
    {
        (BoxKey.Photo, "Photo"), (BoxKey.Name, "Name"), (BoxKey.Title, "Job title"),
        (BoxKey.EmployeeNo, "Number"), (BoxKey.Signature, "Signature"), (BoxKey.Qr, "QR"),
    };

    private sealed record Handle(Border Frame, Polygon Shape, Border Grip, Border Tag);

    private readonly Dictionary<BoxKey, Handle> _handles = new();
    private (BoxKey Key, bool Sizing, Point Start, RectMm Box)? _drag;

    public TemplateStore? Store { get; set; }
    public BoxKey Selected { get; private set; } = BoxKey.Photo;

    /// <summary>A box was moved or resized (true), or only selected (false).</summary>
    public event Action<bool>? Changed;

    public MapEditor()
    {
        Background = Brushes.Transparent;
        foreach (var (key, label) in Placeholders)
        {
            var shape = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x2E, 0xD4, 0xAF, 0x37)), IsHitTestVisible = false };
            var tagText = new TextBlock { Text = label, FontSize = 13 };
            Theme.Paint(tagText, TextBlock.ForegroundProperty, "Color.TrayInk");
            var tag = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Child = tagText,
            };
            Theme.Paint(tag, Border.BackgroundProperty, "Color.Tray");
            var grip = new Border
            {
                Width = GripSize, Height = GripSize,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE,
            };
            Theme.Paint(grip, Border.BackgroundProperty, "Color.AccentSolid");
            Theme.Paint(grip, Border.BorderBrushProperty, "Color.Tray");
            var inner = new Grid();
            inner.Children.Add(shape);
            inner.Children.Add(tag);
            inner.Children.Add(grip);
            var frame = new Border
            {
                BorderThickness = new Thickness(2), Background = Brushes.Transparent,
                Focusable = true, Cursor = Cursors.SizeAll, Child = inner, FocusVisualStyle = null,
            };
            Theme.Paint(frame, Border.BorderBrushProperty, "Color.AccentSolid");
            AutomationProperties.SetName(frame, $"{label} box. Arrow keys move it; Shift and arrow keys resize it.");
            var k = key;
            frame.MouseLeftButtonDown += (_, e) => Grab(k, e);
            frame.MouseMove += (_, e) => Drag(e);
            frame.MouseLeftButtonUp += (_, _) => Release();
            frame.LostMouseCapture += (_, _) => _drag = null;
            frame.KeyDown += (_, e) => Key(k, e);
            frame.GotKeyboardFocus += (_, _) => Select(k, moved: false);
            Children.Add(frame);
            _handles[key] = new Handle(frame, shape, grip, tag);
        }
    }

    /// <summary>
    /// Put every handle back where the design says it is.
    ///
    /// The photo window covers most of the card and the text boxes sit
    /// inside it, so the order they stack in is not cosmetic: whichever is
    /// on top is the only one that can be grabbed. Smallest on top means
    /// every handle stays reachable, and a drag on bare card moves the photo
    /// window, which is the one thing bare card belongs to.
    /// </summary>
    public void Refresh()
    {
        if (Store is null) return;
        var order = new List<(Handle Handle, double Area)>();
        foreach (var (key, handle) in _handles)
        {
            var b = Store.Box(key);
            SetLeft(handle.Frame, b.X * UnitsPerMm);
            SetTop(handle.Frame, b.Y * UnitsPerMm);
            handle.Frame.Width = b.W * UnitsPerMm;
            handle.Frame.Height = b.H * UnitsPerMm;
            var on = key == Selected;
            handle.Frame.BorderThickness = new Thickness(on ? 4 : 2);
            handle.Grip.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            handle.Tag.Opacity = on ? 1 : 0.85;

            // the photo window is not a rectangle on the built-in card: show
            // the shape that will actually be cut, not just its bounding box
            handle.Shape.Points.Clear();
            if (key == BoxKey.Photo)
            {
                foreach (var p in Store.PhotoOutline()) handle.Shape.Points.Add(new Point((p.X - b.X) * UnitsPerMm, (p.Y - b.Y) * UnitsPerMm));
            }
            order.Add((handle, b.W * b.H));
        }
        var z = 1;
        foreach (var (handle, _) in order.OrderByDescending(o => o.Area)) SetZIndex(handle.Frame, z++);
    }

    /// <summary>What the selected placeholder is, and where: the panel's readout.</summary>
    public string Readout()
    {
        if (Store is null) return "";
        var b = Store.Box(Selected);
        var label = Placeholders.First(p => p.Key == Selected).Label;
        return $"{label}: {b.W:0.0} × {b.H:0.0} mm, {b.X:0.0} mm from the left and {b.Y:0.0} mm from the top.";
    }

    private void Select(BoxKey key, bool moved)
    {
        Selected = key;
        Refresh();
        Changed?.Invoke(moved);
    }

    private void Set(BoxKey key, RectMm box)
    {
        Store?.SetBox(key, box);
        Select(key, moved: true);
    }

    private void Grab(BoxKey key, MouseButtonEventArgs e)
    {
        if (Store is null) return;
        var handle = _handles[key];
        var sizing = e.OriginalSource is DependencyObject d && IsInside(d, handle.Grip);
        _drag = (key, sizing, e.GetPosition(this), Store.Box(key));
        handle.Frame.Focus();
        handle.Frame.CaptureMouse();
        Select(key, moved: false);
        e.Handled = true;
    }

    private static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element)) if (ReferenceEquals(element, ancestor)) return true;
        return false;
    }

    private void Drag(MouseEventArgs e)
    {
        if (_drag is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
        var now = e.GetPosition(this);
        double dx = (now.X - d.Start.X) / UnitsPerMm, dy = (now.Y - d.Start.Y) / UnitsPerMm;
        Set(d.Key, d.Sizing
            ? d.Box with { W = d.Box.W + dx, H = d.Box.H + dy }
            : d.Box with { X = d.Box.X + dx, Y = d.Box.Y + dy });
    }

    private void Release()
    {
        if (_drag is not { } d) return;
        _drag = null;
        _handles[d.Key].Frame.ReleaseMouseCapture();
    }

    private void Key(BoxKey key, KeyEventArgs e)
    {
        if (Store is null) return;
        (double X, double Y)? step = e.Key switch
        {
            System.Windows.Input.Key.Left => (-NudgeMm, 0),
            System.Windows.Input.Key.Right => (NudgeMm, 0),
            System.Windows.Input.Key.Up => (0, -NudgeMm),
            System.Windows.Input.Key.Down => (0, NudgeMm),
            _ => null,
        };
        if (step is not { } s) return;
        e.Handled = true;
        var b = Store.Box(key);
        Set(key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? b with { W = b.W + s.X, H = b.H + s.Y }
            : b with { X = b.X + s.X, Y = b.Y + s.Y });
    }
}
