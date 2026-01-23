using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
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

                var purchases = new JArray();
                foreach (var kv in license.AddOnLicenses)
                {
                    var storeId = kv.Key;
                    var lic = kv.Value;

                    purchases.Add(new JObject
                    {
                        ["id"] = storeId,
                        ["isActive"] = lic.IsActive,
                        ["expirationDate"] = lic.ExpirationDate
                    });
                }

                Reply(sender, new JObject
                {
                    ["action"] = ActionName.GET_PURCHASES,
                    ["success"] = true,
                    ["data"] = purchases
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
    }
}