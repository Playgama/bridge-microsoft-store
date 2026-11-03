using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private const string StorageMigrationFolderName = "storage-migration";
        private const string StorageMigrationPageName = "__pg_migrate.html";
        private const string StorageMigrationPlanName = "plan.json";
        private const int StorageMigrationIdleTimeoutSeconds = 20;
        private const int StorageMigrationTotalTimeoutSeconds = 120;
        private const int MaxStorageMigrationAttempts = 3;

        private static readonly string[] StorageMigrationSkippedDatabases = { "UnityCache", "EM_PRELOAD_CACHE" };
        private static readonly string[] StorageMigrationSkippedLocalStoragePrefixes = { "_pubcid", "id5id" };

        private static string[] StorageMigrationHosts => LegacyAppAssetsHosts.Append(AppAssetsHost).ToArray();

        private async Task<string> PrepareGameHostAsync(CoreWebView2 web)
        {
            ApplicationDataContainer settings;
            try
            {
                settings = ApplicationData.Current.LocalSettings;
            }
            catch (Exception ex)
            {
                AppendLog($"Storage migration skipped (no app data: {ex.Message})");
                return AppAssetsHost;
            }

            if (settings.Values.TryGetValue(LocalSettingKey.StorageMigratedHost, out var migratedHost)
                && migratedHost is string completedHost
                && completedHost == AppAssetsHost)
                return AppAssetsHost;

            if (settings.Values.TryGetValue(LocalSettingKey.StorageMigrationAbandoned, out var abandoned) && abandoned is true)
                return LegacyAppAssetsHosts[0];

            var attempts = settings.Values.TryGetValue(LocalSettingKey.StorageMigrationAttempts, out var storedAttempts)
                && storedAttempts is int count ? count : 0;

            if (attempts >= MaxStorageMigrationAttempts)
            {
                AppendLog($"Storage migration: giving up after {attempts} failed attempts, staying on {LegacyAppAssetsHosts[0]}");
                settings.Values[LocalSettingKey.StorageMigrationAbandoned] = true;
                return LegacyAppAssetsHosts[0];
            }

            settings.Values[LocalSettingKey.StorageMigrationAttempts] = attempts + 1;
            AppendLog($"Storage migration: attempt {attempts + 1} of {MaxStorageMigrationAttempts}");

            var migrated = false;
            try
            {
                migrated = await MigrateStorageAsync(web);
            }
            catch (Exception ex)
            {
                AppendLog($"Storage migration failed: {ex.Message}");
            }
            finally
            {
                foreach (var host in StorageMigrationHosts)
                {
                    try
                    {
                        web.ClearVirtualHostNameToFolderMapping(host);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Storage migration: could not unmap {host} ({ex.Message})");
                    }
                }
            }

            if (migrated)
            {
                settings.Values[LocalSettingKey.StorageMigratedHost] = AppAssetsHost;
                DeleteStorageMigrationFolder();
                AppendLog($"Storage migration: done, game host is {AppAssetsHost}");
                return AppAssetsHost;
            }

            if (_closing)
            {
                settings.Values[LocalSettingKey.StorageMigrationAttempts] = attempts;
                AppendLog("Storage migration: interrupted by closing the window, will retry on next launch");
                return LegacyAppAssetsHosts[0];
            }

            AppendLog($"Storage migration: attempt {attempts + 1} failed, starting the game on {LegacyAppAssetsHosts[0]} this time");
            return LegacyAppAssetsHosts[0];
        }

        private async Task<bool> MigrateStorageAsync(CoreWebView2 web)
        {
            var folder = StorageMigrationFolderPath();

            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);

            Directory.CreateDirectory(folder);

            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Migration", StorageMigrationPageName),
                Path.Combine(folder, StorageMigrationPageName));

            await File.WriteAllTextAsync(
                Path.Combine(folder, StorageMigrationPlanName),
                BuildStorageMigrationPlan().ToString(Formatting.None));

            foreach (var host in StorageMigrationHosts)
                web.SetVirtualHostNameToFolderMapping(host, folder, CoreWebView2HostResourceAccessKind.Deny);

            using var total = new CancellationTokenSource(TimeSpan.FromSeconds(StorageMigrationTotalTimeoutSeconds));

            foreach (var host in LegacyAppAssetsHosts)
            {
                if (_closing)
                    return false;

                var dumpPath = Path.Combine(folder, StorageMigrationDumpName(host));

                if (!await RunStorageMigrationPageAsync(web, host, "export", dumpPath, total.Token))
                    return false;
            }

            if (_closing)
                return false;

            return await RunStorageMigrationPageAsync(web, AppAssetsHost, "import", null, total.Token);
        }

        private async Task<bool> RunStorageMigrationPageAsync(CoreWebView2 web, string host, string mode, string? dumpPath, CancellationToken total)
        {
            var pageUrl = $"https://{host}/{StorageMigrationPageName}";
            var idleTimeout = TimeSpan.FromSeconds(StorageMigrationIdleTimeoutSeconds);
            var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var idle = new CancellationTokenSource(idleTimeout);
            using var writer = dumpPath is null ? null : new StreamWriter(dumpPath, false, new UTF8Encoding(false)) { NewLine = "\n" };
            ulong? navigationId = null;
            long written = 0;

            void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
            {
                if (navigationId is null)
                {
                    if (args.Uri.StartsWith(pageUrl, StringComparison.OrdinalIgnoreCase))
                        navigationId = args.NavigationId;
                    return;
                }

                if (args.NavigationId == navigationId)
                    return;

                AppendLog($"Storage migration ({mode} {host}): interrupted by navigation to {args.Uri}");
                result.TrySetResult(false);
            }

            void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (args.NavigationId != navigationId || args.IsSuccess)
                    return;

                AppendLog($"Storage migration ({mode} {host}): page failed to load ({args.WebErrorStatus})");
                result.TrySetResult(false);
            }

            void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
            {
                if (!args.Source.StartsWith(pageUrl, StringComparison.OrdinalIgnoreCase))
                    return;

                try
                {
                    idle.CancelAfter(idleTimeout);

                    var text = args.TryGetWebMessageAsString();
                    var type = ReadStorageMigrationMessageType(text);

                    switch (type)
                    {
                        case "progress":
                            return;

                        case "error":
                            AppendLog($"Storage migration ({mode} {host}) failed: {text}");
                            result.TrySetResult(false);
                            return;

                        case "done":
                            AppendLog($"Storage migration: imported into {host}: {text}");
                            result.TrySetResult(true);
                            return;

                        case "ls":
                        case "db":
                        case "records":
                        case "end":
                            if (writer is null)
                            {
                                AppendLog($"Storage migration ({mode} {host}): unexpected export message");
                                result.TrySetResult(false);
                                return;
                            }

                            writer.WriteLine(text);
                            written += text.Length;

                            if (type == "end")
                            {
                                writer.Flush();
                                AppendLog($"Storage migration: exported {host} ({written:N0} chars): {text}");
                                result.TrySetResult(true);
                            }
                            return;

                        default:
                            AppendLog($"Storage migration ({mode} {host}): unknown message type '{type}'");
                            result.TrySetResult(false);
                            return;
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Storage migration ({mode} {host}): {ex.Message}");
                    result.TrySetResult(false);
                }
            }

            web.NavigationStarting += OnNavigationStarting;
            web.NavigationCompleted += OnNavigationCompleted;
            web.WebMessageReceived += OnMessage;
            using var idleRegistration = idle.Token.Register(() => result.TrySetResult(false));
            using var totalRegistration = total.Register(() => result.TrySetResult(false));

            try
            {
                web.Navigate($"{pageUrl}?mode={mode}");

                var success = await result.Task;

                if (!success && idle.IsCancellationRequested)
                    AppendLog($"Storage migration ({mode} {host}): no progress for {StorageMigrationIdleTimeoutSeconds} s");
                else if (!success && total.IsCancellationRequested)
                    AppendLog($"Storage migration ({mode} {host}): took longer than {StorageMigrationTotalTimeoutSeconds} s in total");

                return success;
            }
            finally
            {
                web.NavigationStarting -= OnNavigationStarting;
                web.NavigationCompleted -= OnNavigationCompleted;
                web.WebMessageReceived -= OnMessage;
            }
        }

        private void DeleteStorageMigrationFolder()
        {
            try
            {
                var folder = StorageMigrationFolderPath();

                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex)
            {
                AppendLog($"Storage migration: could not delete {StorageMigrationFolderName} ({ex.Message})");
            }
        }

        private static string? ReadStorageMigrationMessageType(string text)
        {
            using var reader = new JsonTextReader(new StringReader(text));

            if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                return null;

            if (!reader.Read() || reader.TokenType != JsonToken.PropertyName || (string?)reader.Value != "t")
                return null;

            return reader.ReadAsString();
        }

        private static JObject BuildStorageMigrationPlan() => new()
        {
            ["target"] = new JObject
            {
                ["host"] = AppAssetsHost,
                ["idbfsHash"] = IdbfsHash(AppAssetsHost),
            },
            ["sources"] = new JArray(LegacyAppAssetsHosts.Select(host => new JObject
            {
                ["host"] = host,
                ["file"] = StorageMigrationDumpName(host),
                ["idbfsHash"] = IdbfsHash(host),
            })),
            ["skipDatabases"] = new JArray(StorageMigrationSkippedDatabases),
            ["skipLocalStoragePrefixes"] = new JArray(StorageMigrationSkippedLocalStoragePrefixes),
        };

        private static string StorageMigrationFolderPath() =>
            Path.Combine(ApplicationData.Current.LocalFolder.Path, StorageMigrationFolderName);

        private static string StorageMigrationDumpName(string host) => $"{host}.ndjson";

        private static string IdbfsHash(string host) =>
            Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"https://{host}"))).ToLowerInvariant();
    }
}
