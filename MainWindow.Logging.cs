using System;
using System.Diagnostics;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    public sealed partial class MainWindow
    {
        private void AppendLog(string text)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
        }
    }
}