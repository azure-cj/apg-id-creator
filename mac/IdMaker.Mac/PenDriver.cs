// The pen tablet's driver on a Mac. XP-Pen's driver is an ordinary app that
// its installer puts in /Applications; without it the pen still moves the
// pointer, but macOS hears no pressure from it.
//
// The app can fetch and open XP-Pen's installer, as the Windows setup
// program does: XP-Pen's own file, from XP-Pen's own server, pinned to one
// version and one exact file. It cannot install silently. Apple's installer
// asks for this Mac's administrator password, and macOS then asks the
// person to allow the driver under Input Monitoring and Accessibility; no
// app can answer those for them.
//
// To move to a newer driver: download it from
// https://www.xp-pen.com/download/deco-fun.html, put its address and
// SHA-256 (shasum -a 256) here, and install it once by hand on a test Mac
// before sending it to everyone. The Mac app workflow fails when XP-Pen no
// longer serves the file named here.

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace IdMaker.Mac;

internal static class PenDriver
{
    public const string Name = "XP-Pen Deco Fun driver";
    public const string Version = "4.0.21_261026";
    public const string Url = "https://download01.xp-pen.com/file/2026/10/XPPenMac_4.0.21_261026.zip";
    public const string Sha256 = "c16a4a00822c4f1b80654ebf1cda485c1e8502bacbe9dfad3d36c205b65c96bb";

    // Is this the name of a pen tablet driver? XP-Pen's own, or another
    // maker's: two tablet drivers on one computer fight over the pen. The
    // same rule as the Windows installer's.
    internal static bool IsDriverName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("xp-pen") || lower.Contains("xppen") || lower.Contains("pentablet")
            || lower.Contains("wacom") || lower.Contains("huion");
    }

    public static bool Installed => InstalledUnder("/Applications");

    internal static bool InstalledUnder(string applications)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(applications).Any(entry => IsDriverName(Path.GetFileName(entry)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;       // a folder that cannot be read has no driver this app can see
        }
    }

    private static string Folder => Path.Combine(Path.GetTempPath(), "alpha-premier-id-maker-driver");

    private static string HashOf(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static (int Code, string Output) Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException($"{program} could not be started.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }

    /// <summary>
    /// Download XP-Pen's driver, check it is the exact file named above, and
    /// unpack it as far as Apple's installer package. The package's path, or
    /// what went wrong in words for the operator.
    /// </summary>
    public static async Task<(string? Package, string Error)> Fetch(HttpMessageHandler? handler = null)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var zip = Path.Combine(Folder, "driver.zip");
            if (!File.Exists(zip) || HashOf(zip) != Sha256)
            {
                using var http = handler is null ? new HttpClient() : new HttpClient(handler);
                http.Timeout = TimeSpan.FromMinutes(10);
                await using (var from = await http.GetStreamAsync(Url))
                await using (var to = File.Create(zip))
                {
                    await from.CopyToAsync(to);
                }
                var hash = HashOf(zip);
                if (hash != Sha256)
                {
                    File.Delete(zip);
                    return (null, "The file XP-Pen sent is not the driver this app was built to install, so it was not opened. Download the driver from xp-pen.com instead, or update the app.");
                }
            }

            var unpacked = Path.Combine(Folder, "unpacked");
            if (Directory.Exists(unpacked)) Directory.Delete(unpacked, recursive: true);
            ZipFile.ExtractToDirectory(zip, unpacked);
            var image = Directory.EnumerateFiles(unpacked, "*.dmg", SearchOption.AllDirectories).FirstOrDefault();
            if (image is null) return (null, "XP-Pen’s download did not contain a disk image. Download the driver from xp-pen.com instead.");

            var mount = Path.Combine(Folder, "mounted");
            if (Directory.Exists(mount)) Run("/usr/bin/hdiutil", "detach", mount, "-force");      // left from last time; nothing to do if it was not
            Directory.CreateDirectory(mount);
            var (code, said) = Run("/usr/bin/hdiutil", "attach", "-nobrowse", "-readonly", "-mountpoint", mount, image);
            if (code != 0) return (null, $"XP-Pen’s disk image could not be opened ({said}).");
            var package = Directory.EnumerateFiles(mount, "*.pkg").FirstOrDefault();
            return package is null
                ? (null, "XP-Pen’s disk image did not contain an installer. Download the driver from xp-pen.com instead.")
                : (package, "");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return (null, "The driver could not be downloaded. Check that this Mac is online and try again.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            return (null, $"The driver could not be unpacked ({e.Message.TrimEnd('.')}).");
        }
    }

    /// <summary>Fetch the driver and open it in Apple's installer. "" when the installer is open, or what went wrong.</summary>
    public static async Task<string> Install()
    {
        var (package, error) = await Fetch();
        if (package is null) return error;
        return NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(package)) ? "" : "Apple’s installer could not be opened. Open the XP-Pen driver from xp-pen.com instead.";
    }
}
