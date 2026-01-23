using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow
    {
        private void SetInitialSize(int width, int height)
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new SizeInt32(width, height));
        }
    }
}