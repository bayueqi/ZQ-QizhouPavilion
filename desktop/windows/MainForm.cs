using System;
using System.Drawing;
using System.IO;
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

        public MainForm(string url)
        {
            startUrl = url;
            Text = "琪舟阁";
            ClientSize = new Size(1100, 760);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(420, 320);
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
                MessageBox.Show("WebView2 初始化失败：\n" + ex.Message +
                                "\n\nWindows 10/11 需要安装 Microsoft Edge WebView2 运行时。",
                    "琪舟阁", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        private async Task InitAsync()
        {
            string url = startUrl;
            if (string.IsNullOrWhiteSpace(url)) url = Config.Load();
            if (string.IsNullOrWhiteSpace(url))
            {
                url = Ui.PromptUrl(this);
                if (string.IsNullOrWhiteSpace(url)) { Close(); return; }
                Config.Save(url);
            }
            url = UrlUtil.Normalize(url);
            if (url == null) { Close(); return; }

            string dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QizhouPavilion", "WebView2");
            Directory.CreateDirectory(dataDir);

            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await web.EnsureCoreWebView2Async(env);

            CoreWebView2Settings st = web.CoreWebView2.Settings;
            st.AreDefaultContextMenusEnabled = true;
            st.IsStatusBarEnabled = false;
            st.AreDevToolsEnabled = false;
            st.IsZoomControlEnabled = true;
            string ua = st.UserAgent;
            if (ua != null && ua.Contains("; wv")) st.UserAgent = ua.Replace("; wv", "");

            // target=_blank 的链接也在当前窗口打开
            web.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                web.CoreWebView2.Navigate(e.Uri);
            };

            web.CoreWebView2.Navigate(url);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (web.CoreWebView2 == null) return;
            if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
            {
                web.CoreWebView2.Reload();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.L)
            {
                string input = Ui.PromptUrl(this);
                if (!string.IsNullOrWhiteSpace(input))
                {
                    string normalized = UrlUtil.Normalize(input);
                    if (normalized != null)
                    {
                        Config.Save(normalized);
                        web.CoreWebView2.Navigate(normalized);
                    }
                }
                e.Handled = true;
            }
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

    internal static class Ui
    {
        public static string PromptUrl(IWin32Window owner)
        {
            using (var form = new Form())
            {
                form.Text = "琪舟阁";
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.StartPosition = FormStartPosition.CenterParent;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ClientSize = new Size(440, 172);

                var label = new Label
                {
                    Text = "输入你部署的导航站域名",
                    Left = 20, Top = 18, Width = 400, Height = 20
                };
                var box = new TextBox
                {
                    Left = 20, Top = 44, Width = 400,
                    PlaceholderText = "例如 nav.example.com"
                };
                var ok = new Button
                {
                    Text = "进入", Left = 250, Top = 92, Width = 82, Height = 34,
                    DialogResult = DialogResult.OK
                };
                var cancel = new Button
                {
                    Text = "取消", Left = 340, Top = 92, Width = 82, Height = 34,
                    DialogResult = DialogResult.Cancel
                };

                form.Controls.AddRange(new Control[] { label, box, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                form.ActiveControl = box;

                return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
