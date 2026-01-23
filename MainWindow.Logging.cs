using System;
using System.Diagnostics;

namespace PlaygamaBridgeMicrosoftStore
{
    public sealed partial class MainWindow
    {
        private void AppendLog(string text)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}");
        }
    }
}