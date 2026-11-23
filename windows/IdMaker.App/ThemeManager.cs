// Light or dark, as Windows is set.
//
// Windows keeps the choice under Settings -> Personalisation -> Colours ->
// "Choose your app mode". The window reads it when it opens and follows it
// while it is open: Windows announces a change to every top-level window
// with WM_SETTINGCHANGE "ImmersiveColorSet", and the colours are swapped on
// the spot, title bar included, without restarting anything.
//
// The colours themselves are in Colors.Light.xaml and Colors.Dark.xaml,
// which define the same names. Everything in the window refers to them
// with DynamicResource, so swapping the dictionary is the whole change.

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace IdMaker.App;

public enum AppTheme { Light, Dark }

public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;     // Windows 10 before version 20H1

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>The mode the window is drawn in now.</summary>
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>Raised after the colours change.</summary>
    public static event Action? Changed;

    /// <summary>
    /// What Windows is set to for apps. Light when the setting is missing or
    /// cannot be read: that is Windows' own default, and what older versions
    /// without the setting always were.
    /// </summary>
    public static AppTheme SystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0 ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            return AppTheme.Light;
        }
    }

    private static Uri Palette(AppTheme theme) =>
        new($"pack://application:,,,/AlphaPremierIdMaker;component/Colors.{theme}.xaml", UriKind.Absolute);

    /// <summary>Draw the whole application in this mode.</summary>
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null) return;
        var merged = app.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = Palette(theme) };
        var at = -1;
        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Source?.OriginalString.Contains("Colors.", StringComparison.Ordinal) == true) { at = i; break; }
        }
        if (at >= 0) merged[at] = replacement; else merged.Insert(0, replacement);
        Current = theme;
        foreach (Window window in app.Windows) SetTitleBar(window);
        Changed?.Invoke();
    }

    /// <summary>A dark title bar in dark mode, a light one in light mode.</summary>
    public static void SetTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;                // not shown yet: done again when it is
        var dark = Current == AppTheme.Dark ? 1 : 0;
        try
        {
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref dark, sizeof(int));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // a Windows without the attribute keeps its usual title bar; the window itself still follows the mode
        }
    }

    /// <summary>
    /// Set this window's title bar now, and follow Windows from here on:
    /// when the app mode changes in Settings, the colours change with it.
    /// </summary>
    public static void Follow(Window window)
    {
        SetTitleBar(window);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr lParam, ref bool _) =>
        {
            if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
            {
                var now = SystemTheme();
                if (now != Current) Apply(now);
            }
            return IntPtr.Zero;
        });
    }
}
