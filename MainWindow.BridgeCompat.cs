using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private const string PlatformId = "microsoft_store";

        // Games arrive as opaque prebuilt bundles: we cannot rely on them shipping a config with
        // forciblySetPlatformId, nor on them passing the packaged config to bridge.initialize()
        // (some builds hand it an inline data: URI instead). Neither v1 nor v2 of the bridge can
        // auto-detect this host — their platform detection is a list of hostname predicates with
        // no Microsoft Store entry and no chrome.webview check, so anything unrecognised falls
        // back to the "mock" platform. So the host forces the platform id itself, two ways:
        //   1. ?platform_id=microsoft_store on the page URL (2nd in the bridge's detection order)
        //   2. rewriting whatever the bridge fetches as its config (1st in that order)
        // Runs before any page script (AddScriptToExecuteOnDocumentCreatedAsync).
        private string BuildBridgeCompatScript(string gameDir)
        {
            var (gameId, adsId) = ReadPackagedBridgeConfig(gameDir);
            AppendLog($"Bridge compat: platform={PlatformId} gameId={(gameId.Length == 0 ? "<none>" : gameId)} adsId={(adsId.Length == 0 ? "<none>" : adsId)}");

            return @"
(function () {
    var PLATFORM_ID = " + JsString(PlatformId) + @";
    var GAME_ID = " + JsString(gameId) + @";
    var ADS_ID = " + JsString(adsId) + @";

    // The bridge reads the platform id off window.location at initialize() time.
    try {
        var url = new URL(window.location.href);
        if (url.searchParams.get('platform_id') !== PLATFORM_ID) {
            url.searchParams.set('platform_id', PLATFORM_ID);
            history.replaceState(history.state, '', url.toString());
        }
    } catch (e) { }

    function isConfigUrl(u) {
        return /playgama-bridge-config/i.test(u) || /^data:application\/json/i.test(u);
    }

    // Returns the patched JSON, or null to leave the response untouched.
    function patchConfig(text) {
        var cfg;
        try { cfg = JSON.parse(text); } catch (e) { return null; }
        if (!cfg || typeof cfg !== 'object' || Array.isArray(cfg)) return null;

        var keys = ['platforms', 'advertisement', 'payments', 'leaderboards', 'device', 'forciblySetPlatformId'];
        var looksLikeBridgeConfig = keys.some(function (k) { return k in cfg; });
        if (!looksLikeBridgeConfig) return null;

        cfg.forciblySetPlatformId = PLATFORM_ID;

        if (!cfg.platforms || typeof cfg.platforms !== 'object') cfg.platforms = {};
        var ms = cfg.platforms[PLATFORM_ID];
        if (!ms || typeof ms !== 'object') { ms = {}; cfg.platforms[PLATFORM_ID] = ms; }

        // The Microsoft Store platform bridge rejects initialize() with GAME_PARAMS_NOT_FOUND
        // unless both of these are set, so fill them from the packaged config.
        if (!ms.gameId && GAME_ID) ms.gameId = GAME_ID;
        if (!ms.playgamaAdsId && ADS_ID) ms.playgamaAdsId = ADS_ID;

        return JSON.stringify(cfg);
    }

    // Every bridge build (v1, v2, and the all-in-one bundle) loads its config via fetch —
    // including the data: URI case, which never reaches the native resource handler.
    var originalFetch = window.fetch;
    if (typeof originalFetch === 'function') {
        window.fetch = function (input, init) {
            var url = '';
            try { url = typeof input === 'string' ? input : (input && input.url) || ''; } catch (e) { }

            var pending = originalFetch.apply(this, arguments);
            if (!url || !isConfigUrl(url)) return pending;

            return pending.then(function (response) {
                if (!response || !response.ok) return response;
                return response.clone().text().then(function (text) {
                    var patched = patchConfig(text);
                    if (patched === null) return response;
                    return new Response(patched, {
                        status: response.status,
                        statusText: response.statusText,
                        headers: response.headers
                    });
                }).catch(function () { return response; });
            });
        };
    }
}());";
        }

        // The Packager writes the Store game id / ads id into the game's playgama-bridge-config.json;
        // reuse them so a game that ships its own inline config still gets the right values.
        private (string GameId, string AdsId) ReadPackagedBridgeConfig(string gameDir)
        {
            try
            {
                var path = Path.Combine(gameDir, "playgama-bridge-config.json");
                if (!File.Exists(path)) return ("", "");

                var ms = JObject.Parse(File.ReadAllText(path))["platforms"]?[PlatformId];
                return ((string?)ms?["gameId"] ?? "", (string?)ms?["playgamaAdsId"] ?? "");
            }
            catch (Exception ex)
            {
                AppendLog($"Bridge compat: could not read packaged config ({ex.Message})");
                return ("", "");
            }
        }

        private static string JsString(string value) => JValue.CreateString(value).ToString(Newtonsoft.Json.Formatting.None);
    }
}
