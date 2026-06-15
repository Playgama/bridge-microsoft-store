using System;
using System.Drawing;
using System.IO;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        // Use one of the packaged logos as the window / taskbar / Alt-Tab icon.
        private void SetWindowIcon()
        {
            foreach (var name in new[] { "Square44x44Logo.png", "Square150x150Logo.png", "StoreLogo.png" })
            {
                try
                {
                    var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
                    if (!File.Exists(path)) continue;

                    using var bmp = new Bitmap(path);
                    var hIcon = bmp.GetHicon();
                    Icon = Icon.FromHandle(hIcon);
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
