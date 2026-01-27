using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using PlaygamaBridgeMicrosoftStore.Server;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow
    {
        private async Task InitializeAsync()
        {
            await GameWebView.EnsureCoreWebView2Async();

            GameWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;

            GameWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

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

            await GameWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(permissionProbeScript);

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "game");

            _assetServer = new LocalAssetServer(htmlPath);
            _assetServer.Start();

            AppendLog($"Local server: {_assetServer.BaseUri}");

            GameWebView.Source = new Uri(_assetServer.BaseUri, "index.html");

            _ = IncrementLaunchCount();
        }

        private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
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
                Reply(sender, $"Host received: {msg}");
                return;
            }

            switch (action)
            {
                case ActionName.INITIALIZE:
                    HandleInitialize(sender, data);
                    return;

                case ActionName.RATE:
                    _ = HandleRateAsync(sender, data);
                    return;

                case ActionName.GET_PURCHASES:
                    _ = HandleGetPurchasesAsync(sender, data);
                    return;

                case ActionName.GET_CATALOG:
                    _ = HandleGetCatalogAsync(sender, data);
                    return;

                case ActionName.PURCHASE:
                    _ = HandlePurchaseAsync(sender, data);
                    return;

                case ActionName.CONSUME_PURCHASE:
                    _ = HandleConsumePurchaseAsync(sender, data);
                    return;

                case ActionName.GET_STORAGE_DATA:
                    _ = HandleGetStorageDataAsync(sender, data);
                    return;

                case ActionName.SET_STORAGE_DATA:
                    _ = HandleSetStorageDataAsync(sender, data);
                    return;

                case ActionName.DELETE_STORAGE_DATA:
                    _ = HandleDeleteStorageDataAsync(sender, data);
                    return;

                default:
                    HandleUnknownAction(sender, action);
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
            if (DispatcherQueue is not null && !DispatcherQueue.HasThreadAccess)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    sender.PostWebMessageAsString(payload);
                    AppendLog($"Host → Web: {payload}");
                });
                return;
            }

            sender.PostWebMessageAsString(payload);
            AppendLog($"Host → Web: {payload}");
        }
    }
}