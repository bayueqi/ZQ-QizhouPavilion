using System;
using System.Windows.Forms;

namespace QizhouPavilion
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // DPI 感知由 app.manifest 声明（PerMonitorV2），这里不再 P/Invoke，
            // 否则会覆盖清单里的设置，导致缩放不是 100% 时 WebView2 点击坐标偏移。
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args != null && args.Length > 0 ? args[0] : null));
        }
    }
}
