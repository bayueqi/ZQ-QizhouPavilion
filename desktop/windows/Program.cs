using System;
using System.Windows.Forms;

namespace QizhouPavilion
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }
}
