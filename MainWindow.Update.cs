using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        // Windows' own update check is a once-a-day scheduled task, so a package that is already
        // live in the Store can take days to reach players. Asking the Store ourselves at launch
        // cuts that to a single session — in two deliberately separate steps:
        //   1. while the player plays, only DOWNLOAD the new version (silently, in the background);
        //   2. INSTALL it on exit (OnFormClosing), once the player has already chosen to leave.
        // The split matters: Microsoft's guidance treats the install step as the one that may
        // restart or terminate the app ("confirm whether your app can restart now and then
        // install the updates"), so it must never run mid-session. The next launch then simply
        // starts the new version, with no extra wait.
        private IReadOnlyList<StorePackageUpdate>? _downloadedStoreUpdates;

        private async Task CheckForStorePackageUpdatesAsync()
        {
            // Only a Store-installed package can see (or silently download) updates. For the
            // self-signed sideload builds produced by build.ps1 every call below is a no-op,
            // so skip it — same guard the rating prompt uses.
            if (!IsStoreSignedPackage()) return;

            try
            {
                AppendLog($"Update check: running {FormatVersion(Package.Current.Id.Version)}.");

                // The check itself is one small request, so it runs right away — even a short
                // session then leaves a record in the log of whether an update exists.
                var updates = await _store.GetAppAndOptionalStorePackageUpdatesAsync();

                if (updates is null || updates.Count == 0)
                {
                    AppendLog("Update check: already up to date.");
                    return;
                }

                foreach (var update in updates)
                {
                    AppendLog($"Update check: found {update.Package.Id.Name} " +
                              $"{FormatVersion(update.Package.Id.Version)} (mandatory={update.Mandatory}).");
                }

                // The download is ~170 MB; keep it off the first minute so it does not compete
                // with asset and ad loading.
                await Task.Delay(TimeSpan.FromSeconds(UpdateDownloadDelaySeconds));

                if (_closing || IsDisposed) return;

                // Deliberately silent-only: the Request* overloads put Store UI on top of the game.
                // If silent download is off (auto-updates disabled, metered network) we leave the
                // update to Windows' own scheduler instead.
                if (!_store.CanSilentlyDownloadStorePackageUpdates)
                {
                    AppendLog("Update check: silent download unavailable; leaving it to Windows.");
                    return;
                }

                AppendLog("Update check: downloading in the background.");

                var result = await _store.TrySilentDownloadStorePackageUpdatesAsync(updates);

                foreach (var status in result.StorePackageUpdateStatuses.Where(
                             s => s.PackageUpdateState != StorePackageUpdateState.Completed))
                {
                    AppendLog($"Update check: {status.PackageFamilyName} download ended as {status.PackageUpdateState}.");
                }

                // Anything other than Completed (no Wi-Fi, low battery, transient Store error) is
                // simply retried on the next launch; the Store keeps its own download queue.
                if (result.OverallState != StorePackageUpdateState.Completed)
                {
                    AppendLog($"Update check: download not completed (state={result.OverallState}).");
                    return;
                }

                _downloadedStoreUpdates = updates;
                AppendLog("Update check: downloaded — it will be installed when the game exits.");
            }
            catch (Exception ex)
            {
                AppendLog($"Update check failed: {ex.Message} (0x{ex.HResult:X8})");
            }
        }

        // Called from OnFormClosing after the page has flushed. Installing the app's own package
        // is the step that may restart/terminate the process, so it only ever happens here: the
        // window is hidden first so nothing is visible, and if the OS ends the process to apply
        // the update we were exiting anyway.
        private async Task InstallDownloadedStoreUpdatesAsync()
        {
            var updates = _downloadedStoreUpdates;
            if (updates is null) return;
            _downloadedStoreUpdates = null;

            Hide();
            AppendLog("Update install: applying the downloaded update on exit.");

            // The bits are already local, so this is install-only (it does not download again).
            var install = _store.TrySilentDownloadAndInstallStorePackageUpdatesAsync(updates).AsTask();

            // Never keep a hidden process alive indefinitely: past the timeout the update stays in
            // the Store's queue for Windows to finish, or we try again on the next exit.
            var timeout = Task.Delay(TimeSpan.FromSeconds(UpdateInstallTimeoutSeconds));
            if (await Task.WhenAny(install, timeout) == timeout)
            {
                AppendLog("Update install: still running after timeout; leaving it to Windows.");
                return;
            }

            var result = await install;
            AppendLog($"Update install: finished with state={result.OverallState}.");
        }

        private bool IsStoreSignedPackage()
        {
            try
            {
                var kind = Package.Current.SignatureKind;
                if (kind == PackageSignatureKind.Store) return true;

                AppendLog($"Update check: skipped (SignatureKind={kind}).");
                return false;
            }
            catch (Exception ex)
            {
                // Unpackaged / Package.Current unavailable.
                AppendLog($"Update check: skipped (no package identity: {ex.Message}).");
                return false;
            }
        }

        private static string FormatVersion(PackageVersion v) => $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
    }
}
