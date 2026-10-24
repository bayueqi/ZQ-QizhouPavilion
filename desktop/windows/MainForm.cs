using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QizhouPavilion
{
    internal sealed class MainForm : Form
    {
        private readonly WebView2 web = new WebView2();
        private readonly string startUrl;
        private bool coreReady;
        private bool inSetup;

        public MainForm(string url)
        {
            startUrl = url;
            Text = "琪舟阁";
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = ColorTranslator.FromHtml("#FDF8F3");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(560, 420);
            // 按屏幕工作区比例开窗，避免固定像素在 125%/150% 缩放下尺寸失真
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(
                Math.Max(620, Math.Min(1160, (int)(wa.Width * 0.82))),
                Math.Max(460, Math.Min(820, (int)(wa.Height * 0.86))));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            web.Dock = DockStyle.Fill;
            Controls.Add(web);
            KeyPreview = true;
            KeyDown += OnKeyDown;
            Shown += OnShown;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            try
            {
                await InitAsync();
            }
            catch (Exception ex)
            {
                ShowFatal(ex);
            }
        }

        private async Task InitAsync()
        {
            string url = startUrl;
            if (string.IsNullOrWhiteSpace(url)) url = Config.Load();
            if (!string.IsNullOrWhiteSpace(url)) url = UrlUtil.Normalize(url);

            string dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QizhouPavilion", "WebView2");
            Directory.CreateDirectory(dataDir);

            CoreWebView2Environment env;
            try
            {
                env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            }
            catch (Exception ex)
            {
                ShowFatal(ex);
                return;
            }

            await web.EnsureCoreWebView2Async(env);
            coreReady = true;

            CoreWebView2Settings st = web.CoreWebView2.Settings;
            st.IsStatusBarEnabled = false;
            st.AreDevToolsEnabled = false;
            st.IsZoomControlEnabled = true;
            string ua = st.UserAgent;
            if (ua != null && ua.Contains("; wv")) st.UserAgent = ua.Replace("; wv", "");

            // 自绘右键菜单，替掉 WebView2 自带的英文原生菜单
            string menuJs = Resources.Load("menu.js");
            if (menuJs != null)
            {
                await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(menuJs);
            }

            web.CoreWebView2.WebMessageReceived += OnWebMessage;
            web.CoreWebView2.ContextMenuRequested += OnContextMenu;
            // target=_blank 的链接也在当前窗口打开
            web.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                NavigateTo(e.Uri);
            };

            if (url != null) NavigateTo(url);
            else ShowSetupPage(null);
        }

        private void NavigateTo(string url)
        {
            if (!coreReady) return;
            inSetup = false;
            // 正常浏览时用自绘菜单；欢迎页需要原生的复制/粘贴，见 ShowSetupPage
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Navigate(url);
        }

        private void ShowSetupPage(string current)
        {
            if (!coreReady) return;
            string html = SetupPage.Build(current);
            if (html == null) return;
            inSetup = true;
            // 欢迎页放行原生菜单，否则输入框里没法右键粘贴域名
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            web.CoreWebView2.NavigateToString(html);
        }

        private void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (!coreReady || inSetup) return;
            e.Handled = true;
            Point p = e.Location;
            try
            {
                web.CoreWebView2.ExecuteScriptAsync(
                    "window.__zqMenu&&window.__zqMenu.show(" + p.X + "," + p.Y + ")");
            }
            catch
            {
            }
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string text;
            try { text = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (string.IsNullOrWhiteSpace(text)) return;

            if (text.StartsWith("menu:", StringComparison.Ordinal))
            {
                DoMenuAction(text.Substring(5));
                return;
            }

            // 欢迎页的 ✕ —— 回到原来的站
            if (text == "cancel")
            {
                string current = Config.Load();
                if (current != null) NavigateTo(current);
                return;
            }

            string url = UrlUtil.Normalize(text);
            if (url == null) return;

            Config.Save(url);
            NavigateTo(url);
        }

        private void DoMenuAction(string action)
        {
            if (action == "domain")
            {
                ShowSetupPage(Config.Load());
            }
            else if (action == "back")
            {
                if (web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack();
            }
            else if (action == "reload")
            {
                web.CoreWebView2.Reload();
            }
            else if (action == "quit")
            {
                Close();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!coreReady) return;
            if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
            {
                web.CoreWebView2.Reload();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.L)
            {
                ShowSetupPage(Config.Load());
                e.Handled = true;
            }
            else if (e.Alt && e.KeyCode == Keys.Left)
            {
                if (web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack();
                e.Handled = true;
            }
        }

        private void ShowFatal(Exception ex)
        {
            string extra = "";
            int hr = 0;
            try { hr = System.Runtime.InteropServices.Marshal.GetHRForException(ex); } catch { }
            if (hr == unchecked((int)0x8007000B))
            {
                extra = "\n\n原因：程序位数与 WebView2Loader.dll 不一致（应为 x64）。";
            }
            else if (hr == unchecked((int)0x80040154) || hr == unchecked((int)0x8007007E))
            {
                extra = "\n\n原因：系统里没有 Microsoft Edge WebView2 运行时。\nWin11 自带；Win10 装一次 Edge 即可。";
            }
            MessageBox.Show("WebView2 初始化失败：\n" + ex.Message + extra,
                "琪舟阁", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    internal static class Resources
    {
        public static string Load(string suffix)
        {
            var asm = typeof(Resources).Assembly;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) return null;
                    using (var reader = new StreamReader(s, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            return null;
        }
    }

    internal static class SetupPage
    {
        private static string template;

        public static string Build(string current)
        {
            if (template == null) template = Resources.Load("setup.html");
            if (template == null) return null;
            string safe = (current ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            return template.Replace("__CURRENT__", safe);
        }
    }

    internal static class Config
    {
        private static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QizhouPavilion", "home.txt");
            }
        }

        public static string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                string text = File.ReadAllText(FilePath).Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string url)
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, url);
            }
            catch
            {
            }
        }
    }

    internal static class UrlUtil
    {
        public static string Normalize(string raw)
        {
            if (raw == null) return null;
            string s = raw.Trim();
            if (s.Length == 0) return null;
            if (!s.StartsWith("http://") && !s.StartsWith("https://")) s = "https://" + s;
            s = s.TrimEnd('/');
            if (s.Length <= 8) return null;
            if (!s.Contains(".")) return null;
            return s;
        }
    }
}
