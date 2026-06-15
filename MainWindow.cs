using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json.Linq;
using Windows.Services.Store;
using WinRT.Interop;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow : Form
    {
        private readonly StoreContext _store;
        private readonly nint _hwnd;

        // WinForms WebView2 uses windowed hosting (a real child HWND), so input AND the
        // Pointer Lock API both work — unlike the WinUI XAML WebView2 (visual hosting).
        private readonly WebView2 GameWebView;

        private const string AppSettingsFileName = "appsettings.json";

        private sealed class AppConfiguration
        {
            public required string ClientId { get; init; }
            public required Uri ServiceTicketBaseUrl { get; init; }

            public Uri ServiceTicketEndpoint => new(ServiceTicketBaseUrl, "/api/bridge/v1/microsoft-store/service-ticket");
        }

        private static AppConfiguration? _config;

        private static async Task<AppConfiguration> LoadConfigurationAsync()
        {
            var path = Path.Combine(AppContext.BaseDirectory, AppSettingsFileName);

            var text = File.Exists(path)
                ? await File.ReadAllTextAsync(path).ConfigureAwait(false)
                : "";

            var json = string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);

            var clientId = (string?)json["clientId"] ?? "";
            var baseUrl = (string?)json["serviceTicketBaseUrl"] ?? "";

            return new AppConfiguration
            {
                ClientId = clientId,
                ServiceTicketBaseUrl = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri : new Uri("https://playgama.com"),
            };
        }

        private static async Task EnsureConfigurationLoadedAsync()
        {
            _config ??= await LoadConfigurationAsync().ConfigureAwait(false);
        }

        private static AppConfiguration GetConfiguration()
        {
            if (_config is null)
                throw new InvalidOperationException("Configuration not loaded. Call EnsureConfigurationLoadedAsync() at startup.");
            return _config;
        }

        public MainWindow()
        {
            Text = GetDisplayName();
            // Default (restored) size, then open maximized to fill the screen.
            Width = 1280;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            WindowState = FormWindowState.Maximized;

            SetWindowIcon();

            GameWebView = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(GameWebView);

            // Force HWND creation so StoreContext / MSAL can be associated with this window.
            _hwnd = Handle;

            _store = StoreContext.GetDefault();
            InitializeWithWindow.Initialize(_store, _hwnd);
        }

        // The visible app/window name comes from the packaged DisplayName (set by the Packager
        // from the "Game title" field), so it matches the Store listing instead of a hardcoded value.
        private static string GetDisplayName()
        {
            try
            {
                var name = Windows.ApplicationModel.Package.Current.DisplayName;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { /* unpackaged / not available */ }
            return "Game";
        }

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                await EnsureConfigurationLoadedAsync();
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Startup failed: {ex}");
            }
        }
    }
}
