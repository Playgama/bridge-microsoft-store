using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
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

            // Force HWND creation so StoreContext can be associated with this window.
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
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Startup failed: {ex}");
            }
        }

        private bool _closing;

        // On Alt+F4 / X the form (and WebView2 process) would tear down instantly, dropping
        // queued analytics/network calls. Defer the close, let the page flush, then exit.
        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closing)
            {
                base.OnFormClosing(e);
                return;
            }

            e.Cancel = true;     // postpone the real close
            _closing = true;

            try { await FlushBeforeCloseAsync(); }
            catch (Exception ex) { AppendLog($"Flush on close failed: {ex.Message}"); }

            Close();             // now _closing == true, so it proceeds
        }

        private async Task FlushBeforeCloseAsync()
        {
            var core = GameWebView?.CoreWebView2;
            if (core is null) return;

            // 1) Fire the page-lifecycle events analytics libraries flush on (sendBeacon/fetch).
            try
            {
                await core.ExecuteScriptAsync(
                    "try{window.dispatchEvent(new Event('pagehide'));" +
                    "document.dispatchEvent(new Event('visibilitychange'));" +
                    "window.dispatchEvent(new Event('beforeunload'));}catch(e){}");
            }
            catch { /* page may be busy */ }

            // 2) Navigate to a blank page — a real unload, so the browser runs the page's
            //    unload handlers and flushes queued beacons/requests.
            var navigated = new TaskCompletionSource();
            void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs a)
            {
                core.NavigationCompleted -= OnNav;
                navigated.TrySetResult();
            }
            core.NavigationCompleted += OnNav;
            try { core.Navigate("about:blank"); }
            catch { navigated.TrySetResult(); }

            await Task.WhenAny(navigated.Task, Task.Delay(2000));

            // 3) Give the network stack a brief moment to actually send the requests.
            await Task.Delay(700);
        }
    }
}
