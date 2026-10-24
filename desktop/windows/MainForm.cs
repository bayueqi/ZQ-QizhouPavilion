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

        public MainForm(string url)
        {
            startUrl = url;
            Text = "琪舟阁";
            ClientSize = new Size(1100, 760);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(420, 320);
            BackColor = ColorTranslator.FromHtml("#FDF8F3");
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
            st.AreDefaultContextMenusEnabled = true;
            st.IsStatusBarEnabled = false;
            st.AreDevToolsEnabled = false;
            st.IsZoomControlEnabled = true;
            string ua = st.UserAgent;
            if (ua != null && ua.Contains("; wv")) st.UserAgent = ua.Replace("; wv", "");

            web.CoreWebView2.WebMessageReceived += OnWebMessage;
            web.CoreWebView2.ContextMenuRequested += OnContextMenu;
            // target=_blank 的链接也在当前窗口打开
            web.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                web.CoreWebView2.Navigate(e.Uri);
            };

            if (url != null) web.CoreWebView2.Navigate(url);
            else ShowSetupPage(null);
        }

        private void ShowSetupPage(string current)
        {
            if (!coreReady) return;
            string html = SetupPage.Build(current);
            if (html == null) return;
            web.CoreWebView2.NavigateToString(html);
        }

        /// <summary>
        /// 右键菜单顶部插入 App 自己的入口，这样不记快捷键也能换域名 / 退出。
        /// </summary>
        private void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (!coreReady) return;
            CoreWebView2Environment env = web.CoreWebView2.Environment;
            var items = e.MenuItems;

            CoreWebView2ContextMenuItem change =
                env.CreateContextMenuItem("更换导航站域名", null, CoreWebView2ContextMenuItemKind.Command);
            change.CustomItemSelected += delegate { BeginInvoke((Action)(() => ShowSetupPage(Config.Load()))); };

            CoreWebView2ContextMenuItem back =
                env.CreateContextMenuItem("后退", null, CoreWebView2ContextMenuItemKind.Command);
            back.CustomItemSelected += delegate
            {
                BeginInvoke((Action)(() =>
                {
                    if (web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack();
                }));
            };

            CoreWebView2ContextMenuItem reload =
                env.CreateContextMenuItem("刷新", null, CoreWebView2ContextMenuItemKind.Command);
            reload.CustomItemSelected += delegate { BeginInvoke((Action)(() => web.CoreWebView2.Reload())); };

            CoreWebView2ContextMenuItem quit =
                env.CreateContextMenuItem("退出琪舟阁", null, CoreWebView2ContextMenuItemKind.Command);
            quit.CustomItemSelected += delegate { BeginInvoke((Action)(() => Close())); };

            CoreWebView2ContextMenuItem sep =
                env.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator);

            items.Insert(0, sep);
            items.Insert(0, quit);
            items.Insert(0, reload);
            items.Insert(0, back);
            items.Insert(0, change);
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string text;
            try { text = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (string.IsNullOrWhiteSpace(text)) return;

            // 欢迎页的 ✕ —— 回到原来的站
            if (text == "cancel")
            {
                string current = Config.Load();
                if (current != null) web.CoreWebView2.Navigate(current);
                return;
            }

            string url = UrlUtil.Normalize(text);
            if (url == null) return;

            Config.Save(url);
            web.CoreWebView2.Navigate(url);
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

    internal static class SetupPage
    {
        private static string template;

        public static string Build(string current)
        {
            if (template == null) template = ReadResource();
            if (template == null) return null;
            string safe = (current ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            return template.Replace("__CURRENT__", safe);
        }

        private static string ReadResource()
        {
            var asm = typeof(SetupPage).Assembly;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith("setup.html", StringComparison.OrdinalIgnoreCase)) continue;
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
