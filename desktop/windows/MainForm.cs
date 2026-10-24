using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace QizhouPavilion
{
    /**
     * 不再使用 WinForms 的 WebView2 控件：
     * 那个封装层会按 DIP 换算 controller 的 bounds，窗口不是最大化时它和真实客户区对不上，
     * 表现就是"画面在上面的位置，点击却落到别处"。
     * 这里直接拿 CoreWebView2Controller，绑定窗体 HWND，并用 UseRawPixels 让它按物理像素走。
     */
    internal sealed class MainForm : Form
    {
        private const string BuildTag = "build 10";

        private readonly string startUrl;
        private CoreWebView2Controller controller;
        private CoreWebView2 core;
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
        }

        /** 把 WebView 的显示区域钉死在客户区（物理像素）。 */
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
            // 关键：bounds 用物理像素，避免 DIP 换算带来的点击偏移
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
            string ua = st.UserAgent;
            if (ua != null && ua.Contains("; wv")) st.UserAgent = ua.Replace("; wv", "");

            core.WebMessageReceived += OnWebMessage;
            core.ContextMenuRequested += OnContextMenu;
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
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Navigate(url);
        }

        private void ShowSetupPage(string current)
        {
            if (!coreReady) return;
            string html = SetupPage.Build(current);
            if (html == null) return;
            inSetup = true;
            // 欢迎页放行原生菜单，否则输入框里没法右键粘贴域名
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.NavigateToString(html);
        }

        private void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (!coreReady || inSetup) return;
            e.Handled = true;
            ShowAppMenu(e.Location);
        }

        private void ShowAppMenu(Point p)
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new RoundMenuRenderer();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.BackColor = Color.White;
            menu.Font = new Font("Microsoft YaHei UI", 9.5F);
            menu.Padding = new Padding(0, 6, 0, 6);
            menu.DropShadowEnabled = true;

            AddItem(menu, "更换导航站域名", delegate { ShowSetupPage(Config.Load()); });
            AddItem(menu, "后退", delegate
            {
                if (core != null && core.CanGoBack) core.GoBack();
            });
            AddItem(menu, "刷新", delegate
            {
                if (core != null) core.Reload();
            });
            menu.Items.Add(new ToolStripSeparator());
            AddItem(menu, "退出琪舟阁", delegate { Close(); });

            menu.Opened += delegate
            {
                try
                {
                    using (GraphicsPath path = RoundMenuRenderer.RoundedPath(
                        new Rectangle(0, 0, menu.Width, menu.Height), 10))
                    {
                        menu.Region = new Region(path);
                    }
                }
                catch
                {
                }
            };

            menu.Show(this, p);
        }

        private static void AddItem(ContextMenuStrip menu, string text, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text);
            item.Padding = new Padding(14, 5, 14, 5);
            item.Click += onClick;
            menu.Items.Add(item);
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
            else if (key == Keys.Left && (mods & Keys.Alt) == Keys.Alt)
            {
                if (core != null && core.CanGoBack) core.GoBack();
                e.Handled = true;
            }
        }

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

            string url = UrlUtil.Normalize(text);
            if (url == null) return;

            Config.Save(url);
            NavigateTo(url);
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
                sb.AppendLine("Controller = " + b.Width + " x " + b.Height + " @ " + b.X + "," + b.Y);
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

    /** 自绘右键菜单：白底圆角、悬停高亮。 */
    internal sealed class RoundMenuRenderer : ToolStripProfessionalRenderer
    {
        public static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedPath(new Rectangle(Point.Empty, e.ToolStrip.Size), 10))
            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedPath(
                new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10))
            using (var pen = new Pen(Color.FromArgb(30, 16, 24, 40)))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (GraphicsPath path = RoundedPath(r, 7))
            using (var brush = new SolidBrush(Color.FromArgb(26, 48, 85, 128)))
            {
                e.Graphics.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected
                ? ColorTranslator.FromHtml("#254267")
                : ColorTranslator.FromHtml("#1F2937");
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle r = e.Item.Bounds;
            using (var pen = new Pen(Color.FromArgb(24, 16, 24, 40)))
            {
                e.Graphics.DrawLine(pen, r.Left + 12, r.Top + r.Height / 2,
                    r.Right - 12, r.Top + r.Height / 2);
            }
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
