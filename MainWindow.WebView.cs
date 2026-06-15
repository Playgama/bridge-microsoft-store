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

            // Mirror the game's cursor intent (Cursor.visible) from the Unity canvas onto the
            // whole document. Unity only sets `cursor:none` on its <canvas>, which can leave the
            // default arrow visible around the canvas / fail to repaint inside WebView2. Reading
            // the canvas's computed cursor and replaying it keeps the wrapper neutral: it hides
            // when the game hides and shows when the game shows, across unlimited toggles.
            var mirrorCursorScript = @"
            (function () {
                function findCanvas() {
                    return document.querySelector('canvas#unity-canvas')
                        || document.querySelector('canvas');
                }

                function sync(canvas) {
                    var cur = getComputedStyle(canvas).cursor;
                    document.documentElement.style.cursor = cur;
                    if (document.body) document.body.style.cursor = cur;
                }

                function start() {
                    var canvas = findCanvas();
                    if (!canvas) { setTimeout(start, 200); return; }

                    var observer = new MutationObserver(function () { sync(canvas); });
                    observer.observe(canvas, { attributes: true, attributeFilter: ['style', 'class'] });

                    sync(canvas);
                }

                if (document.readyState === 'loading')
                    document.addEventListener('DOMContentLoaded', start);
                else
                    start();
            })();";

            await GameWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(mirrorCursorScript);

            // Make Pointer Lock (CursorLockMode.Locked) resilient: remember the game's lock
            // request even when it fails for lack of a user gesture, and replay it on the first
            // real interaction so the cursor doesn't get stuck visible. The very first gesture
            // and the post-Esc cooldown are Chromium security rules that self-heal on next click.
            var pointerLockScript = @"
            (function () {
                var pending = null;
                var native = Element.prototype.requestPointerLock;

                Element.prototype.requestPointerLock = function () {
                    pending = this;
                    try { return native.apply(this, arguments); } catch (e) {}
                };

                function retry() {
                    if (pending && document.pointerLockElement == null) {
                        try { native.call(pending); } catch (e) {}
                    }
                }
                ['pointerdown', 'mousedown', 'keydown', 'touchstart'].forEach(function (ev) {
                    window.addEventListener(ev, retry, true);
                });

                document.addEventListener('pointerlockchange', function () {
                    if (document.pointerLockElement) pending = null;
                });
            })();";

            await GameWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(pointerLockScript);

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "Assets", "game");

            GameWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                AppAssetsHost,
                htmlPath,
                CoreWebView2HostResourceAccessKind.Allow);

            AppendLog($"Virtual host: {AppAssetsBaseUri}");

            GameWebView.Source = new Uri(AppAssetsBaseUri, "index.html");

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

                case ActionName.AUTHORIZE_PLAYER:
                    _ = HandleAuthorizeAsync(sender, data);
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