using System;
using System.Windows.Forms;

namespace Playgama.Bridge.Wrappers.MicrosoftStore
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainWindow());
        }
    }
}
