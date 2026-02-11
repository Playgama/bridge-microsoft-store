using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow
    {
        private const string MsaClientId = "";

        private static readonly string[] MsaScopes = new[]
        {
            "openid",
            "profile",
            "email",
            "User.Read"
        };

        private async Task HandleAuthorizeAsync(CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: authorize (MSAL/WAM)");

            try
            {
                var pca = PublicClientApplicationBuilder
                    .Create(MsaClientId)
                    .WithAuthority(AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
                    .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
                    .Build();

                AuthenticationResult result;

                try
                {
                    var account = (await pca.GetAccountsAsync().ConfigureAwait(true)).FirstOrDefault();

                    result = await pca
                        .AcquireTokenSilent(MsaScopes, account)
                        .ExecuteAsync()
                        .ConfigureAwait(true);
                }
                catch (MsalUiRequiredException)
                {
                    result = await pca
                        .AcquireTokenInteractive(MsaScopes)
                        .WithPrompt(Prompt.SelectAccount)
                        .WithParentActivityOrWindow(_hwnd)
                        .ExecuteAsync()
                        .ConfigureAwait(true);
                }

                Reply(sender, new JObject
                {
                    ["action"] = ActionName.AUTHORIZE_PLAYER,
                    ["success"] = true,
                    ["data"] = new JObject
                    {
                        ["idToken"] = result.IdToken,

                        ["expiresOn"] = result.ExpiresOn.UtcDateTime,
                        ["tenantId"] = result.TenantId,
                        ["scopes"] = new JArray(result.Scopes),
                        ["account"] = result.Account is null ? null : new JObject
                        {
                            ["username"] = result.Account.Username,
                            ["homeAccountId"] = result.Account.HomeAccountId?.Identifier
                        }
                    }
                }.ToString());
            }
            catch (Exception ex)
            {
                Reply(sender, new JObject
                {
                    ["action"] = ActionName.AUTHORIZE_PLAYER,
                    ["success"] = false,
                    ["error"] = "exception",
                    ["message"] = ex.Message,
                    ["hresult"] = $"0x{ex.HResult:X8}"
                }.ToString());
            }
        }
    }
}