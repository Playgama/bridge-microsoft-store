using System;
using System.Drawing;
using System.IO;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        // Use a multi-size favicon.ico if present (best for title bar / taskbar / Alt-Tab),
        // otherwise fall back to converting one of the PNG logos.
        private void SetWindowIcon()
        {
            try
            {
                var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "favicon.ico");
                if (File.Exists(ico))
                {
                    Icon = new Icon(ico);
                    return;
                }
            }
            catch { /* fall through to PNG */ }

            foreach (var name in new[] { "Square44x44Logo.png", "Square150x150Logo.png", "StoreLogo.png" })
            {
                try
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
                    if (!File.Exists(path)) continue;

                    using var bmp = new Bitmap(path);
                    Icon = Icon.FromHandle(bmp.GetHicon());
                    return;
                }
                catch
                {
                    // try the next candidate; fall back to the default icon
                }
            }
        }
    }
}
