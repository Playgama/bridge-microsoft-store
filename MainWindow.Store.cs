using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow
    {
        private static string[] ExtractStoreIdsFromData(JToken? data)
        {
            if (data is not JArray arr)
            {
                return Array.Empty<string>();
            }

            return arr
                .Where(x => x.Type == JTokenType.String)
                .Select(x => (string?)x)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private async Task HandleGetCatalogAsync(CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: get_catalog");

            var requestedStoreIds = ExtractStoreIdsFromData(data);

            try
            {
                var kinds = new[] { "Consumable", "Durable", "UnmanagedConsumable" };

                StoreProductQueryResult result = requestedStoreIds.Length > 0
                    ? await _store.GetStoreProductsAsync(kinds, requestedStoreIds)
                    : await _store.GetAssociatedStoreProductsAsync(kinds);

                if (result.ExtendedError is not null && result.ExtendedError.HResult != 0)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.GET_CATALOG,
                        ["success"] = false,
                        ["error"] = "store_error",
                        ["extendedError"] = result.ExtendedError.ToString(),
                    }.ToString());
                    return;
                }

                var items = new JArray();
                foreach (var p in result.Products.Values)
                {
                    items.Add(new JObject
                    {
                        ["id"] = p.StoreId,
                        ["productKind"] = p.ProductKind,
                        ["title"] = p.Title,
                        ["description"] = p.Description,
                        ["inAppOfferToken"] = p.InAppOfferToken,
                        ["hasDigitalDownload"] = p.HasDigitalDownload,
                        ["price"] = new JObject
                        {
                            ["formattedBasePrice"] = p.Price?.FormattedBasePrice,
                            ["formattedPrice"] = p.Price?.FormattedPrice,
                            ["currencyCode"] = p.Price?.CurrencyCode,
                        }
                    });
                }

                Reply(sender, new JObject
                {
                    ["action"] = ActionName.GET_CATALOG,
                    ["success"] = true,
                    ["data"] = items
                }.ToString());
            }
            catch (Exception ex)
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.GET_CATALOG,
                    ["success"] = false,
                    ["error"] = "exception",
                    ["message"] = ex.Message
                }.ToString());
            }
        }

        private async Task HandleGetPurchasesAsync(CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: get_purchases");

            try
            {
                // 1) Load full catalog (all add-on types)
                var kinds = new[] { "Consumable", "Durable", "UnmanagedConsumable" };
                var catalogResult = await _store.GetAssociatedStoreProductsAsync(kinds);

                if (catalogResult.ExtendedError is not null && catalogResult.ExtendedError.HResult != 0)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.GET_PURCHASES,
                        ["success"] = false,
                        ["error"] = "store_error",
                        ["extendedError"] = catalogResult.ExtendedError.ToString(),
                    }.ToString());
                    return;
                }

                // Build a map from StoreId -> product info (kind/title/...)
                var productsById = new Dictionary<string, StoreProduct>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in catalogResult.Products.Values)
                {
                    if (!string.IsNullOrWhiteSpace(p.StoreId))
                        productsById[p.StoreId] = p;
                }

                // 2) Get licenses (old implementation)
                var license = await _store.GetAppLicenseAsync();
                if (license is null)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.GET_PURCHASES,
                        ["success"] = false,
                        ["error"] = "no_license"
                    }.ToString());
                    return;
                }

                var response = new JArray();

                // 3) Merge: start with everything from catalog and enrich with license/balance
                foreach (var kv in productsById)
                {
                    var storeId = kv.Key;
                    var product = kv.Value;

                    var productKind = NormalizeProductKind(product.ProductKind);

                    var item = new JObject
                    {
                        ["id"] = storeId,
                        ["productKind"] = productKind,
                        ["title"] = product.Title,
                    };

                    // License info (durable/subscription typically shows up here; consumables often won't)
                    if (license.AddOnLicenses.TryGetValue(storeId, out var addOnLicense))
                    {
                        item["license"] = new JObject
                        {
                            ["isActive"] = addOnLicense.IsActive,
                            ["expirationDate"] = addOnLicense.ExpirationDate
                        };
                    }

                    // Consumable balance (source of truth for consumables)
                    if (IsConsumableKind(productKind))
                    {
                        try
                        {
                            var bal = await _store.GetConsumableBalanceRemainingAsync(storeId);

                            item["consumable"] = new JObject
                            {
                                ["status"] = bal.Status.ToString(),
                                ["balanceRemaining"] = bal.BalanceRemaining,
                                ["trackingId"] = bal.TrackingId.ToString(),
                                ["extendedError"] = bal.ExtendedError is null || bal.ExtendedError.HResult == 0 ? null : bal.ExtendedError.ToString(),
                            };
                        }
                        catch (Exception ex)
                        {
                            // Do not fail whole response; just annotate item.
                            item["consumable"] = new JObject
                            {
                                ["error"] = "exception",
                                ["message"] = ex.Message,
                                ["hresult"] = $"0x{ex.HResult:X8}"
                            };
                        }
                    }

                    response.Add(item);
                }

                // 4) Edge case: license has add-ons not present in catalog response
                foreach (var kv in license.AddOnLicenses)
                {
                    var storeId = kv.Key;
                    if (productsById.ContainsKey(storeId))
                        continue;

                    var lic = kv.Value;

                    response.Add(new JObject
                    {
                        ["id"] = storeId,
                        ["productKind"] = "Unknown",
                        ["license"] = new JObject
                        {
                            ["isActive"] = lic.IsActive,
                            ["expirationDate"] = lic.ExpirationDate
                        }
                    });
                }

                Reply(sender, new JObject
                {
                    ["action"] = ActionName.GET_PURCHASES,
                    ["success"] = true,
                    ["data"] = response
                }.ToString());
            }
            catch (Exception ex)
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.GET_PURCHASES,
                    ["success"] = false,
                    ["error"] = "exception",
                    ["message"] = ex.Message
                }.ToString());
            }
        }

        private async Task HandlePurchaseAsync(CoreWebView2 sender, JToken? data)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    AppendLog("Handler: purchase");

                    var storeId = data?.Type == JTokenType.String ? (string?)data : null;

                    if (string.IsNullOrWhiteSpace(storeId))
                    {
                        Reply(sender, new JObject
                        {
                            ["action"] = ActionName.PURCHASE,
                            ["success"] = false,
                            ["error"] = "missing_storeId"
                        }.ToString());
                        tcs.SetResult();
                        return;
                    }

                    try
                    {
                        var result = await _store.RequestPurchaseAsync(storeId);

                        Reply(sender, new JObject
                        {
                            ["action"] = ActionName.PURCHASE,
                            ["success"] = result.Status == StorePurchaseStatus.Succeeded ||
                                     result.Status == StorePurchaseStatus.AlreadyPurchased,
                            ["data"] = new JObject
                            {
                                ["id"] = storeId,
                                ["status"] = result.Status.ToString(),
                                ["extendedError"] = result.ExtendedError is null || result.ExtendedError.HResult == 0 ? null : result.ExtendedError.ToString(),
                            }
                        }.ToString());
                    }
                    catch (Exception ex)
                    {
                        Reply(sender, new JObject
                        {
                            ["action"] = ActionName.PURCHASE,
                            ["success"] = false,
                            ["error"] = "exception",
                            ["message"] = ex.Message,
                            ["data"] = storeId
                        }.ToString());
                    }

                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            {
                tcs.SetException(new InvalidOperationException("Failed to marshal purchase to UI thread (DispatcherQueue.TryEnqueue returned false)."));
            }

            await tcs.Task;
        }

        private async Task HandleConsumePurchaseAsync(CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: consume_purchase");

            var storeId = data?.Type == JTokenType.String ? (string?)data : null;

            if (string.IsNullOrWhiteSpace(storeId))
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.CONSUME_PURCHASE,
                    ["success"] = false,
                    ["error"] = "missing_storeId"
                }.ToString());
                return;
            }

            const uint quantity = 1;
            var trackingId = Guid.NewGuid();

            try
            {
                var result = await _store.ReportConsumableFulfillmentAsync(storeId, quantity, trackingId);

                Reply(sender, new JObject
                {
                    ["action"] = ActionName.CONSUME_PURCHASE,
                    ["success"] = result.Status == StoreConsumableStatus.Succeeded,
                    ["data"] = new JObject
                    {
                        ["id"] = storeId,
                        ["quantity"] = quantity,
                        ["status"] = result.Status.ToString(),
                        ["balanceRemaining"] = result.BalanceRemaining,
                        ["trackingId"] = trackingId.ToString(),
                        ["extendedError"] = result.ExtendedError is null || result.ExtendedError.HResult == 0 ? null : result.ExtendedError.ToString(),
                    }
                }.ToString());
            }
            catch (Exception ex)
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.CONSUME_PURCHASE,
                    ["success"] = false,
                    ["error"] = "exception",
                    ["message"] = ex.Message,
                    ["data"] = storeId
                }.ToString());
            }
        }

        private static string NormalizeProductKind(string? productKind)
        {
            // Keep exactly Store's kind strings used elsewhere in your code.
            if (string.IsNullOrWhiteSpace(productKind))
                return "Unknown";

            return productKind;
        }

        private static bool IsConsumableKind(string? productKind)
            => string.Equals(productKind, "Consumable", StringComparison.OrdinalIgnoreCase);
    }
}