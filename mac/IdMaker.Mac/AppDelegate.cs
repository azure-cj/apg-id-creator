namespace IdMaker.Mac;

[Register("AppDelegate")]
public sealed class AppDelegate : NSApplicationDelegate
{
    private MainWindow? _main;

    /// <summary>Say whether the window opened with the card in it, then quit. Asks nobody anything.</summary>
    public bool ExitAfterStart { get; init; }

    public override void DidFinishLaunching(NSNotification notification)
    {
        _main = new MainWindow();
        if (ExitAfterStart) _main.OfferDriverOnStart = false;
        _main.Window.MakeKeyAndOrderFront(this);
        if (ExitAfterStart)
        {
            Console.WriteLine(_main.Loaded ? "The window opened and the card loaded." : $"The window opened, but: {_main.LoadError}");
            Console.Out.Flush();
            Environment.Exit(_main.Loaded && _main.Window.IsVisible ? 0 : 1);
        }
        if (OperatingSystem.IsMacOSVersionAtLeast(14)) NSApplication.SharedApplication.Activate();
        else NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;
}
