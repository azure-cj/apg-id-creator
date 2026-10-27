using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AlphaPremier.IdMaker;

/// <summary>
/// A window around the ID Maker. It draws nothing itself: the card, the
/// photo pipeline and the PDF all come from the one HTML file, exactly as
/// they do in a browser, so the golden-render test still governs what is
/// printed.
/// </summary>
internal static class Program
{
    // Serving the page from a made-up host rather than from a file is the
    // reason this shell exists. A file:// page has no real origin, which
    // makes browser storage fragile and cross-site requests -- the shared
    // register among them -- behave unlike anywhere the app was tested.
    // Under a host name it is an ordinary https origin and everything
    // behaves as it does on a real site.
    private const string Host = "idmaker.alphapremier.local";
    private const string Url = $"https://{Host}/index.html";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private sealed class MainForm : Form
    {
        private readonly WebView2 _view = new() { Dock = DockStyle.Fill };

        public MainForm()
        {
            Text = "Alpha Premier ID Maker";
            MinimumSize = new Size(960, 700);
            ClientSize = new Size(1280, 900);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "app.ico")); } catch { /* no icon, no matter */ }

            Controls.Add(_view);
            _ = StartAsync();
        }

        private async Task StartAsync()
        {
            // the browser's own data (the issued-ID list, the register
            // address, the printer settings) belongs to this app, not to
            // whoever is signed in to Edge
            var data = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlphaPremierIDMaker", "WebView2");
            Directory.CreateDirectory(data);

            try
            {
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: data);
                await _view.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "This app needs the Microsoft Edge WebView2 Runtime, which is not installed.\n\n" +
                    "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and start the app again.\n\n" +
                    ex.Message,
                    "Alpha Premier ID Maker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Close();
                return;
            }

            var core = _view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                Host, Path.Combine(AppContext.BaseDirectory, "web"),
                CoreWebView2HostResourceAccessKind.Allow);

            // it is an application window, not a browser
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            // left on deliberately: this is how a problem on a user's
            // machine gets diagnosed, and nobody finds it by accident
            core.Settings.AreDevToolsEnabled = true;

            // a link to somewhere else opens in the real browser, so the
            // app window can never become a way to browse the web
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                OpenExternally(e.Uri);
            };
            core.NavigationStarting += (_, e) =>
            {
                if (e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) return;
                if (e.Uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return;   // the PDF download
                e.Cancel = true;
                OpenExternally(e.Uri);
            };

            core.Navigate(Url);
        }

        private static void OpenExternally(string uri)
        {
            if (!uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch { /* nothing sensible to do if the browser will not open */ }
        }
    }
}
