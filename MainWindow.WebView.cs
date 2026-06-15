using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private const string AppAssetsHost = "appassets.local";
        private static readonly Uri AppAssetsBaseUri = new($"https://{AppAssetsHost}/");

        private async Task InitializeAsync()
        {
            await GameWebView.EnsureCoreWebView2Async();

            var web = GameWebView.CoreWebView2;

            web.Settings.AreDefaultScriptDialogsEnabled = false;

            web.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Hide navigator.mediaDevices so games don't prompt for camera/mic permissions.
            var permissionProbeScript = @"
            (function   () {
                try {
                    Object.defineProperty(navigator, 'mediaDevices', {
                        get() { return undefined; },
                        configurable: false
                    });
                } catch (e) {
                    // fallback
                    try { navigator.mediaDevices = undefined; } catch (e2) { }
                }
            })();";

            await web.AddScriptToExecuteOnDocumentCreatedAsync(permissionProbeScript);

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "game");

            web.SetVirtualHostNameToFolderMapping(
                AppAssetsHost,
                htmlPath,
                CoreWebView2HostResourceAccessKind.Allow);

            AppendLog($"Virtual host: {AppAssetsBaseUri}");

            GameWebView.Source = new Uri(AppAssetsBaseUri, "index.html");

            _ = IncrementLaunchCount();
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var core = GameWebView.CoreWebView2;

            var msg = args.TryGetWebMessageAsString();
            AppendLog($"Web → Host: {msg}");

            string? action = null;
            JToken? data = null;

            try
            {
                var root = JObject.Parse(msg);
                action = (string?)root["action"];
                data = root["data"];
            }
            catch (Exception)
            {
                // invalid JSON
            }

            AppendLog($"Parsed action: {action ?? "<null>"}");

            if (string.IsNullOrWhiteSpace(action))
            {
                Reply(core, $"Host received: {msg}");
                return;
            }

            switch (action)
            {
                case ActionName.INITIALIZE:
                    HandleInitialize(core, data);
                    return;

                case ActionName.AUTHORIZE_PLAYER:
                    _ = HandleAuthorizeAsync(core, data);
                    return;

                case ActionName.RATE:
                    _ = HandleRateAsync(core, data);
                    return;

                case ActionName.GET_PURCHASES:
                    _ = HandleGetPurchasesAsync(core, data);
                    return;

                case ActionName.GET_CATALOG:
                    _ = HandleGetCatalogAsync(core, data);
                    return;

                case ActionName.PURCHASE:
                    _ = HandlePurchaseAsync(core, data);
                    return;

                case ActionName.CONSUME_PURCHASE:
                    _ = HandleConsumePurchaseAsync(core, data);
                    return;

                case ActionName.GET_STORAGE_DATA:
                    _ = HandleGetStorageDataAsync(core, data);
                    return;

                case ActionName.SET_STORAGE_DATA:
                    _ = HandleSetStorageDataAsync(core, data);
                    return;

                case ActionName.DELETE_STORAGE_DATA:
                    _ = HandleDeleteStorageDataAsync(core, data);
                    return;

                default:
                    HandleUnknownAction(core, action);
                    return;
            }
        }

        private void HandleInitialize(CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: initialize");

            Reply(sender, new JObject
            {
                ["action"] = ActionName.INITIALIZE,
                ["success"] = true,
                ["data"] = data
            }.ToString());
        }

        private void HandleUnknownAction(CoreWebView2 sender, string action)
        {
            AppendLog($"Handler: unknown action '{action}'");

            Reply(sender, new JObject
            {
                ["action"] = action,
                ["success"] = false,
                ["error"] = "unknown_action"
            }.ToString());
        }

        private void Reply(CoreWebView2 sender, string payload)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() =>
                {
                    sender.PostWebMessageAsString(payload);
                    AppendLog($"Host → Web: {payload}");
                }));
                return;
            }

            sender.PostWebMessageAsString(payload);
            AppendLog($"Host → Web: {payload}");
        }
    }
}
