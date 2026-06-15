using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Windows.Services.Store;
using Windows.Storage;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private static readonly HttpClient Http = new();

        private const string PublisherUserFileName = "publisherUser.json";

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
                var kinds = new[] { "Consumable", "Durable", "UnmanagedConsumable" };

                var result = await _store.GetUserCollectionAsync(kinds);

                if (result.ExtendedError is not null && result.ExtendedError.HResult != 0)
                {
                    Reply(sender, new JObject
                    {
                        ["action"] = ActionName.GET_PURCHASES,
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
                    ["action"] = ActionName.GET_PURCHASES,
                    ["success"] = true,
                    ["data"] = items
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

            Action work = async () =>
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
                        var purchaseResult = await _store.RequestPurchaseAsync(storeId);

                        var isSuccess =
                            purchaseResult.Status == StorePurchaseStatus.Succeeded ||
                            purchaseResult.Status == StorePurchaseStatus.AlreadyPurchased;

                        var responseData = new JObject
                        {
                            ["id"] = storeId,
                            ["productId"] = storeId,
                            ["status"] = purchaseResult.Status.ToString(),
                            ["extendedError"] = purchaseResult.ExtendedError is null || purchaseResult.ExtendedError.HResult == 0
                                ? null
                                : purchaseResult.ExtendedError.ToString(),
                        };

                        if (isSuccess)
                        {
                            try
                            {
                                var publisherUserId = await GetOrCreatePublisherUserIdAsync().ConfigureAwait(true);

                                var config = GetConfiguration();
                                var clientId = config.ClientId;

                                responseData["clientId"] = clientId;

                                var serviceTicket = await GetServiceTicketAsync(clientId).ConfigureAwait(true);

                                if (!string.IsNullOrWhiteSpace(serviceTicket))
                                {
                                    var customerCollectionsId = await _store.GetCustomerCollectionsIdAsync(serviceTicket, publisherUserId);
                                    responseData["customerCollectionsId"] = customerCollectionsId;
                                }
                                else
                                {
                                    responseData["customerCollectionsId"] = null;
                                }
                            }
                            catch (Exception ex)
                            {
                                responseData["serviceTicketError"] = ex.Message;
                            }
                        }

                        Reply(sender, new JObject
                        {
                            ["action"] = ActionName.PURCHASE,
                            ["success"] = isSuccess,
                            ["data"] = responseData
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
            };

            if (InvokeRequired) BeginInvoke(work);
            else work();

            await tcs.Task;
        }

        private static async Task<string> GetOrCreatePublisherUserIdAsync()
        {
            var folder = ApplicationData.Current.LocalFolder;
            var path = Path.Combine(folder.Path, PublisherUserFileName);

            JObject root;

            if (File.Exists(path))
            {
                var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                root = string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
            }
            else
            {
                root = new JObject();
            }

            var publisherUserId = (string?)root["publisherUserId"];

            if (string.IsNullOrWhiteSpace(publisherUserId))
            {
                publisherUserId = Guid.NewGuid().ToString("N");
                root["publisherUserId"] = publisherUserId;

                var json = root.ToString(Newtonsoft.Json.Formatting.Indented);
                await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
            }

            return publisherUserId;
        }

        private static async Task<string?> GetServiceTicketAsync(string clientId)
        {
            var payload = new JObject
            {
                ["clientId"] = clientId,
            }.ToString(Newtonsoft.Json.Formatting.None);

            var endpoint = GetConfiguration().ServiceTicketEndpoint;

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Service-ticket HTTP {(int)resp.StatusCode}: {body}");

            var json = JObject.Parse(body);
            return (string?)json["serviceTicket"];
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
    }
}