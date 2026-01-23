using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;
using Windows.Storage;
using Windows.System;

namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow
    {
        private static int IncrementLaunchCount()
        {
            var settings = ApplicationData.Current.LocalSettings;
            var current = settings.Values.TryGetValue(LocalSettingKey.LaunchCount, out var v) && v is int i ? i : 0;
            var next = current + 1;
            settings.Values[LocalSettingKey.LaunchCount] = next;
            return next;
        }

        private static int GetLaunchCount()
        {
            var settings = ApplicationData.Current.LocalSettings;
            return settings.Values.TryGetValue(LocalSettingKey.LaunchCount, out var v) && v is int i ? i : 0;
        }

        private static bool GetRateDialogEverShown()
        {
            var settings = ApplicationData.Current.LocalSettings;
            return settings.Values.TryGetValue(LocalSettingKey.RateDialogEverShown, out var v) && v is bool b && b;
        }

        private static void SetRateDialogEverShown()
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[LocalSettingKey.RateDialogEverShown] = true;
        }

        private static bool IsEligibleToShowRateDialog(int launchCount)
        {
            if (GetRateDialogEverShown())
            {
                return false;
            }

            return launchCount >= MinLaunchCountToPrompt;
        }

        private async Task ShowRatePromptAsync(XamlRoot xamlRoot, int launchCount)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    SetRateDialogEverShown();

                    var pkg = Package.Current;

                    var dialog = new ContentDialog
                    {
                        Title = "Do you like the game?",
                        PrimaryButtonText = "😍",
                        CloseButtonText = "😒",
                        XamlRoot = xamlRoot
                    };

                    var dialogResult = await dialog.ShowAsync();

                    if (dialogResult != ContentDialogResult.Primary)
                    {
                        tcs.SetResult();
                        return;
                    }

                    // Prefer Store rating prompt only for Store-signed packages.
                    if (pkg.SignatureKind != PackageSignatureKind.Store)
                    {
                        AppendLog($"Skip RequestRateAndReviewAppAsync (SignatureKind={pkg.SignatureKind}). Using Store URI fallback.");
                        await LaunchStoreReviewFallbackAsync();
                        tcs.SetResult();
                        return;
                    }

                    await _store.RequestRateAndReviewAppAsync();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            {
                tcs.SetException(new InvalidOperationException("Failed to show rate prompt dialog (DispatcherQueue.TryEnqueue returned false)."));
            }

            await tcs.Task;
        }

        private async Task LaunchStoreReviewFallbackAsync()
        {
            try
            {
                var pfn = Package.Current.Id.FamilyName;
                var uri = new Uri($"ms-windows-store://review/?PFN={pfn}");
                _ = await Launcher.LaunchUriAsync(uri);
            }
            catch (Exception fallbackEx)
            {
                AppendLog($"Fallback review launch failed. HResult=0x{fallbackEx.HResult:X8}, Message={fallbackEx.Message}");
            }
        }

        private async Task HandleRateAsync(CoreWebView2 sender, JToken? data)
        {
            AppendLog("Handler: rate");

            var launchCount = GetLaunchCount();

            if (IsEligibleToShowRateDialog(launchCount))
            {
                await ShowRatePromptAsync(GameWebView.XamlRoot, launchCount);
            }

            Reply(sender, new JObject
            {
                ["action"] = ActionName.RATE,
                ["success"] = true,
                ["data"] = data
            }.ToString());
        }
    }
}