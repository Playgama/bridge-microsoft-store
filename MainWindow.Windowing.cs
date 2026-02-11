using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.UI;
namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private void SetWindowIcon(string iconPath)
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.SetIcon(iconPath);

            if (appWindow.TitleBar is not null)
            {
                appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

                appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

                appWindow.TitleBar.ButtonForegroundColor = Colors.White;
                appWindow.TitleBar.ButtonInactiveForegroundColor = Colors.LightGray;

                appWindow.TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
                appWindow.TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
            }
        }
    }
}