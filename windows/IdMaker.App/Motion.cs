// The window's few movements: a tool sliding in over the card and out
// again, the card turning over, a step's page arriving from the side you
// are heading, the outline on the card gliding to the next field, and the
// print preview settling when its layout changes.
//
// Each one is short (under a quarter of a second), eases out, and moves
// only by a render transform or opacity, so nothing around it is laid out
// again: the card keeps its size and place while it turns, and the form
// does not shift when a tool opens. None of them runs when Windows' own
// "Animation effects" are off (Settings -> Accessibility -> Visual effects),
// and the window tests switch them off, so a picture is never taken halfway.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace IdMaker.App;

internal static class Motion
{
    /// <summary>Set by the window tests: every movement ends where it would have ended, at once.</summary>
    internal static bool Off { get; set; }

    /// <summary>Whether to move at all: Windows' animation effects are on, and nothing has switched them off here.</summary>
    internal static bool On => !Off && SystemParameters.ClientAreaAnimation;

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    private static DoubleAnimation To(double from, double to, int ms, IEasingFunction ease) =>
        new(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };

    private static TranslateTransform Shift(UIElement element)
    {
        if (element.RenderTransform is TranslateTransform shift) return shift;
        shift = new TranslateTransform();
        element.RenderTransform = shift;
        return shift;
    }

    /// <summary>A panel arriving from the side it belongs to, fading in as it comes.</summary>
    internal static void SlideIn(UIElement element, double fromX)
    {
        var shift = Shift(element);
        if (!On)
        {
            shift.BeginAnimation(TranslateTransform.XProperty, null);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            shift.X = 0;
            element.Opacity = 1;
            return;
        }
        shift.BeginAnimation(TranslateTransform.XProperty, To(fromX, 0, 200, EaseOut));
        element.BeginAnimation(UIElement.OpacityProperty, To(0, 1, 160, EaseOut));
    }

    /// <summary>A panel leaving towards the side it came from, fading as it goes; `done` runs when it has gone (at once when nothing moves).</summary>
    internal static void SlideOut(UIElement element, double toX, Action done)
    {
        if (!On) { done(); return; }
        var shift = Shift(element);
        var fade = To(element.Opacity, 0, 150, EaseIn);
        fade.Completed += (_, _) => done();
        shift.BeginAnimation(TranslateTransform.XProperty, To(shift.X, toX, 150, EaseIn));
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Take an element's opacity to `to`; `done`, if given, runs when it gets there (at once when nothing moves).</summary>
    internal static void Fade(UIElement element, double to, int ms = 180, Action? done = null)
    {
        if (!On)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            done?.Invoke();
            return;
        }
        var fade = To(element.Opacity, to, ms, to < element.Opacity ? EaseIn : EaseOut);
        if (done is not null) fade.Completed += (_, _) => done();
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>
    /// Move a number (a place on a canvas, a width) to a new value: gliding
    /// there when `glide` and motion is on, otherwise straight there.
    /// </summary>
    internal static void Glide(UIElement element, DependencyProperty property, double to, bool glide)
    {
        var now = (double)element.GetValue(property);
        if (!On || !glide || double.IsNaN(now))
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, to);
            return;
        }
        element.BeginAnimation(property, To(now, to, 240, EaseOut));
    }

    /// <summary>Something that changed in place, fading up from part-way so it is never gone.</summary>
    internal static void FadeIn(UIElement element, double from = 0.35, int ms = 180)
    {
        if (!On) { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; return; }
        element.BeginAnimation(UIElement.OpacityProperty, To(from, 1, ms, EaseOut));
    }

    /// <summary>
    /// Turn the card over: it narrows to its edge about its own centre, the
    /// other side is drawn while it is edge-on, and it widens again. Only its
    /// render transform changes, so its size and place on the page do not.
    /// </summary>
    internal static void Flip(FrameworkElement card, Action turn)
    {
        if (card.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            card.RenderTransform = scale;
            card.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        if (!On || card.Visibility != Visibility.Visible) { scale.ScaleX = 1; turn(); return; }

        var narrow = To(scale.ScaleX, 0, 110, EaseIn);
        narrow.Completed += (_, _) =>
        {
            turn();
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, To(0, 1, 140, EaseOut));
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, narrow);
    }
}
