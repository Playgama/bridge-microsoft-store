using System;
using System.Diagnostics;
using System.IO;
using Windows.Storage;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private static readonly object _logLock = new();
        private static string? _logPath;

        // host-log.txt lives in the packaged app's LocalState folder:
        //   %LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\host-log.txt
        // It captures every Web→Host and Host→Web message, so a purchase reply
        // (with clientId + customerCollectionsId) can be read/copied without DevTools.
        private static string ResolveLogPath()
        {
            try { return Path.Combine(ApplicationData.Current.LocalFolder.Path, "host-log.txt"); }
            catch { return Path.Combine(AppContext.BaseDirectory, "host-log.txt"); }
        }

        private void AppendLog(string text)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}";
            Debug.WriteLine(line);

            try
            {
                lock (_logLock)
                {
                    _logPath ??= ResolveLogPath();

                    // Keep the log from growing without bound (games log storage ops often).
                    if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 5_000_000)
                        File.WriteAllText(_logPath, string.Empty);

                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Logging must never break the app.
            }
        }
    }
}
