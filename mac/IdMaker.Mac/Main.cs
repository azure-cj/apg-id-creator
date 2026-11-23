using IdMaker.Mac;

// `--selftest` runs the checks that need a real Mac and no person, prints
// what it found and exits: 0 when all hold. CI runs it on every change.
NSApplication.Init();
if (args.Contains("--selftest"))
{
    var at = Array.IndexOf(args, "--repo");
    return SelfTest.Run(at >= 0 && at + 1 < args.Length ? args[at + 1] : null, args.Contains("--needs-register"));
}

#if WINDOWTEST
// `--windowtest` opens the real window and does what an operator does, with
// a pretend register behind it. Only in a build made for testing.
if (args.Contains("--windowtest"))
{
    var at = Array.IndexOf(args, "--repo");
    NSApplication.SharedApplication.Delegate = new WindowTestDelegate(at >= 0 && at + 1 < args.Length ? args[at + 1] : null);
    NSApplication.Main(args);
    return 0;
}
#endif

// `--starttest` starts the app as a double-click does, says whether the
// window opened with the card in it, and quits: how a packaged app is
// shown to start.
NSApplication.SharedApplication.Delegate = new AppDelegate { ExitAfterStart = args.Contains("--starttest") };
NSApplication.Main(args);
return 0;
