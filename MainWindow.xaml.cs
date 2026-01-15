using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await GameWebView.EnsureCoreWebView2Async();
            GameWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            var htmlPath = Path.Combine(AppContext.BaseDirectory, "game", "index.html");
            if (!File.Exists(htmlPath))
            {
                AppendLog($"Missing file: {htmlPath}");
                GameWebView.NavigateToString("<!doctype html><html><body>game/index.html not found</body></html>");
                return;
            }

            // Для простоты: читаем файл и грузим как строку
            var html = await File.ReadAllTextAsync(htmlPath);
            GameWebView.NavigateToString(html);

            AppendLog($"Loaded: {htmlPath}");
        }

        private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var msg = args.TryGetWebMessageAsString();
            AppendLog($"Web ? Host: {msg}");

            // Пример ответа обратно в web
            var reply = $"Host received: {msg}";
            sender.PostWebMessageAsString(reply);
            AppendLog($"Host ? Web: {reply}");
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var msg = MessageTextBox.Text ?? string.Empty;
            if (GameWebView.CoreWebView2 is null)
            {
                AppendLog("CoreWebView2 not initialized yet.");
                return;
            }

            GameWebView.CoreWebView2.PostWebMessageAsString(msg);
            AppendLog($"Host ? Web: {msg}");
        }

        private void AppendLog(string text)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogListBox.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {text}");
            });
        }
    }
}
