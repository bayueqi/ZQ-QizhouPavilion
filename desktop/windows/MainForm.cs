using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace QizhouPavilion
{
    /**
     * 进程声明 SystemAware（见 app.manifest），画面按系统缩放清晰渲染。
     * 点击能对齐的关键是把真实缩放比告诉 WebView2（RasterizationScale），
     * 并按 DIP 给它 bounds —— 少了这一步它按 100% 算坐标，就会和画面对不上。
     */
    internal sealed class MainForm : Form
    {
        private const string BuildTag = "b21";

        private readonly string startUrl;
        private CoreWebView2Controller controller;
        private CoreWebView2 core;
        private XButtonFilter xFilter;
        private string injectJs;
        private string errorHtml;
        private string lastUrl;
        private bool showingError;
        private bool coreReady;
        private bool inSetup;
        private bool fullscreen;
        private bool loading;
        private float spinAngle;
        private int loadingSince;
        private Timer spinTimer;
        private Timer hideTimer;
        private Timer watchdog;
        private FormBorderStyle prevBorder = FormBorderStyle.Sizable;
        private FormWindowState prevState = FormWindowState.Normal;

        public MainForm(string url)
        {
            startUrl = url;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = ColorTranslator.FromHtml("#FDF8F3");
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(480, 360);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(
                Math.Max(620, (int)(wa.Width * 0.82)),
                Math.Max(460, (int)(wa.Height * 0.86)));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            UpdateTitle();

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

        /** 标题栏顺手带上尺寸和 DPI，截图就能看出跑在什么环境下。 */
        private void UpdateTitle()
        {
            Text = "琪舟阁";
        }

        private double RasterScale
        {
            get
            {
                double scale = DeviceDpi / 96.0;
                return scale > 0 ? scale : 1.0;
            }
        }

        /**
         * RasterizationScale 在 .NET Framework 的 WebView2 兼容层里不一定直接暴露，
         * 用反射设置：有就设，没有就算了（不会因此编译不过）。
         */
        private static void SetRasterizationScale(CoreWebView2Controller ctl, double scale)
        {
            if (ctl == null) return;
            try
            {
                System.Reflection.PropertyInfo prop = ctl.GetType().GetProperty(
                    "RasterizationScale",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(ctl, scale, null);
                }
            }
            catch
            {
            }
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
                double scale = RasterScale;
                controller.Bounds = new Rectangle(0, 0,
                    (int)Math.Round(ClientSize.Width / scale),
                    (int)Math.Round(ClientSize.Height / scale));
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

            // 便携模式：配置和 WebView2 数据都放在 exe 同目录的 data 里，不往 AppData 里塞
            string dataDir = Path.Combine(Config.AppDir, "data", "WebView2");
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
            // 关键：告诉 WebView2 真实缩放比，再按 DIP 给 bounds
            double scale = RasterScale;
            SetRasterizationScale(controller, scale);
            controller.Bounds = new Rectangle(0, 0,
                (int)Math.Round(ClientSize.Width / scale),
                (int)Math.Round(ClientSize.Height / scale));
            controller.IsVisible = true;
            controller.ZoomFactor = 1.0;

            core = controller.CoreWebView2;
            coreReady = true;

            CoreWebView2Settings st = core.Settings;
            st.IsStatusBarEnabled = false;
            st.AreDevToolsEnabled = false;
            // 缩放交给网页自己的 viewport，App 不再提供缩放，免得缩放系数和点击坐标对不上
            st.IsZoomControlEnabled = false;
            // 需要保留默认上下文菜单的触发生效（关掉就收不到 ContextMenuRequested），
            // 具体画什么在 OnContextMenu 里交给 AppMenu
            st.AreDefaultContextMenusEnabled = true;
            string ua = st.UserAgent;
            if (ua != null && ua.Contains("; wv")) st.UserAgent = ua.Replace("; wv", "");

            core.WebMessageReceived += OnWebMessage;
            core.ContextMenuRequested += OnContextMenu;
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                NavigateTo(e.Uri);
            };

            // 往页面里插「更改域名」入口（不改网页源码）
            injectJs = Resources.Load("inject.js");
            if (injectJs != null)
            {
                try
                {
                    await core.AddScriptToExecuteOnDocumentCreatedAsync(injectJs);
                }
                catch
                {
                }
            }

            errorHtml = Resources.Load("error.html");
            core.NavigationCompleted += OnNavigationCompleted;

            if (url != null) NavigateTo(url);
            else ShowSetupPage(null);
        }

        /** document-created 阶段注入那次可能 DOM 还没解析完，加载完成后再补一次（脚本自身幂等）。 */
        private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!coreReady) return;
            // 欢迎页自己的加载结果
            if (inSetup)
            {
                SetLoading(false);
                return;
            }
            // 错误页自己的加载结果：加载完了才把画面放出来，中间不会闪过内核那张报错页
            if (showingError)
            {
                SetLoading(false);
                return;
            }
            if (!e.IsSuccess)
            {
                ShowErrorPage();
                return;
            }
            SetLoading(false);
            if (injectJs == null) return;
            try
            {
                await core.ExecuteScriptAsync(injectJs);
            }
            catch
            {
            }
        }

        /**
         * 导航期间把 WebView 藏起来、自己在窗口上转圈：
         * 内核自带的那张报错页没机会露脸，点「重试」也有加载反馈。
         * 秒失败的情况（域名解析不出来）也把圈留够 450ms，否则一闪而过，看着像没反应。
         */
        private void SetLoading(bool on)
        {
            if (on)
            {
                if (hideTimer != null) hideTimer.Stop();
                loadingSince = Environment.TickCount;
                ApplyLoading(true);
                return;
            }
            int shown = unchecked(Environment.TickCount - loadingSince);
            int wait = 450 - shown;
            if (loading && shown >= 0 && wait > 0)
            {
                if (hideTimer == null)
                {
                    hideTimer = new Timer();
                    hideTimer.Tick += delegate
                    {
                        hideTimer.Stop();
                        ApplyLoading(false);
                    };
                }
                hideTimer.Interval = wait;
                hideTimer.Start();
                return;
            }
            ApplyLoading(false);
        }

        private void ApplyLoading(bool on)
        {
            loading = on;
            if (controller != null)
            {
                try { controller.IsVisible = !on; } catch { }
            }
            if (on)
            {
                if (spinTimer == null)
                {
                    spinTimer = new Timer();
                    spinTimer.Interval = 40;
                    spinTimer.Tick += delegate
                    {
                        spinAngle = (spinAngle + 24) % 360;
                        if (loading) Invalidate();
                    };
                }
                spinTimer.Start();
                StartWatchdog();
            }
            else
            {
                if (spinTimer != null) spinTimer.Stop();
                if (watchdog != null) watchdog.Stop();
            }
            Invalidate();
            Update();
        }

        /** 万一次导航回调没来，别让窗口一直空着。 */
        private void StartWatchdog()
        {
            if (watchdog == null)
            {
                watchdog = new Timer();
                watchdog.Interval = 20000;
                watchdog.Tick += delegate
                {
                    watchdog.Stop();
                    if (loading) SetLoading(false);
                };
            }
            watchdog.Stop();
            watchdog.Start();
        }

        private const int WM_CONTEXTMENU = 0x007B;

        /**
         * 转圈这层是窗口自己画的，网页拿不到右键；这里补上同六项的菜单，
         * 和网页里的右键菜单、手机长按菜单一致。
         */
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CONTEXTMENU && loading)
            {
                ShowAppMenu(Cursor.Position);
                return;
            }
            base.WndProc(ref m);
        }

        /** 六项菜单：网页里、转圈层上都用这一份，样式见 AppMenu。 */
        private void ShowAppMenu(Point screenPos)
        {
            string[] labels = {
                "刷新",
                fullscreen ? "退出全屏" : "全屏",
                "更换域名",
                "后退上一页",
                "前进下一页",
                "返回主页面"
            };
            bool[] enabled = {
                true,
                true,
                true,
                core != null && core.CanGoBack,
                core != null && core.CanGoForward,
                true
            };
            Action[] actions = {
                delegate { RetryLast(); },
                delegate { ToggleFullscreen(); },
                delegate { ShowSetupPage(Config.Load()); },
                delegate { NavigateBack(); },
                delegate { NavigateForward(); },
                delegate { GoHome(); }
            };
            new AppMenu(labels, enabled, actions).ShowAt(screenPos);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!loading) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int size = 26;
            float cx = ClientSize.Width / 2f;
            float cy = ClientSize.Height / 2f;
            RectangleF rect = new RectangleF(cx - size / 2f, cy - size / 2f, size, size);
            using (Pen track = new Pen(Color.FromArgb(46, 48, 85, 128), 2.6f))
            {
                e.Graphics.DrawEllipse(track, rect);
            }
            using (Pen arc = new Pen(ColorTranslator.FromHtml("#305580"), 2.6f))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                e.Graphics.DrawArc(arc, rect, spinAngle, 90);
            }
        }

        private void NavigateTo(string url)
        {
            if (!coreReady) return;
            showingError = false;
            inSetup = false;
            lastUrl = url;
            SetLoading(true);
            core.Navigate(url);
        }

        /** 打不开时换成 App 自己的提示页，不用 WebView2 自带的那张报错页。 */
        private void ShowErrorPage()
        {
            if (errorHtml == null)
            {
                SetLoading(false);
                return;
            }
            showingError = true;
            try { core.NavigateToString(errorHtml); }
            catch { SetLoading(false); }
        }

        private void RetryLast()
        {
            if (string.IsNullOrEmpty(lastUrl))
            {
                GoHome();
                return;
            }
            NavigateTo(lastUrl);
        }

        private void ShowSetupPage(string current)
        {
            if (!coreReady) return;
            string html = SetupPage.Build(current);
            if (html == null) return;
            showingError = false;
            inSetup = true;
            // 转圈时点「更换域名」：把在跑的那次导航停掉，欢迎页加载完才把画面放出来
            if (loading)
            {
                try { core.Stop(); } catch { }
            }
            core.NavigateToString(html);
        }

        /** 网页里的消息：欢迎页提交地址 / 取消、侧边栏「更改域名」、长按「返回主页面」。 */
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

            // 打不开的提示页上那两颗按钮
            if (text == "retry")
            {
                RetryLast();
                return;
            }

            if (text == "change")
            {
                ShowSetupPage(Config.Load());
                return;
            }

            if (text == "home")
            {
                GoHome();
                return;
            }

            if (text == "back")
            {
                NavigateBack();
                return;
            }

            if (text == "forward")
            {
                NavigateForward();
                return;
            }

            if (text == "fullscreen")
            {
                ToggleFullscreen();
                return;
            }

            if (text == "reload")
            {
                if (showingError) RetryLast();
                else if (core != null) core.Reload();
                return;
            }

            string url = UrlUtil.Normalize(text);
            if (url == null) return;

            Config.Save(url);
            NavigateTo(url);
        }

        /** 回导航站首页，不保留历史。 */
        private void GoHome()
        {
            string home = Config.Load();
            if (home != null) NavigateTo(home);
        }

        /**
         * 右键菜单：只用我们自己的六项（刷新 / 全屏 / 更换域名 / 后退上一页 / 前进下一页 / 返回主页面），
         * 不保留 WebView2 自带项，也不挂任何快捷键提示。
         */
        private void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (!coreReady || inSetup) return;
            try
            {
                // 交给自绘的 AppMenu 画，和转圈层上那份菜单是同一套样式
                e.Handled = true;
                ShowAppMenu(Cursor.Position);
            }
            catch
            {
            }
        }

        private void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                prevBorder = FormBorderStyle;
                prevState = WindowState;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = prevBorder;
                WindowState = prevState;
            }
            fullscreen = !fullscreen;
            SyncBounds();
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

    /**
     * 自绘的右键菜单。Windows 自带菜单是方角老样式，和网页里那套（以及 Linux 自绘的）
     * 差得远，这里按同一份样式画：白卡片、圆角、悬停高亮、不可用项变灰。
     */
    internal sealed class AppMenu : Form
    {
        private const int CS_DROPSHADOW = 0x00020000;
        private const int CardPad = 6;
        private const int ItemPadX = 13;
        private const int ItemPadY = 11;
        private const int ItemRadius = 9;
        private const int CardRadius = 14;
        private const int MinWidth = 184;

        private readonly string[] labels;
        private readonly bool[] enabled;
        private readonly Action[] actions;
        private int hover = -1;
        private int itemHeight;
        private int cardWidth;
        private bool closing;

        public AppMenu(string[] labels, bool[] enabled, Action[] actions)
        {
            this.labels = labels;
            this.enabled = enabled;
            this.actions = actions;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            DoubleBuffered = true;
            Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
            Measure();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        private void Measure()
        {
            int textWidth = 0;
            int textHeight = 0;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                for (int i = 0; i < labels.Length; i++)
                {
                    Size sz = TextRenderer.MeasureText(g, labels[i], Font);
                    if (sz.Width > textWidth) textWidth = sz.Width;
                    if (sz.Height > textHeight) textHeight = sz.Height;
                }
            }
            itemHeight = textHeight + ItemPadY * 2;
            cardWidth = Math.Max(MinWidth, textWidth + ItemPadX * 2 + CardPad * 2);
            ClientSize = new Size(cardWidth, itemHeight * labels.Length + CardPad * 2);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundRegion(CardRadius);
        }

        private void ApplyRoundRegion(int radius)
        {
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private Rectangle ItemRect(int i)
        {
            return new Rectangle(CardPad, CardPad + itemHeight * i,
                cardWidth - CardPad * 2, itemHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < labels.Length; i++)
            {
                Rectangle r = ItemRect(i);
                bool hot = i == hover && enabled[i];
                if (hot)
                {
                    using (GraphicsPath p = RoundedPath(r, ItemRadius))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(236, 240, 244)))
                    {
                        e.Graphics.FillPath(b, p);
                    }
                }
                Color c;
                if (!enabled[i]) c = ColorTranslator.FromHtml("#C2C7D0");
                else if (hot) c = ColorTranslator.FromHtml("#254267");
                else c = ColorTranslator.FromHtml("#1F2937");

                Rectangle text = new Rectangle(r.X + ItemPadX, r.Y, r.Width - ItemPadX * 2, r.Height);
                TextRenderer.DrawText(e.Graphics, labels[i], Font, text, c,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        private int IndexAt(Point p)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                if (ItemRect(i).Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = IndexAt(e.Location);
            if (i != hover)
            {
                hover = i;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1)
            {
                hover = -1;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int i = IndexAt(e.Location);
                if (i >= 0 && enabled[i])
                {
                    Action run = actions[i];
                    Dismiss();
                    if (run != null) run();
                    return;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Dismiss();
        }

        private void Dismiss()
        {
            if (closing) return;
            closing = true;
            Close();
        }

        /** 在鼠标处弹出，贴边时往里收。 */
        public void ShowAt(Point screenPos)
        {
            Rectangle wa = Screen.FromPoint(screenPos).WorkingArea;
            int x = Math.Min(screenPos.X, wa.Right - Width);
            int y = Math.Min(screenPos.Y, wa.Bottom - Height);
            Location = new Point(Math.Max(wa.Left, x), Math.Max(wa.Top, y));
            Show();
            Activate();
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
        /** exe 所在目录：配置、缓存全部放这里面，卸载时删整个文件夹就干净了。 */
        public static string AppDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        private static string FilePath
        {
            get { return Path.Combine(AppDir, "data", "home.txt"); }
        }

        public static string Load()
        {
            try
            {
                if (!File.Exists(FilePath)) MigrateFromLegacy();
                if (!File.Exists(FilePath)) return null;
                string text = File.ReadAllText(FilePath).Trim();
                return text.Length == 0 ? null : text;
            }
            catch
            {
                return null;
            }
        }

        /** 老版本把域名存在 AppData 里，第一次运行时搬过来，免得重新填。 */
        private static void MigrateFromLegacy()
        {
            try
            {
                string legacy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "QizhouPavilion", "home.txt");
                if (!File.Exists(legacy)) return;
                string dir = Path.GetDirectoryName(FilePath);
                if (dir != null) Directory.CreateDirectory(dir);
                File.Copy(legacy, FilePath, true);
            }
            catch
            {
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
