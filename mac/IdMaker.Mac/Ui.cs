// Small helpers for building the window in code. The window is drawn with
// AppKit's own controls and laid out by stack views; nothing here decides
// anything about the card.

namespace IdMaker.Mac;

internal static class Ui
{
    /// <summary>The widest a line of words may run before it wraps: the form's column. A tool drawer sets its own.</summary>
    public const double TextWidth = 408;

    /// <summary>The company's gold: the one button that moves the work forward, and the design's boxes.</summary>
    public static readonly NSColor Gold = NSColor.FromRgb(0.83f, 0.69f, 0.22f);

    public static NSTextField Label(string text, double size = 13, bool bold = false, bool muted = false, double maxWidth = TextWidth)
    {
        var label = NSTextField.CreateWrappingLabel(text);
        label.PreferredMaxLayoutWidth = (nfloat)maxWidth;
        if ((bold ? NSFont.BoldSystemFontOfSize((nfloat)size) : NSFont.SystemFontOfSize((nfloat)size)) is { } font) label.Font = font;
        if (muted) label.TextColor = NSColor.SecondaryLabel;
        label.Selectable = false;
        label.TranslatesAutoresizingMaskIntoConstraints = false;
        return label;
    }

    public static NSTextField Heading(string text) => Label(text, 20, bold: true);
    public static NSTextField Note(string text = "") => Label(text, 12, muted: true);

    public static NSTextField Error(string text = "")
    {
        var label = Label(text, 12);
        label.TextColor = NSColor.SystemRed;
        return label;
    }

    public static NSTextField Warning(string text = "")
    {
        var label = Label(text, 12);
        label.TextColor = NSColor.SystemOrange;
        return label;
    }

    public static NSTextField Field(string accessibilityLabel, double width = 0)
    {
        var field = new NSTextField { TranslatesAutoresizingMaskIntoConstraints = false, AccessibilityLabel = accessibilityLabel };
        if (NSFont.SystemFontOfSize(14) is { } font) field.Font = font;
        if (width > 0) field.WidthAnchor.ConstraintEqualTo((nfloat)width).Active = true;
        return field;
    }

    public static NSButton Button(string title, Action pressed, bool primary = false)
    {
        var button = new NSButton { Title = title, BezelStyle = NSBezelStyle.Rounded, TranslatesAutoresizingMaskIntoConstraints = false };
        button.SetButtonType(NSButtonType.MomentaryPushIn);
        // Return presses the button that moves the work forward, and it is the one gold thing on the screen, as on Windows.
        if (primary)
        {
            button.KeyEquivalent = "\r";
            button.BezelColor = Gold;
        }
        button.Activated += (_, _) => pressed();
        return button;
    }

    public static NSButton Check(string title, Action<bool> changed)
    {
        var box = new NSButton { Title = title, TranslatesAutoresizingMaskIntoConstraints = false };
        box.SetButtonType(NSButtonType.Switch);
        box.Activated += (_, _) => changed(box.State == NSCellStateValue.On);
        return box;
    }

    public static NSPopUpButton Popup(string accessibilityLabel, IEnumerable<string> items, Action<int> picked)
    {
        var popup = new NSPopUpButton(CGRect.Empty, false) { TranslatesAutoresizingMaskIntoConstraints = false };
        Says(popup, accessibilityLabel);
        foreach (var item in items) popup.AddItem(item);
        popup.Activated += (_, _) => picked((int)popup.IndexOfSelectedItem);
        return popup;
    }

    public static NSStackView Column(double spacing, params NSView[] views)
    {
        var stack = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = (nfloat)spacing,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        foreach (var view in views) stack.AddArrangedSubview(view);
        // as tall as what is in it and no taller: spare height belongs to a Spacer, not to a gap between two controls
        stack.SetHuggingPriority(750, NSLayoutConstraintOrientation.Vertical);
        return stack;
    }

    /// <summary>Takes whatever height a column has to spare, so the controls above it stay together.</summary>
    public static NSView Spacer()
    {
        var spacer = new NSView { TranslatesAutoresizingMaskIntoConstraints = false };
        spacer.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Vertical);
        return spacer;
    }

    public static NSStackView Row(double spacing, params NSView[] views)
    {
        var stack = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Alignment = NSLayoutAttribute.CenterY,
            Spacing = (nfloat)spacing,
            TranslatesAutoresizingMaskIntoConstraints = false,
        };
        foreach (var view in views) stack.AddArrangedSubview(view);
        return stack;
    }

    public static NSImageView Picture(double width, double height)
    {
        var view = new NSImageView { ImageScaling = NSImageScale.ProportionallyUpOrDown, TranslatesAutoresizingMaskIntoConstraints = false };
        if (width > 0) view.WidthAnchor.ConstraintEqualTo((nfloat)width).Active = true;
        if (height > 0) view.HeightAnchor.ConstraintEqualTo((nfloat)height).Active = true;
        return view;
    }

    /// <summary>What VoiceOver reads for a control whose title alone does not say what it does.</summary>
    public static void Says(NSView view, string label) => view.AccessibilityLabel = label;

    public static void Show(NSView view, bool visible)
    {
        if (view.Hidden == visible) view.Hidden = !visible;
    }

    /// <summary>Write a text only when it differs, so a box the operator is typing in keeps its cursor when nothing changed.</summary>
    public static void SetText(NSTextField label, string text)
    {
        if (label.StringValue != text) label.StringValue = text;
    }

    public static NSImage ToNsImage(IdMaker.Core.RgbaImage image, double pointsWide)
    {
        using var picture = MacPictures.ToImage(image);
        return new NSImage(picture, new CGSize(pointsWide, pointsWide * image.Height / image.Width));
    }

    /// <summary>A picture of a view as it is drawn now, for the window test's report.</summary>
    public static void Snapshot(NSView view, string path)
    {
        view.LayoutSubtreeIfNeeded();
        var rep = view.BitmapImageRepForCachingDisplayInRect(view.Bounds);
        if (rep is null) return;
        view.CacheDisplay(view.Bounds, rep);
        var png = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
        png?.Save(NSUrl.FromFilename(path), true);
    }
}
