using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace QizhouPavilion
{
    /**
     * 直接使用 CoreWebView2Controller（不用 WinForms 的 WebView2 控件）：
     * 那个封装层按 DIP 换算 bounds，窗口不是最大化时会和真实客户区对不上，
     * 表现就是"画面在这里，点击落到别处"。这里用 UseRawPixels 让它按物理像素走。
     */
    internal sealed class MainForm : Form
    {
        private const string BuildTag = "build 11";

        private readonly string startUrl;
        private CoreWebView2Controller controller;
        private CoreWebView2 core;
        private XButtonFilter xFilter;
        private bool coreReady;
        private bool inSetup;

        public MainForm(string url)
        {
            startUrl = url;
            Text = "琪舟阁  v1.0.1 " + BuildTag;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = ColorTranslator.FromHtml("#FDF8F3");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(480, 360);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(
                Math.Max(620, Math.Min(1160, (int)(wa.Width * 0.82))),
                Math.Max(460, Math.Min(820, (int)(wa.Height * 0.86))));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Resize += delegate { SyncBounds(); };
            Shown += OnShown;

            // 鼠标侧键（后退/前进），键盘焦点在网页里时也能收到
            xFilter = new XButtonFilter(this);
            Application.AddMessageFilter(xFilter);
        }

        public void NavigateBack()
        {
            if (core != null && core.CanGoBack) core.GoBack();
        }

        public void NavigateForward()
        {
            if (core != null && core.CanGoForward) core.GoForward();
        }

        private void SyncBounds()
        {
            if (controller == null) return;
            if (WindowState == FormWindowState.Minimized) return;
            try
            {
                controller.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            }
            catch
            {
            }
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

        private async System.Threading.Tasks.Task InitAsync()
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

            controller = await env.CreateCoreWebView2ControllerAsync(Handle);
            controller.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
            controller.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            controller.IsVisible = true;
            controller.AcceleratorKeyPressed += OnAcceleratorKey;

            core = controller.CoreWebView2;
            coreReady = true;

            CoreWebView2Settings st = core.Settings;
            st.IsStatusBarEnabled = false;
            st.AreDevToolsEnabled = false;
            st.IsZoomControlEnabled = true;
            // 右键交回给网页自己（复制、另存为都还在）
            st.AreDefaultContextMenusEnabled = true;
            string ua = st.UserAgent;
            if (ua != null && ua.Contains("; wv")) st.UserAgent = ua.Replace("; wv", "");

            core.WebMessageReceived += OnWebMessage;
            core.NewWindowRequested += (s, e) =>
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
            core.Navigate(url);
        }

        private void ShowSetupPage(string current)
        {
            if (!coreReady) return;
            string html = SetupPage.Build(current);
            if (html == null) return;
            inSetup = true;
            core.NavigateToString(html);
        }

        /** 网页里的消息：欢迎页提交地址、取消，以及侧边栏的"更改域名"。 */
        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string text;
            try { text = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (string.IsNullOrWhiteSpace(text)) return;

            if (text == "cancel")
            {
                string current = Config.Load();
                if (current != null) NavigateTo(current);
                return;
            }

            if (text == "change")
            {
                ShowSetupPage(Config.Load());
                return;
            }

            string url = UrlUtil.Normalize(text);
            if (url == null) return;

            Config.Save(url);
            NavigateTo(url);
        }

        /** 键盘焦点在网页里时 Form.KeyDown 收不到，只能从 controller 这一层拦。 */
        private void OnAcceleratorKey(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if (e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown &&
                e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;

            Keys key = (Keys)e.VirtualKey;
            Keys mods = ModifierKeys;

            if (key == Keys.D && (mods & Keys.Control) == Keys.Control && (mods & Keys.Shift) == Keys.Shift)
            {
                ShowDiagnostics();
                e.Handled = true;
            }
            else if (key == Keys.F5 || (key == Keys.R && (mods & Keys.Control) == Keys.Control))
            {
                if (core != null) core.Reload();
                e.Handled = true;
            }
            else if (key == Keys.L && (mods & Keys.Control) == Keys.Control)
            {
                ShowSetupPage(Config.Load());
                e.Handled = true;
            }
            else if ((key == Keys.Left && (mods & Keys.Alt) == Keys.Alt) ||
                     (key == Keys.BrowserBack))
            {
                NavigateBack();
                e.Handled = true;
            }
        }

        private void ShowDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(BuildTag);
            sb.AppendLine("DeviceDpi = " + DeviceDpi + "  (" + (DeviceDpi * 100 / 96) + "%)");
            sb.AppendLine("ClientSize = " + ClientSize.Width + " x " + ClientSize.Height);
            sb.AppendLine("Screen = " + Screen.PrimaryScreen.Bounds.Width + " x " +
                          Screen.PrimaryScreen.Bounds.Height);
            if (controller != null)
            {
                Rectangle b = controller.Bounds;
                sb.AppendLine("Bounds = " + b.Width + " x " + b.Height + " @ " + b.X + "," + b.Y);
                sb.AppendLine("BoundsMode = " + controller.BoundsMode);
            }
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (xFilter != null)
            {
                Application.RemoveMessageFilter(xFilter);
                xFilter = null;
            }
            try
            {
                if (controller != null) controller.Close();
            }
            catch
            {
            }
            base.OnFormClosed(e);
        }
    }

    /** 鼠标侧键：XBUTTON1 后退，XBUTTON2 前进。 */
    internal sealed class XButtonFilter : IMessageFilter
    {
        private const int WM_XBUTTONDOWN = 0x020B;
        private readonly MainForm form;

        public XButtonFilter(MainForm owner)
        {
            form = owner;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_XBUTTONDOWN) return false;
            int button = (int)(m.WParam.ToInt64() >> 16) & 0xFFFF;
            if (button == 1)
            {
                form.NavigateBack();
                return true;
            }
            if (button == 2)
            {
                form.NavigateForward();
                return true;
            }
            return false;
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
                    using (var reader = new StreamReader(s, System.Text.Encoding.UTF8))
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
