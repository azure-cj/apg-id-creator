// Values that have their home in Core but have to be spelled again in files
// that cannot read Core: the markup, the project files, the Mac's property
// list, the installer and the packaging scripts. These tests read those
// files, as SecretTests and ThemeTests read theirs, and fail when one of
// them says something other than Core does. They need the files, not
// Windows or a Mac.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using IdMaker.Core;

namespace IdMaker.Tests;

public class HomeTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Repo.Root }.Concat(parts).ToArray()));

    private static readonly string Xaml = Read("windows", "IdMaker.App", "MainWindow.xaml");
    private static readonly string WindowsProject = Read("windows", "IdMaker.App", "IdMaker.App.csproj");
    private static readonly string MacProject = Read("mac", "IdMaker.Mac", "IdMaker.Mac.csproj");
    private static readonly string Installer = Read("windows", "installer", "IdMaker.iss");
    private static readonly string InstallerCheck = Read("windows", "installer", "selftest.ps1");
    private static readonly string Portable = Read("windows", "installer", "portable.ps1");
    private static readonly string PackageWindows = Read("windows", "installer", "package.ps1");
    private static readonly string PackageMac = Read("mac", "package.sh");

    /// <summary>The one value a pattern finds in a file; the test fails if it is not there exactly once.</summary>
    private static string One(string text, string pattern)
    {
        var found = Regex.Matches(text, pattern);
        Assert.True(found.Count == 1, $"expected one match of {pattern}, found {found.Count}");
        return found[0].Groups[1].Value;
    }

    /// <summary>An attribute of the one element in the window's markup with this x:Name.</summary>
    private static string Attribute(string name, string attribute)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var element = Assert.Single(XDocument.Parse(Xaml).Descendants(), e => (string?)e.Attribute(x + "Name") == name);
        return (string?)element.Attribute(attribute) ?? "";
    }

    private static string Element(string project, string name) => One(project, $"<{name}>([^<]+)</{name}>");

    [Fact]
    public void The_version_is_the_same_everywhere_it_is_written()
    {
        Assert.Equal(CardConfig.AppVersion, Element(WindowsProject, "Version"));
        Assert.Equal(CardConfig.AppVersion, Element(MacProject, "Version"));
        Assert.Equal(CardConfig.AppVersion, Element(MacProject, "ApplicationDisplayVersion"));
        // Windows' own manifest carries four parts
        Assert.Equal(CardConfig.AppVersion + ".0", One(Read("windows", "IdMaker.App", "app.manifest"), "<assemblyIdentity version=\"([^\"]+)\""));
    }

    [Fact]
    public void The_app_has_one_name_one_publisher_and_one_program_name()
    {
        Assert.Equal(CardConfig.AppName, One(Xaml, "\\sTitle=\"([^\"]+)\""));
        Assert.Equal(CardConfig.AppName, Element(WindowsProject, "Product"));
        Assert.Equal(CardConfig.AppName, Element(MacProject, "ApplicationTitle"));
        Assert.Equal(CardConfig.AppName, One(Read("mac", "IdMaker.Mac", "Info.plist"), "<key>CFBundleName</key>\\s*<string>([^<]+)</string>"));
        Assert.Equal(CardConfig.AppName, One(Installer, "#define AppName \"([^\"]+)\""));
        Assert.Equal(CardConfig.AppName, One(InstallerCheck, "\\$Name\\s*=\\s*'([^']+)'"));
        Assert.Equal(CardConfig.AppName, One(Portable, "\\$title -ne '([^']+)'"));
        Assert.Equal(CardConfig.AppName, One(PackageMac, "-volname \"([^\"]+)\""));

        Assert.Equal(CardConfig.Publisher, Element(WindowsProject, "Company"));
        Assert.Equal(CardConfig.Publisher, One(Installer, "AppPublisher=([^\\r\\n]+)"));
        Assert.Equal(CardConfig.Publisher, One(InstallerCheck, "\\.Publisher -ne '([^']+)'"));

        Assert.Equal(CardConfig.ProgramName, Element(WindowsProject, "AssemblyName"));
        Assert.Equal(CardConfig.ProgramName, Element(MacProject, "AssemblyName"));
        Assert.Equal(CardConfig.ProgramName + ".exe", One(Installer, "#define AppExe \"([^\"]+)\""));
        Assert.Equal(CardConfig.ProgramName + "-Setup", One(Installer, "OutputBaseFilename=([^\\r\\n]+)"));
        Assert.Equal(CardConfig.ProgramName + ".exe", One(InstallerCheck, "\\$Exe\\s*=\\s*Join-Path \\$Folder '([^']+)'"));
        Assert.Equal(CardConfig.ProgramName + ".exe", One(Portable, "\\$built = Join-Path \\$Out '([^']+)'"));
        Assert.Equal(CardConfig.ProgramName, One(PackageMac, "Contents/MacOS/([A-Za-z]+)\""));
        // the palettes are found by the program's name, inside a resource address
        Assert.Equal(CardConfig.ProgramName, One(Read("windows", "IdMaker.App", "ThemeManager.cs"), "pack://application:,,,/([A-Za-z]+);component/"));
    }

    [Fact]
    public void The_scripts_look_for_the_app_s_data_where_the_app_keeps_it()
    {
        var folder = $@"{FileStore.CompanyFolder}\{FileStore.AppFolder}";
        Assert.Equal(folder, One(InstallerCheck, "\\$Data\\s*=\\s*Join-Path \\$env:LOCALAPPDATA '([^']+)'"));
        Assert.Equal(folder, One(Portable, "\\$data = Join-Path \\$env:LOCALAPPDATA '([^']+)'"));
        Assert.Equal(CardConfig.LastErrorFile, One(InstallerCheck, "\\$err = Join-Path \\$Data '([^']+)'"));
        Assert.Equal(CardConfig.LastErrorFile, One(Portable, "\\$err = Join-Path \\$data '([^']+)'"));
    }

    [Fact]
    public void Both_apps_are_built_with_the_pictures_Core_names()
    {
        foreach (var file in new[] { CardConfig.FrontArtFile, CardConfig.BackArtFile, CardConfig.LogoFile })
        {
            Assert.True(File.Exists(Repo.Asset(file)), $"assets/{file} is not there");
            Assert.Contains($"assets\\{file}\" Link=\"Assets\\{file}\"", WindowsProject);
            Assert.Contains($"assets\\{file}\" LogicalName=\"Assets/{file}\"", MacProject);
        }
        // what the packaging refuses to go on without
        Assert.Contains($"'Assets\\{CardConfig.FrontArtFile}', 'Assets\\{CardConfig.BackArtFile}'", PackageWindows);
        Assert.Contains($"'{CardConfig.ProgramName}.exe'", PackageWindows);
    }

    [Fact]
    public void The_form_s_boxes_take_as_many_characters_as_Core_allows()
    {
        string Most(string box) => Attribute(box, "MaxLength");
        var name = CardConfig.MaxNameLength.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(name, Most("TxtLast"));
        Assert.Equal(name, Most("TxtFirst"));
        Assert.Equal(name, Most("TxtMiddle"));
        Assert.Equal(CardConfig.MaxSuffixLength.ToString(CultureInfo.InvariantCulture), Most("TxtSuffix"));
        var title = CardConfig.MaxTitleLength.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(title, Most("TxtTitleCustom"));
        Assert.Equal(title, Most("TxtTitle2Custom"));
        Assert.Equal(CardRules.MostSeqDigits.ToString(CultureInfo.InvariantCulture), Most("TxtSeq"));
    }

    [Fact]
    public void The_size_slider_runs_over_the_zoom_Core_allows()
    {
        string Percent(double zoom) => (zoom * 100).ToString("0", CultureInfo.InvariantCulture);
        var photo = CardConfig.Photo;
        Assert.Equal(Percent(photo.ZoomMin), Attribute("SldZoom", "Minimum"));
        Assert.Equal(Percent(photo.ZoomMax), Attribute("SldZoom", "Maximum"));
        Assert.Equal(Percent(photo.ZoomStep), Attribute("SldZoom", "TickFrequency"));
        Assert.Equal(Percent(photo.ZoomStep), Attribute("SldZoom", "SmallChange"));
        Assert.Equal("100", Attribute("SldZoom", "Value"));

        // the Mac's slider is made in code, in per cent as well
        var mac = Read("mac", "IdMaker.Mac", "MainWindow.cs");
        Assert.Equal(Percent(photo.ZoomMin), One(mac, "_sldZoom = new\\(\\) \\{ MinValue = (\\d+),"));
        Assert.Equal(Percent(photo.ZoomMax), One(mac, "_sldZoom = new\\(\\) \\{ MinValue = \\d+, MaxValue = (\\d+),"));
        Assert.Equal(Percent(photo.ZoomStep), One(mac, "_sldZoom\\.DoubleValue / (\\d+)\\) \\* \\d+ / 100"));
    }

    [Fact]
    public void The_card_on_screen_has_the_card_s_own_shape()
    {
        // the map's boxes are placed in tenths of a millimetre over a surface that many units across
        var units = double.Parse(One(Read("windows", "IdMaker.App", "MapEditor.cs"), "UnitsPerMm = (\\d+);"), CultureInfo.InvariantCulture);
        Assert.Equal((CardConfig.CardWidthMm * units).ToString("0", CultureInfo.InvariantCulture), Attribute("CardSurface", "Width"));
        Assert.Equal((CardConfig.CardHeightMm * units).ToString("0", CultureInfo.InvariantCulture), Attribute("CardSurface", "Height"));
        // and the artwork a designer is asked for is the size Core stores
        Assert.Contains($"Both at {TemplateStore.ArtworkWidth} × {TemplateStore.ArtworkHeight} pixels.", Xaml);
    }

    [Fact]
    public void The_markup_s_tags_are_the_words_Core_uses()
    {
        Assert.Equal(ToolKey.Register, Attribute("RailRegister", "Tag"));
        Assert.Equal(ToolKey.Batch, Attribute("RailBatch", "Tag"));
        Assert.Equal(ToolKey.Printer, Attribute("RailPrinter", "Tag"));
        Assert.Equal(ToolKey.Design, Attribute("RailDesign", "Tag"));

        var doc = XDocument.Parse(Xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        List<string> TagsIn(string box) => doc.Descendants().Single(e => (string?)e.Attribute(x + "Name") == box)
            .Elements().Select(item => (string?)item.Attribute("Tag") ?? "").ToList();
        Assert.Equal(new[] { Batch.LayoutDuplex, Batch.LayoutPairs, Batch.LayoutCards }, TagsIn("CmbBatchLayout"));
        Assert.Equal(new[] { PersonKind.Employee, PersonKind.Intern }, TagsIn("CmbKind"));

        // the three Edit links on the last step go to the name and the job step (the number shares the job's page), by number
        var edits = doc.Descendants().Where(e => (string?)e.Attribute("Click") == "SummaryEdit_Click").Select(e => (string?)e.Attribute("Tag") ?? "").ToList();
        Assert.Equal(new[] { (int)Step.Name, (int)Step.Title, (int)Step.Title }.Select(n => n.ToString(CultureInfo.InvariantCulture)), edits);
        // and the window shows one panel per step
        var panels = new[] { "StepName", "StepTitle", "StepPhoto", "StepSign", "StepCheck" };
        Assert.Equal(Session.Steps.Length, panels.Length);
        Assert.All(panels, panel => Assert.NotNull(Attribute(panel, "Visibility")));
    }

    [Fact]
    public void Gold_and_screen_ink_are_the_same_colour_wherever_they_are_spelled()
    {
        static string Bytes(string hex) => $"0x{hex[1..3]}, 0x{hex[3..5]}, 0x{hex[5..7]}";
        foreach (var mode in new[] { "Light", "Dark" })
        {
            var palette = Read("windows", "IdMaker.App", $"Colors.{mode}.xaml");
            Assert.Equal(CardConfig.BrandGold, One(palette, "x:Key=\"Palette.Gold\">(#[0-9A-Fa-f]{6})<"));
        }
        // the map's handles are drawn in code, in bytes
        Assert.Contains(Bytes(CardConfig.BrandGold), Read("windows", "IdMaker.App", "MapEditor.cs"));
        Assert.Contains($"InkColor = Color.FromRgb({Bytes(CardConfig.ScreenInk)})", Read("windows", "IdMaker.App", "SignaturePad.cs"));
    }
}
