using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        // Only persistence helpers; does not change existing handler logic/format validation.
        private static async Task<StorageFile> EnsureSaveFileAsync()
        {
            var folder = ApplicationData.Current.LocalFolder;

            var existing = await folder.TryGetItemAsync(SaveFileName).AsTask().ConfigureAwait(false);
            if (existing is StorageFile f)
                return f;

            var file = await folder.CreateFileAsync(SaveFileName, CreationCollisionOption.OpenIfExists)
                .AsTask().ConfigureAwait(false);

            await FileIO.WriteTextAsync(file, "{}", Windows.Storage.Streams.UnicodeEncoding.Utf8);
            return file;
        }

        private static async Task<JObject> ReadSaveRootAsync()
        {
            var file = await EnsureSaveFileAsync().ConfigureAwait(false);
            var text = await FileIO.ReadTextAsync(file, Windows.Storage.Streams.UnicodeEncoding.Utf8)
                .AsTask().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
                return new JObject();

            try
            {
                return JObject.Parse(text);
            }
            catch
            {
                // If corrupted, reset to empty object rather than failing storage operations.
                return new JObject();
            }
        }

        private static async Task WriteSaveRootAsync(JObject root)
        {
            var file = await EnsureSaveFileAsync().ConfigureAwait(false);
            await FileIO.WriteTextAsync(
                    file,
                    root.ToString(Newtonsoft.Json.Formatting.None),
                    Windows.Storage.Streams.UnicodeEncoding.Utf8)
                .AsTask().ConfigureAwait(false);
        }

        // New: storage handlers using ApplicationData.Current.LocalSettings
        private async Task HandleGetStorageDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: get_storage_data");

            try
            {
                var root = await ReadSaveRootAsync();

                if (data is null)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.GET_STORAGE_DATA,
                        ["success"] = false,
                        ["error"] = "missing_data"
                    }.ToString());
                    return;
                }

                if (data.Type == JTokenType.String)
                {
                    var key = (string?)data;

                    JToken? value = null;
                    if (!string.IsNullOrWhiteSpace(key) && root.TryGetValue(key!, out var v))
                    {
                        value = v;
                    }

                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.GET_STORAGE_DATA,
                        ["success"] = true,
                        ["data"] = value
                    }.ToString());
                    return;
                }

                if (data is JArray arr)
                {
                    var results = new JArray();
                    foreach (var token in arr)
                    {
                        if (token.Type != JTokenType.String)
                        {
                            results.Add(JValue.CreateNull());
                            continue;
                        }
                        var k = (string?)token;
                        if (string.IsNullOrWhiteSpace(k)) { results.Add(JValue.CreateNull()); continue; }
                        results.Add(root.TryGetValue(k!, out var v) ? (v ?? JValue.CreateNull()) : JValue.CreateNull());
                    }

                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.GET_STORAGE_DATA,
                        ["success"] = true,
                        ["data"] = results
                    }.ToString());
                    return;
                }

                Reply(sender, new JObject
                {
                    ["action"] = ActionName.GET_STORAGE_DATA,
                    ["success"] = false,
                    ["error"] = "invalid_data"
                }.ToString());
            }
            catch (Exception ex)
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.GET_STORAGE_DATA,
                    ["success"] = false,
                    ["error"] = "exception",
                    ["message"] = ex.Message
                }.ToString());
            }
        }

        private async Task HandleSetStorageDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: set_storage_data");

            try
            {
                if (data is not JObject obj)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.SET_STORAGE_DATA,
                        ["success"] = false,
                        ["error"] = "invalid_data"
                    }.ToString());
                    return;
                }

                var keyToken = obj["key"];
                var valueToken = obj["value"];

                if (keyToken is null || valueToken is null)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.SET_STORAGE_DATA,
                        ["success"] = false,
                        ["error"] = "missing_key_or_value"
                    }.ToString());
                    return;
                }

                static bool TryConvertSettingValue(JToken token, out object? value, out string? error)
                {
                    value = null;
                    error = null;

                    switch (token.Type)
                    {
                        case JTokenType.String:
                            value = (string?)token;
                            return true;

                        case JTokenType.Boolean:
                            value = token.Value<bool>();
                            return true;

                        case JTokenType.Integer:
                            // RoamingSettings supports Int32 (not Int64).
                            var l = token.Value<long>();
                            if (l < int.MinValue || l > int.MaxValue)
                            {
                                error = "integer_out_of_range_int32";
                                return false;
                            }
                            value = (int)l;
                            return true;

                        case JTokenType.Float:
                            // RoamingSettings supports Double.
                            value = token.Value<double>();
                            return true;

                        case JTokenType.Null:
                            value = null;
                            return true;

                        default:
                            error = "unsupported_value_type";
                            return false;
                    }
                }

                static bool TryGetValidKey(JToken token, out string? key)
                {
                    key = null;
                    if (token.Type != JTokenType.String) return false;
                    key = token.Value<string?>();
                    return !string.IsNullOrWhiteSpace(key);
                }

                var root = await ReadSaveRootAsync().ConfigureAwait(false);

                // single key/value
                if (TryGetValidKey(keyToken, out var singleKey))
                {
                    if (!TryConvertSettingValue(valueToken, out var value, out var err))
                    {
                        Reply(sender, new JObject
                        {
                            ["action"] = ActionName.SET_STORAGE_DATA,
                            ["success"] = false,
                            ["error"] = err ?? "invalid_value"
                        }.ToString());
                        return;
                    }

                    root[singleKey!] = value is null ? JValue.CreateNull() : JToken.FromObject(value);
                    await WriteSaveRootAsync(root).ConfigureAwait(false);

                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.SET_STORAGE_DATA,
                        ["success"] = true
                    }.ToString());
                    return;
                }

                if (keyToken is JArray keysArr && valueToken is JArray valuesArr)
                {
                    if (keysArr.Count != valuesArr.Count)
                    {
                        Reply(sender, new JObject
                        {
                            ["action"] = ActionName.SET_STORAGE_DATA,
                            ["success"] = false,
                            ["error"] = "keys_values_length_mismatch"
                        }.ToString());
                        return;
                    }

                    for (int i = 0; i < keysArr.Count; i++)
                    {
                        if (!TryGetValidKey(keysArr[i], out var k))
                        {
                            Reply(sender, new JObject
                            {
                                ["action"] = ActionName.SET_STORAGE_DATA,
                                ["success"] = false,
                                ["error"] = "invalid_key",
                                ["index"] = i
                            }.ToString());
                            return;
                        }

                        if (!TryConvertSettingValue(valuesArr[i], out var v, out var err))
                        {
                            Reply(sender, new JObject
                            {
                                ["action"] = ActionName.SET_STORAGE_DATA,
                                ["success"] = false,
                                ["error"] = err ?? "invalid_value",
                                ["index"] = i
                            }.ToString());
                            return;
                        }

                        root[k!] = v is null ? JValue.CreateNull() : JToken.FromObject(v);
                    }

                    await WriteSaveRootAsync(root).ConfigureAwait(false);

                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.SET_STORAGE_DATA,
                        ["success"] = true
                    }.ToString());
                    return;
                }

                Reply(sender, new JObject
                {
                    ["action"] = ActionName.SET_STORAGE_DATA,
                    ["success"] = false,
                    ["error"] = "unsupported_format"
                }.ToString());
            }
            catch (Exception ex)
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.SET_STORAGE_DATA,
                    ["success"] = false,
                    ["error"] = "exception",
                    ["message"] = ex.Message
                }.ToString());
            }
        }

        private async Task HandleDeleteStorageDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: delete_storage_data");

            try
            {
                var root = await ReadSaveRootAsync().ConfigureAwait(false);

                if (data is null)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.DELETE_STORAGE_DATA,
                        ["success"] = false,
                        ["error"] = "missing_data"
                    }.ToString());
                    return;
                }

                if (data.Type == JTokenType.String)
                {
                    var key = (string?)data;

                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        root.Remove(key!);
                        await WriteSaveRootAsync(root).ConfigureAwait(false);
                    }

                    Reply(sender, new JObject {
                        ["action"] = ActionName.DELETE_STORAGE_DATA,
                        ["success"] = true,
                        ["data"] = new JObject {
                            ["key"] = key
                        }
                    }.ToString());
                    return;
                }

                if (data is JArray arr)
                {
                    foreach (var token in arr)
                    {
                        if (token.Type != JTokenType.String) continue;
                        var k = (string?)token;
                        if (string.IsNullOrWhiteSpace(k)) continue;
                        root.Remove(k!);
                    }

                    await WriteSaveRootAsync(root).ConfigureAwait(false);

                    Reply(sender, new JObject { ["action"] = ActionName.DELETE_STORAGE_DATA, ["success"] = true }.ToString());
                    return;
                }

                Reply(sender, new JObject { ["action"] = ActionName.DELETE_STORAGE_DATA, ["success"] = false, ["error"] = "invalid_data" }.ToString());
            }
            catch (Exception ex)
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.DELETE_STORAGE_DATA,
                    ["success"] = false,
                    ["error"] = "exception",
                    ["message"] = ex.Message
                }.ToString());
            }
        }
    }
}