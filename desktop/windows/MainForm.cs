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
        private const string BuildTag = "build 8";

        private readonly WebView2 web = new WebView2();
        private readonly string startUrl;
        private string menuJs;
        private bool coreReady;
        private bool inSetup;
        private bool menuReady;

        public MainForm(string url)
        {
            startUrl = url;
            Text = "琪舟阁  v1.0.1 " + BuildTag;
            // 不能再叠加 WinForms 的自动缩放：会和 app.manifest / app.config 的 DPI 设置打架，
            // 导致 WebView2 的 bounds 与实际客户区不一致，鼠标点击整体偏移。
            AutoScaleMode = AutoScaleMode.None;
            BackColor = ColorTranslator.FromHtml("#FDF8F3");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(480, 360);
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
            ClientSizeChanged += OnClientSizeChanged;
        }

        /** 尺寸一变就把 WebView2 的 bounds 钉死在客户区，避免残留旧尺寸导致点击命中错位。 */
        private void OnClientSizeChanged(object sender, EventArgs e)
        {
            if (web == null) return;
            web.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
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

            menuJs = Resources.Load("menu.js");
            if (menuJs != null)
            {
                try
                {
                    await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(menuJs);
                }
                catch
                {
                }
            }

            web.CoreWebView2.WebMessageReceived += OnWebMessage;
            web.CoreWebView2.ContextMenuRequested += OnContextMenu;
            web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
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
            menuReady = false;
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

        private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (inSetup || !coreReady) return;
            if (menuJs == null) return;
            try
            {
                string r = await web.CoreWebView2.ExecuteScriptAsync(
                    menuJs + ";typeof window.__zqMenu;");
                menuReady = r != null && r.IndexOf("object", StringComparison.Ordinal) >= 0;
            }
            catch
            {
                menuReady = false;
            }
        }

        /**
         * 右键：优先用页面里的自绘菜单；脚本没装上就退回系统菜单，保证"点了有反应"。
         */
        private async void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (!coreReady || inSetup) return;
            e.Handled = true;
            Point p = e.Location;

            if (menuJs == null)
            {
                ShowFallbackMenu(p);
                return;
            }

            string js = "(function(){try{" + menuJs +
                        ";if(!window.__zqMenu){return 'nomenu'}" +
                        "window.__zqMenu.show(" + p.X + "," + p.Y + ");return 'ok'" +
                        "}catch(err){return 'err'}})()";
            try
            {
                string r = await web.CoreWebView2.ExecuteScriptAsync(js);
                if (r == null || r.IndexOf("ok", StringComparison.Ordinal) < 0)
                {
                    ShowFallbackMenu(p);
                }
                else
                {
                    menuReady = true;
                }
            }
            catch
            {
                ShowFallbackMenu(p);
            }
        }

        /** 兜底菜单：自绘脚本没跑起来时才用，至少功能不缺。 */
        private void ShowFallbackMenu(Point p)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("更换导航站域名", null, delegate { ShowSetupPage(Config.Load()); });
            menu.Items.Add("后退", null, delegate
            {
                if (web.CoreWebView2 != null && web.CoreWebView2.CanGoBack) web.CoreWebView2.GoBack();
            });
            menu.Items.Add("刷新", null, delegate
            {
                if (web.CoreWebView2 != null) web.CoreWebView2.Reload();
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出琪舟阁", null, delegate { Close(); });
            menu.Show(web, p);
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
            if (e.Control && e.Shift && e.KeyCode == Keys.D)
            {
                ShowDiagnostics();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
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

        private void ShowDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildTag);
            sb.AppendLine("DeviceDpi = " + DeviceDpi + "  (" + (DeviceDpi * 100 / 96) + "%)");
            sb.AppendLine("ClientSize = " + ClientSize.Width + " x " + ClientSize.Height);
            if (web != null)
            {
                sb.AppendLine("WebView2 = " + web.Bounds.Width + " x " + web.Bounds.Height +
                              " @ " + web.Bounds.X + "," + web.Bounds.Y);
            }
            Rectangle b = Screen.PrimaryScreen.Bounds;
            Rectangle w = Screen.PrimaryScreen.WorkingArea;
            sb.AppendLine("Screen = " + b.Width + " x " + b.Height +
                          "  work = " + w.Width + " x " + w.Height);
            sb.AppendLine("AutoScaleMode = " + AutoScaleMode);
            sb.AppendLine("menuReady = " + menuReady);
            MessageBox.Show(sb.ToString(), "诊断信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
