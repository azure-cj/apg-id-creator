// The three things that sit on or around the card preview and take the
// mouse: dragging the photo, dragging the design's boxes, and dropping a
// picture file. None of them decides anything; each reports what the
// operator did and the window passes it on.

using IdMaker.Core;

namespace IdMaker.Mac;

/// <summary>Lies over the card preview: drag to move the photo, scroll to resize it.</summary>
internal sealed class PhotoDragView : NSView
{
    private CGPoint? _start;
    private double _scrolled;

    /// <summary>May the photo be moved right now?</summary>
    public Func<bool> CanDrag { get; set; } = () => false;
    public Action Began { get; set; } = () => { };
    /// <summary>How far the pointer is from where the drag began, in points.</summary>
    public Action<double, double> Dragged { get; set; } = (_, _) => { };
    public Action Ended { get; set; } = () => { };
    /// <summary>One step larger (true) or smaller.</summary>
    public Action<bool> Wheel { get; set; } = _ => { };

    public override bool IsFlipped => true;

    public override void MouseDown(NSEvent theEvent)
    {
        if (!CanDrag()) { base.MouseDown(theEvent); return; }
        _start = ConvertPointFromView(theEvent.LocationInWindow, null);
        Began();
    }

    public override void MouseDragged(NSEvent theEvent)
    {
        if (_start is not { } start) { base.MouseDragged(theEvent); return; }
        var now = ConvertPointFromView(theEvent.LocationInWindow, null);
        Dragged(now.X - start.X, now.Y - start.Y);
    }

    public override void MouseUp(NSEvent theEvent)
    {
        if (_start is null) { base.MouseUp(theEvent); return; }
        _start = null;
        Ended();
    }

    public override void ScrollWheel(NSEvent theEvent)
    {
        if (!CanDrag()) { base.ScrollWheel(theEvent); return; }
        // A wheel sends whole clicks; a trackpad sends a stream of small moves, which add up to one step every ten points.
        _scrolled += theEvent.HasPreciseScrollingDeltas ? theEvent.ScrollingDeltaY : theEvent.ScrollingDeltaY * 10;
        while (Math.Abs(_scrolled) >= 10)
        {
            Wheel(_scrolled > 0);
            _scrolled -= Math.Sign(_scrolled) * 10;
        }
    }
}

/// <summary>A part of the window a picture file can be dropped on.</summary>
internal sealed class FileDropView : NSView
{
    private const string FileUrl = "public.file-url";

    public Action<string> Dropped { get; set; } = _ => { };

    public FileDropView(NSView content)
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        AddSubview(content);
        NSLayoutConstraint.ActivateConstraints(new[]
        {
            content.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            content.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            content.TopAnchor.ConstraintEqualTo(TopAnchor),
            content.BottomAnchor.ConstraintEqualTo(BottomAnchor),
        });
        RegisterForDraggedTypes(new[] { FileUrl });
    }

    private static string? PathOn(INSDraggingInfo sender)
    {
        var address = sender.DraggingPasteboard.GetStringForType(FileUrl);
        return address is { Length: > 0 } && NSUrl.FromString(address) is { IsFileUrl: true, Path: { Length: > 0 } path } ? path : null;
    }

    public override NSDragOperation DraggingEntered(INSDraggingInfo sender) =>
        PathOn(sender) is null ? NSDragOperation.None : NSDragOperation.Copy;

    public override bool PerformDragOperation(INSDraggingInfo sender)
    {
        if (PathOn(sender) is not { } path) return false;
        Dropped(path);
        return true;
    }
}

/// <summary>
/// Dragging the placeholders. An overlay exactly on top of the card
/// preview, one box per placeholder: drag the middle to move it, the
/// corner to resize it; the card underneath redraws as it goes.
/// Everything is held in millimetres, the unit the card is printed in.
/// The keyboard works too: Tab goes from box to box, the arrow keys move
/// the selected one a quarter of a millimetre, Shift and an arrow key
/// resize it.
/// </summary>
internal sealed class MapEditorView : NSView
{
    private const double NudgeMm = 0.25;
    private const double Grip = 16;

    private static readonly (BoxKey Key, string Label)[] Placeholders =
    {
        (BoxKey.Photo, "Photo"), (BoxKey.Name, "Name"), (BoxKey.Title, "Job title"),
        (BoxKey.EmployeeNo, "Number"), (BoxKey.Signature, "Signature"), (BoxKey.Qr, "QR"),
    };

    private readonly Dictionary<BoxKey, NSTextField> _tags = new();
    private (BoxKey Key, bool Sizing, CGPoint Start, RectMm Box)? _drag;

    public TemplateStore? Store { get; set; }
    public BoxKey Selected { get; private set; } = BoxKey.Photo;

    /// <summary>A box was moved or resized (true), or only selected (false).</summary>
    public event Action<bool>? Changed;

    public MapEditorView()
    {
        TranslatesAutoresizingMaskIntoConstraints = false;
        foreach (var (key, label) in Placeholders)
        {
            var tag = NSTextField.CreateLabel(label);
            tag.DrawsBackground = true;
            tag.BackgroundColor = NSColor.Black.ColorWithAlphaComponent(0.75f);
            tag.TextColor = NSColor.White;
            if (NSFont.SystemFontOfSize(11) is { } font) tag.Font = font;
            tag.SizeToFit();
            AddSubview(tag);
            _tags[key] = tag;
        }
        Ui.Says(this, "The card’s boxes. Tab goes from box to box; the arrow keys move the selected box, and Shift with an arrow key resizes it.");
    }

    public override bool IsFlipped => true;
    public override bool AcceptsFirstResponder() => true;

    private double Scale => Bounds.Width / CardConfig.CardWidthMm;         // points to the millimetre

    private CGRect RectOf(BoxKey key)
    {
        if (Store is null) return CGRect.Empty;
        var b = Store.Box(key);
        return new CGRect(b.X * Scale, b.Y * Scale, b.W * Scale, b.H * Scale);
    }

    /// <summary>Put every box back where the design says it is.</summary>
    public void Refresh()
    {
        foreach (var (key, tag) in _tags)
        {
            var r = RectOf(key);
            tag.SetFrameOrigin(new CGPoint(r.X + 2, r.Y + 2));
            tag.AlphaValue = key == Selected ? 1 : 0.8f;
        }
        NeedsDisplay = true;
    }

    /// <summary>What the selected placeholder is, and where: the panel's readout.</summary>
    public string Readout()
    {
        if (Store is null) return "";
        var b = Store.Box(Selected);
        var label = Placeholders.First(p => p.Key == Selected).Label;
        return $"{label}: {b.W:0.0} × {b.H:0.0} mm, {b.X:0.0} mm from the left and {b.Y:0.0} mm from the top.";
    }

    public override void DrawRect(CGRect dirtyRect)
    {
        var context = NSGraphicsContext.CurrentContext?.CGContext;
        if (context is null || Store is null) return;
        var gold = new CGColor(0.83f, 0.69f, 0.22f, 1);
        var wash = new CGColor(0.83f, 0.69f, 0.22f, 0.18f);
        foreach (var (key, _) in Placeholders)
        {
            var r = RectOf(key);
            // the photo window is not a rectangle on the built-in card: show the shape that will actually be cut
            if (key == BoxKey.Photo && Store.PhotoOutline() is { Count: > 2 } outline)
            {
                context.MoveTo((nfloat)(outline[0].X * Scale), (nfloat)(outline[0].Y * Scale));
                for (var i = 1; i < outline.Count; i++) context.AddLineToPoint((nfloat)(outline[i].X * Scale), (nfloat)(outline[i].Y * Scale));
                context.ClosePath();
                context.SetFillColor(wash);
                context.FillPath();
            }
            else
            {
                context.SetFillColor(wash);
                context.FillRect(r);
            }
            context.SetStrokeColor(gold);
            context.SetLineWidth(key == Selected ? 3 : 1.5f);
            context.StrokeRect(r);
            if (key == Selected)
            {
                context.SetFillColor(gold);
                context.FillRect(new CGRect(r.Right - Grip, r.Bottom - Grip, Grip, Grip));
            }
        }
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

    /// <summary>Move the selected box, or resize it, by this many millimetres: what an arrow key does, and what a drag adds up to.</summary>
    internal void Nudge(double dxMm, double dyMm, bool resize)
    {
        if (Store is null) return;
        var b = Store.Box(Selected);
        Set(Selected, resize ? b with { W = b.W + dxMm, H = b.H + dyMm } : b with { X = b.X + dxMm, Y = b.Y + dyMm });
    }

    internal void SelectNext(bool backwards)
    {
        var at = Array.FindIndex(Placeholders, p => p.Key == Selected);
        var next = (at + (backwards ? Placeholders.Length - 1 : 1)) % Placeholders.Length;
        Select(Placeholders[next].Key, moved: false);
    }

    // The photo window covers most of the card and the text boxes sit
    // inside it: the smallest box under the pointer is the one meant, so
    // every box stays reachable and bare card belongs to the photo.
    private BoxKey? Under(CGPoint point)
    {
        BoxKey? found = null;
        var smallest = double.MaxValue;
        foreach (var (key, _) in Placeholders)
        {
            var r = RectOf(key);
            if (!r.Contains(point) || r.Width * r.Height >= smallest) continue;
            smallest = r.Width * r.Height;
            found = key;
        }
        return found;
    }

    public override void MouseDown(NSEvent theEvent)
    {
        if (Store is null) return;
        Window?.MakeFirstResponder(this);
        var point = ConvertPointFromView(theEvent.LocationInWindow, null);
        var selected = RectOf(Selected);
        var onGrip = new CGRect(selected.Right - Grip, selected.Bottom - Grip, Grip, Grip).Contains(point);
        var key = onGrip ? Selected : Under(point);
        if (key is not { } grabbed) return;
        _drag = (grabbed, onGrip, point, Store.Box(grabbed));
        Select(grabbed, moved: false);
    }

    public override void MouseDragged(NSEvent theEvent)
    {
        if (_drag is not { } d) return;
        var now = ConvertPointFromView(theEvent.LocationInWindow, null);
        double dx = (now.X - d.Start.X) / Scale, dy = (now.Y - d.Start.Y) / Scale;
        Set(d.Key, d.Sizing
            ? d.Box with { W = d.Box.W + dx, H = d.Box.H + dy }
            : d.Box with { X = d.Box.X + dx, Y = d.Box.Y + dy });
    }

    public override void MouseUp(NSEvent theEvent) => _drag = null;

    public override void KeyDown(NSEvent theEvent)
    {
        var shift = theEvent.ModifierFlags.HasFlag(NSEventModifierMask.ShiftKeyMask);
        // the keys as the keyboard numbers them: Tab, then the four arrows
        switch (theEvent.KeyCode)
        {
            case 48: SelectNext(shift); break;
            case 123: Nudge(-NudgeMm, 0, shift); break;
            case 124: Nudge(NudgeMm, 0, shift); break;
            case 125: Nudge(0, NudgeMm, shift); break;
            case 126: Nudge(0, -NudgeMm, shift); break;
            default: base.KeyDown(theEvent); break;
        }
    }
}
