using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;
using Windows.System;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;

            AppendLog($"New window blocked: {args.Uri} (userInitiated={args.IsUserInitiated})");

            if (args.IsUserInitiated)
                _ = OpenInDefaultBrowserAsync(args.Uri);
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (IsGameNavigation(args.Uri))
                return;

            args.Cancel = true;

            AppendLog($"Navigation blocked: {args.Uri} (userInitiated={args.IsUserInitiated})");

            if (args.IsUserInitiated)
                _ = OpenInDefaultBrowserAsync(args.Uri);
        }

        private bool IsGameNavigation(string uri)
        {
            if (string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                return true;

            return Uri.TryCreate(uri, UriKind.Absolute, out var target)
                && target.Scheme == Uri.UriSchemeHttps
                && string.Equals(target.Host, _gameHost, StringComparison.OrdinalIgnoreCase);
        }

        private async Task OpenInDefaultBrowserAsync(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var target)
                || (target.Scheme != Uri.UriSchemeHttps && target.Scheme != Uri.UriSchemeHttp))
            {
                AppendLog($"External link ignored: {uri}");
                return;
            }

            try
            {
                if (!await Launcher.LaunchUriAsync(target))
                    AppendLog($"External link not opened: {uri}");
            }
            catch (Exception ex)
            {
                AppendLog($"External link failed: {ex.Message}");
            }
        }
    }
}
