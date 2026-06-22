using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DebugTool")]
[assembly: AssemblyDescription("开启Debug调试工具")]
[assembly: AssemblyCompany("金恩出品")]
[assembly: AssemblyProduct("DebugTool")]
[assembly: AssemblyCopyright("Copyright © 金恩出品")]
[assembly: AssemblyVersion("1.1.13.0")]
[assembly: AssemblyFileVersion("1.1.13.0")]
[assembly: AssemblyInformationalVersion("1.1.13")]

namespace DebugTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppId = "my.zte.tool.v1";
        private const string AppTitle = "开启Debug调试工具 - 金恩出品";
        private const string AppVersion = "1.1.13";
        private const string UpdateJsonUrl = "https://github.com/gegj/DebugTool/releases/latest/download/latest.json";
        private const string DefaultHost = "192.168.0.1";
        private const string DefaultRemoHost = "192.168.100.1";
        private const int WmSettingChange = 0x001A;
        private const int WmThemeChanged = 0x031A;
        private const int ContentWidth = 328;
        private const int InputPaddingX = 8;
        private const int ImeiButtonWidth = 76;
        private const int ImeiButtonGap = 10;

        private Color _bg;
        private Color _bg2;
        private Color _accent;
        private Color _red;
        private Color _amber;
        private Color _text;
        private Color _text2;
        private Color _border;

        private readonly List<Button> _buttons = new List<Button>();
        private TextBox _hostBox;
        private TextBox _imeiBox;
        private TextBox _remoHostBox;
        private TextBox _remoImeiBox;
        private Label _infoLabel;
        private bool _busy;

        public MainForm()
        {
            LoadSystemThemeColors();
            InitWindowsAppId();
            InitWindow();
            BuildUi();
            RefreshSystemTheme();
            Shown += delegate
            {
                BeginUpdateCheck();
                BeginAutoFetch();
            };
        }

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

        private static void InitWindowsAppId()
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID(AppId);
            }
            catch
            {
            }
        }

        private void InitWindow()
        {
            Text = AppTitle + " v" + AppVersion;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(380, 656);
            BackColor = _bg;
            Font = new Font("Microsoft YaHei UI", 9F);

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Tag = "bg";
            root.Dock = DockStyle.Fill;
            root.BackColor = _bg;
            root.Padding = new Padding(16, 6, 16, 0);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 324));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 284));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(root);

            root.Controls.Add(BuildF32Panel(), 0, 0);
            root.Controls.Add(BuildRemoPanel(), 0, 1);

            var statusHost = new Panel();
            statusHost.Tag = "bg";
            statusHost.Dock = DockStyle.Fill;
            statusHost.BackColor = _bg;
            root.Controls.Add(statusHost, 0, 2);

            var statusFrame = new RoundedPanel();
            statusFrame.Tag = "status-frame";
            statusFrame.Left = ContentLeft(statusHost);
            statusFrame.Top = 3;
            statusFrame.Width = ContentWidth;
            statusFrame.Height = 34;
            statusFrame.Anchor = AnchorStyles.Top;
            statusFrame.BackColor = _bg;
            statusFrame.FillColor = _bg2;
            statusFrame.BorderColor = _border;
            statusFrame.Radius = 8;
            statusFrame.Padding = new Padding(8, 0, 8, 0);
            statusHost.Controls.Add(statusFrame);
            TrackCentered(statusHost, statusFrame);

            _infoLabel = new Label();
            _infoLabel.Tag = "status";
            _infoLabel.Dock = DockStyle.Fill;
            _infoLabel.BackColor = _bg2;
            _infoLabel.ForeColor = _text2;
            _infoLabel.Text = "就绪";
            _infoLabel.TextAlign = ContentAlignment.MiddleCenter;
            _infoLabel.Font = new Font("Microsoft YaHei UI", 8.25F);
            statusFrame.Controls.Add(_infoLabel);
        }

        private Control BuildF32Panel()
        {
            var panel = NewSectionPanel();
            int y = 4;
            AddSectionTitle(panel, "新版 F32/F30PRO", 15F, ref y);

            AddLabel(panel, "IP", ref y);
            _hostBox = AddEntry(panel, DefaultHost, ref y);
            _hostBox.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter)
                {
                    args.SuppressKeyPress = true;
                    FetchImei();
                }
            };

            AddLabel(panel, "IMEI", ref y);
            Panel row = AddEntryRow(panel, ref y);
            _imeiBox = AddEntryInRow(row);
            AddSmallButton(row, "获取", FetchImei);

            AddWideButton(panel, "▶  开启 Telnet", _accent, DoTelnet, ref y);
            AddWideButton(panel, "★  开启 Telnet + Debug", _amber, DoAll, ref y);
            AddWideButton(panel, "■  关闭 Telnet", _text2, DoDisable, ref y);
            AddWideButton(panel, "↻  重启设备", _red, DoReboot, ref y);
            return panel;
        }

        private static int ContentLeft(Control parent)
        {
            int width = parent.ClientSize.Width > 0 ? parent.ClientSize.Width : ContentWidth;
            return Math.Max(0, (width - ContentWidth) / 2);
        }

        private void TrackCentered(Control parent, Control child)
        {
            parent.Resize += delegate
            {
                child.Left = ContentLeft(parent);
            };
        }

        private Control BuildRemoPanel()
        {
            var panel = NewSectionPanel();
            int y = 0;
            AddSeparator(panel, ref y);
            AddSectionTitle(panel, "REMO", 13F, ref y);

            AddLabel(panel, "IP", ref y);
            _remoHostBox = AddEntry(panel, DefaultRemoHost, ref y);
            _remoHostBox.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter)
                {
                    args.SuppressKeyPress = true;
                    DoXxremoDebug();
                }
            };

            AddLabel(panel, "IMEI", ref y);
            Panel row = AddEntryRow(panel, ref y);
            _remoImeiBox = AddEntryInRow(row);
            AddSmallButton(row, "获取", FetchRemoImei);

            AddWideButton(panel, "★  开启 REMO Debug", _amber, DoXxremoDebug, ref y);
            AddWideButton(panel, "↻  重启设备", _red, DoRemoReboot, ref y);
            return panel;
        }

        private Panel NewSectionPanel()
        {
            var panel = new Panel();
            panel.Tag = "bg";
            panel.Dock = DockStyle.Fill;
            panel.BackColor = _bg;
            return panel;
        }

        private void AddSectionTitle(Control parent, string text, float size, ref int y)
        {
            var label = new Label();
            label.Tag = "title";
            label.Left = ContentLeft(parent);
            label.Top = y;
            label.Width = ContentWidth;
            label.Height = 34;
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.BackColor = _bg;
            label.ForeColor = _accent;
            label.Font = new Font("Microsoft YaHei UI", size, FontStyle.Bold);
            label.Anchor = AnchorStyles.Top;
            parent.Controls.Add(label);
            TrackCentered(parent, label);
            y += 38;
        }

        private void AddLabel(Control parent, string text, ref int y)
        {
            var label = new Label();
            label.Tag = "label";
            label.Left = ContentLeft(parent);
            label.Top = y;
            label.Width = 80;
            label.Height = 20;
            label.Text = text;
            label.BackColor = _bg;
            label.ForeColor = _text2;
            label.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(label);
            TrackCentered(parent, label);
            y += 20;
        }

        private TextBox AddEntry(Control parent, string value, ref int y)
        {
            var box = new TextBox();
            box.Tag = "entry";
            box.Left = InputPaddingX;
            box.Top = 7;
            box.Width = ContentWidth - InputPaddingX * 2;
            box.Height = 20;
            box.Text = value;
            box.BackColor = _bg2;
            box.ForeColor = _text;
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("Microsoft YaHei UI", 9.5F);
            Panel frame = NewInputFrame(ContentLeft(parent), y, ContentWidth, 34);
            frame.Controls.Add(box);
            parent.Controls.Add(frame);
            TrackCentered(parent, frame);
            y += 40;
            return box;
        }

        private Panel AddEntryRow(Control parent, ref int y)
        {
            var row = new Panel();
            row.Tag = "bg";
            row.Left = ContentLeft(parent);
            row.Top = y;
            row.Width = ContentWidth;
            row.Height = 36;
            row.BackColor = _bg;
            parent.Controls.Add(row);
            TrackCentered(parent, row);
            y += 42;
            return row;
        }

        private TextBox AddEntryInRow(Control row)
        {
            var box = new TextBox();
            box.Tag = "entry";
            box.Left = InputPaddingX;
            box.Top = 7;
            box.Width = ContentWidth - ImeiButtonWidth - ImeiButtonGap - InputPaddingX * 2;
            box.Height = 20;
            box.BackColor = _bg2;
            box.ForeColor = _text;
            box.BorderStyle = BorderStyle.None;
            box.Font = new Font("Microsoft YaHei UI", 9.5F);
            Panel frame = NewInputFrame(0, 0, ContentWidth - ImeiButtonWidth - ImeiButtonGap, 34);
            frame.Controls.Add(box);
            row.Controls.Add(frame);
            return box;
        }

        private void AddSmallButton(Control row, string text, Action action)
        {
            var button = NewButton(text, _bg2, _accent);
            button.Tag = "button:accent";
            button.Left = ContentWidth - ImeiButtonWidth;
            button.Top = 0;
            button.Width = ImeiButtonWidth;
            button.Height = 34;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Click += delegate { action(); };
            row.Controls.Add(button);
            _buttons.Add(button);
        }

        private void AddWideButton(Control parent, string text, Color foreColor, Action action, ref int y)
        {
            var button = NewButton(text, _bg2, foreColor);
            button.Tag = ButtonTagForColor(foreColor);
            button.Left = ContentLeft(parent);
            button.Top = y;
            button.Width = ContentWidth;
            button.Height = 34;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Click += delegate { action(); };
            parent.Controls.Add(button);
            TrackCentered(parent, button);
            _buttons.Add(button);
            y += 40;
        }

        private Panel NewInputFrame(int left, int top, int width, int height)
        {
            var frame = new RoundedPanel();
            frame.Tag = "input-frame";
            frame.Left = left;
            frame.Top = top;
            frame.Width = width;
            frame.Height = height;
            frame.BackColor = _bg;
            frame.FillColor = _bg2;
            frame.BorderColor = _border;
            frame.Radius = 8;
            return frame;
        }

        private Button NewButton(string text, Color backColor, Color foreColor)
        {
            var button = new ModernButton();
            button.Text = text;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.ParentBackColor = _bg;
            button.BorderColor = _border;
            button.HoverBackColor = _border;
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            button.FlatAppearance.BorderColor = _border;
            button.FlatAppearance.MouseOverBackColor = _border;
            return button;
        }

        private void AddSeparator(Control parent, ref int y)
        {
            var line = new Panel();
            line.Tag = "border";
            line.Left = ContentLeft(parent);
            line.Top = y + 4;
            line.Width = ContentWidth;
            line.Height = 1;
            line.BackColor = _border;
            parent.Controls.Add(line);
            TrackCentered(parent, line);
            y += 10;
        }

        private string ButtonTagForColor(Color color)
        {
            if (color == _accent)
            {
                return "button:accent";
            }
            if (color == _amber)
            {
                return "button:amber";
            }
            if (color == _red)
            {
                return "button:red";
            }
            return "button:muted";
        }

        private void LoadSystemThemeColors()
        {
            bool dark = IsWindowsDarkMode();
            if (dark)
            {
                _bg = Color.FromArgb(19, 22, 30);
                _bg2 = Color.FromArgb(26, 30, 40);
                _accent = Color.FromArgb(0, 229, 160);
                _red = Color.FromArgb(255, 85, 85);
                _amber = Color.FromArgb(245, 166, 35);
                _text = Color.FromArgb(232, 236, 244);
                _text2 = Color.FromArgb(122, 130, 153);
                _border = Color.FromArgb(38, 44, 60);
            }
            else
            {
                _bg = Color.FromArgb(244, 246, 251);
                _bg2 = Color.White;
                _accent = Color.FromArgb(0, 143, 104);
                _red = Color.FromArgb(217, 45, 58);
                _amber = Color.FromArgb(183, 121, 0);
                _text = Color.FromArgb(17, 24, 39);
                _text2 = Color.FromArgb(100, 116, 139);
                _border = Color.FromArgb(216, 222, 233);
            }
        }

        private static bool IsWindowsDarkMode()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1
                );
                return value is int && (int)value == 0;
            }
            catch
            {
                return false;
            }
        }

        private void RefreshSystemTheme()
        {
            LoadSystemThemeColors();
            ApplyTheme(this);
            Invalidate(true);
        }

        private void ApplyTheme(Control control)
        {
            string tag = control.Tag as string;
            if (tag == "bg")
            {
                control.BackColor = _bg;
            }
            else if (tag == "title")
            {
                control.BackColor = _bg;
                control.ForeColor = _accent;
            }
            else if (tag == "label")
            {
                control.BackColor = _bg;
                control.ForeColor = _text2;
            }
            else if (tag == "entry")
            {
                control.BackColor = _bg2;
                control.ForeColor = _text;
            }
            else if (tag == "input-frame")
            {
                control.BackColor = _bg;
                if (control is RoundedPanel)
                {
                    RoundedPanel roundedPanel = (RoundedPanel)control;
                    roundedPanel.FillColor = _bg2;
                    roundedPanel.BorderColor = _border;
                    roundedPanel.Invalidate();
                }
            }
            else if (tag == "status")
            {
                control.BackColor = _bg2;
                control.ForeColor = _text2;
            }
            else if (tag == "status-frame")
            {
                control.BackColor = _bg;
                if (control is RoundedPanel)
                {
                    RoundedPanel roundedPanel = (RoundedPanel)control;
                    roundedPanel.FillColor = _bg2;
                    roundedPanel.BorderColor = _border;
                    roundedPanel.Invalidate();
                }
            }
            else if (tag == "border")
            {
                control.BackColor = _border;
            }
            else if (tag != null && tag.StartsWith("button:", StringComparison.Ordinal))
            {
                ApplyButtonTheme((Button)control, tag);
            }
            else if (control == this)
            {
                control.BackColor = _bg;
            }

            foreach (Control child in control.Controls)
            {
                ApplyTheme(child);
            }
        }

        private void ApplyButtonTheme(Button button, string tag)
        {
            button.BackColor = _bg2;
            if (button is ModernButton)
            {
                ModernButton modernButton = (ModernButton)button;
                modernButton.ParentBackColor = _bg;
                modernButton.BorderColor = _border;
                modernButton.HoverBackColor = _border;
            }
            if (tag == "button:accent")
            {
                button.ForeColor = _accent;
            }
            else if (tag == "button:amber")
            {
                button.ForeColor = _amber;
            }
            else if (tag == "button:red")
            {
                button.ForeColor = _red;
            }
            else
            {
                button.ForeColor = _text2;
            }
            button.FlatAppearance.BorderColor = _border;
            button.FlatAppearance.MouseOverBackColor = _border;
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WmSettingChange || message.Msg == WmThemeChanged)
            {
                RefreshSystemTheme();
            }
        }

        private RouterClient MakeClient()
        {
            var client = new RouterClient(_hostBox.Text);
            if (client.Host != _hostBox.Text.Trim())
            {
                _hostBox.Text = client.Host;
            }
            return client;
        }

        private RouterClient MakeRemoClient()
        {
            var client = new RouterClient(_remoHostBox.Text);
            if (client.Host != _remoHostBox.Text.Trim())
            {
                _remoHostBox.Text = client.Host;
            }
            return client;
        }

        private void BeginUpdateCheck()
        {
            Task.Run(delegate
            {
                try
                {
                    UpdateInfoData update = FetchUpdateInfo();
                    if (update == null || string.IsNullOrWhiteSpace(update.Version))
                    {
                        return;
                    }

                    if (!IsNewerVersion(update.Version, AppVersion))
                    {
                        return;
                    }

                    BeginInvoke((Action)delegate
                    {
                        string message =
                            "发现新版本: " + update.Version + Environment.NewLine +
                            "当前版本: " + AppVersion + Environment.NewLine + Environment.NewLine +
                            "更新说明: " + (string.IsNullOrWhiteSpace(update.Notes) ? "无" : update.Notes) + Environment.NewLine + Environment.NewLine +
                            "是否打开下载地址？";

                        DialogResult result = MessageBox.Show(this, message, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (result == DialogResult.Yes && !string.IsNullOrWhiteSpace(update.Url))
                        {
                            OpenUrl(update.Url);
                        }
                    });
                }
                catch
                {
                    // 更新检测失败不影响主程序使用。
                }
            });
        }

        private static UpdateInfoData FetchUpdateInfo()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(UpdateJsonUrl);
            request.Method = "GET";
            request.Timeout = 5000;
            request.ReadWriteTimeout = 5000;
            request.Proxy = null;
            request.UserAgent = "DebugTool/" + AppVersion;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                var info = new UpdateInfoData();
                info.Version = ExtractJsonString(json, "version");
                info.Url = ExtractJsonString(json, "url");
                info.Notes = ExtractJsonString(json, "notes");
                return info;
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return "";
            }
            return Regex.Unescape(match.Groups[1].Value);
        }

        private static bool IsNewerVersion(string remoteVersion, string localVersion)
        {
            Version remote;
            Version local;
            if (Version.TryParse(NormalizeVersion(remoteVersion), out remote) && Version.TryParse(NormalizeVersion(localVersion), out local))
            {
                return remote.CompareTo(local) > 0;
            }
            return string.Compare(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase) > 0;
        }

        private static string NormalizeVersion(string version)
        {
            string value = (version ?? "").Trim().TrimStart('v', 'V');
            if (value.Length == 0)
            {
                return "0.0.0";
            }
            return value;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void BeginAutoFetch()
        {
            if (_busy)
            {
                return;
            }

            SetBusy(true);
            UpdateInfo("正在自动获取设备信息...", _text2);
            string host = _hostBox.Text;
            string remoHost = _remoHostBox.Text;

            Task.Run(delegate
            {
                string f32Imei = "";
                string remoImei = "";
                string remoError = "";
                string normalizedHost = "";
                string normalizedRemoHost = "";

                var f32Thread = new Thread(delegate()
                {
                    try
                    {
                        var client = new RouterClient(host);
                        normalizedHost = client.Host;
                        f32Imei = client.FetchImei();
                    }
                    catch
                    {
                    }
                });

                var remoThread = new Thread(delegate()
                {
                    try
                    {
                        var client = new RouterClient(remoHost);
                        normalizedRemoHost = client.Host;
                        XxremoInfo info = client.FetchXxremoDebugInfo();
                        remoImei = info.Imei;
                        if (string.IsNullOrWhiteSpace(remoImei))
                        {
                            remoError = "REMO 未解析到 IMEI: " + RouterClient.ShortResponse(info.RawInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        remoError = "REMO 获取失败: " + ex.Message;
                    }
                });

                f32Thread.IsBackground = true;
                remoThread.IsBackground = true;
                f32Thread.Start();
                remoThread.Start();
                f32Thread.Join();
                remoThread.Join();

                BeginInvoke((Action)delegate
                {
                    SetBusy(false);
                    if (!string.IsNullOrWhiteSpace(normalizedHost))
                    {
                        _hostBox.Text = normalizedHost;
                    }
                    if (!string.IsNullOrWhiteSpace(normalizedRemoHost))
                    {
                        _remoHostBox.Text = normalizedRemoHost;
                    }
                    if (!string.IsNullOrWhiteSpace(f32Imei))
                    {
                        _imeiBox.Text = f32Imei;
                    }
                    if (!string.IsNullOrWhiteSpace(remoImei))
                    {
                        _remoImeiBox.Text = remoImei;
                    }

                    if (!string.IsNullOrWhiteSpace(f32Imei) && !string.IsNullOrWhiteSpace(remoImei))
                    {
                        UpdateInfo("F32/F30Pro: " + f32Imei + Environment.NewLine + "REMO: " + remoImei, _accent);
                    }
                    else if (!string.IsNullOrWhiteSpace(f32Imei))
                    {
                        UpdateInfo("IMEI 获取成功: " + f32Imei, _accent);
                    }
                    else if (!string.IsNullOrWhiteSpace(remoImei))
                    {
                        UpdateInfo("REMO IMEI 获取成功: " + remoImei, _accent);
                    }
                    else if (!string.IsNullOrWhiteSpace(remoError))
                    {
                        UpdateInfo(remoError, _red);
                    }
                    else
                    {
                        UpdateInfo("未自动获取到设备 IMEI", _text2);
                    }
                });
            });
        }

        private void FetchImei()
        {
            _imeiBox.Focus();
            RouterClient client;
            try
            {
                client = MakeClient();
            }
            catch (Exception ex)
            {
                UpdateInfo(ex.Message, _red);
                return;
            }

            RunWorker(client, "正在获取 IMEI...", delegate(RouterClient router)
            {
                try
                {
                    string imei = router.FetchImei();
                    if (string.IsNullOrWhiteSpace(imei))
                    {
                        PostInfo("获取失败: 接口未返回 IMEI", _red);
                        return;
                    }
                    Post(delegate
                    {
                        _imeiBox.Text = imei;
                        _imeiBox.Focus();
                        _imeiBox.SelectAll();
                        UpdateInfo("IMEI 获取成功: " + imei, _accent);
                    });
                }
                catch (Exception)
                {
                    PostInfo("无法连接到设备: " + router.Host, _text2);
                }
            });
        }

        private void FetchRemoImei()
        {
            _remoImeiBox.Focus();
            RouterClient client;
            try
            {
                client = MakeRemoClient();
            }
            catch (Exception ex)
            {
                UpdateInfo(ex.Message, _red);
                return;
            }

            RunWorker(client, "正在获取 REMO IMEI...", delegate(RouterClient router)
            {
                try
                {
                    XxremoInfo info = router.FetchXxremoDebugInfo();
                    if (string.IsNullOrWhiteSpace(info.Imei))
                    {
                        PostInfo("REMO 未解析到 IMEI: " + RouterClient.ShortResponse(info.RawInfo), _red);
                        return;
                    }
                    Post(delegate
                    {
                        _remoImeiBox.Text = info.Imei;
                        _remoImeiBox.Focus();
                        _remoImeiBox.SelectAll();
                        UpdateInfo("REMO IMEI 获取成功: " + info.Imei, _accent);
                    });
                }
                catch (Exception ex)
                {
                    PostInfo("REMO 获取失败: " + ex.Message, _red);
                }
            });
        }

        private void DoTelnet()
        {
            ExecuteTask("telnetd_enable=1", "Telnet 开启成功", "确定开启 Telnet 吗？");
        }

        private void DoAll()
        {
            ExecuteTask("telnetd_enable=1&debug_enable=1", "Telnet & ADB 开启成功", "确定开启 Telnet + Debug 吗？");
        }

        private void DoDisable()
        {
            ExecuteTask("telnetd_enable=0", "Telnet 已关闭", "确定关闭 Telnet 吗？");
        }

        private void ExecuteTask(string command, string successMessage, string confirmMessage)
        {
            if (_busy)
            {
                return;
            }
            if (MessageBox.Show(this, confirmMessage, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            RouterClient client;
            try
            {
                client = MakeClient();
            }
            catch (Exception ex)
            {
                UpdateInfo(ex.Message, _red);
                return;
            }

            string initialImei = _imeiBox.Text;
            bool ok = false;
            RunWorker(client, "正在执行指令...", delegate(RouterClient router)
            {
                string imei = ResolveImei(router, initialImei);
                try
                {
                    if (command != "telnetd_enable=0")
                    {
                        SendWithOptionalImei(router, "telnetd_enable=0", imei);
                        Thread.Sleep(500);
                    }
                    ok = SendWithOptionalImei(router, command, imei);
                }
                catch
                {
                    ok = false;
                }
            }, delegate
            {
                UpdateInfo(ok ? successMessage : "指令执行失败，请检查 IP、连接或 IMEI", ok ? _accent : _red);
            });
        }

        private void DoReboot()
        {
            SendReboot(true);
        }

        private void SendReboot(bool confirm)
        {
            if (_busy)
            {
                return;
            }
            if (confirm && MessageBox.Show(this, "确定立即重启设备吗？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            RouterClient client;
            try
            {
                client = MakeClient();
            }
            catch (Exception ex)
            {
                UpdateInfo(ex.Message, _red);
                return;
            }

            string initialImei = _imeiBox.Text;
            RunWorker(client, "正在发送重启指令...", delegate(RouterClient router)
            {
                string imei = ResolveImei(router, initialImei);
                bool ok;
                try
                {
                    ok = SendWithOptionalImei(router, "reboot_now=1", imei);
                }
                catch
                {
                    ok = false;
                }
                PostInfo(ok ? "重启指令已发送" : "重启指令发送失败，请检查 IP、连接或 IMEI", ok ? _amber : _red);
            });
        }

        private void DoXxremoDebug()
        {
            if (_busy)
            {
                return;
            }
            if (MessageBox.Show(this, "确定开启 REMO Debug 吗？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            RouterClient client;
            try
            {
                client = MakeRemoClient();
            }
            catch (Exception ex)
            {
                UpdateInfo(ex.Message, _red);
                return;
            }

            bool ok = false;
            string message = "";
            string imei = "";
            RunWorker(client, "正在开启 REMO Debug...", delegate(RouterClient router)
            {
                try
                {
                    XxremoEnableResult result = router.EnableXxremoDebug(delegate(string text)
                    {
                        PostInfo(text, _text2);
                    });
                    ok = result.Ok;
                    message = result.Message;
                    imei = result.Imei;
                }
                catch (Exception ex)
                {
                    ok = false;
                    message = ex.Message;
                }
            }, delegate
            {
                if (!string.IsNullOrWhiteSpace(imei))
                {
                    _remoImeiBox.Text = imei;
                }
                UpdateInfo(message, ok ? _accent : _red);
            });
        }

        private void DoRemoReboot()
        {
            if (_busy)
            {
                return;
            }
            if (MessageBox.Show(this, "确定立即重启 REMO 设备吗？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            RouterClient client;
            try
            {
                client = MakeRemoClient();
            }
            catch (Exception ex)
            {
                UpdateInfo(ex.Message, _red);
                return;
            }

            bool ok = false;
            RunWorker(client, "正在发送 REMO 重启指令...", delegate(RouterClient router)
            {
                try
                {
                    ok = router.RebootXxremoDevice();
                }
                catch
                {
                    ok = false;
                }
            }, delegate
            {
                UpdateInfo(ok ? "REMO 重启指令已发送" : "REMO 重启指令发送失败，请检查 IP 或连接", ok ? _amber : _red);
            });
        }

        private bool SendWithOptionalImei(RouterClient router, string command, string imei)
        {
            string suffix = string.IsNullOrWhiteSpace(imei) ? "" : "&imei=" + imei.Trim();
            return router.SendPayload(command + suffix);
        }

        private string ResolveImei(RouterClient router, string initialImei)
        {
            string imei = (initialImei ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(imei))
            {
                return imei;
            }

            PostInfo("缺失 IMEI，正在尝试补全...", _text2);
            try
            {
                imei = router.FetchImei();
            }
            catch
            {
                return "";
            }

            if (!string.IsNullOrWhiteSpace(imei))
            {
                Post(delegate
                {
                    _imeiBox.Text = imei;
                    UpdateInfo("已补全 IMEI: " + imei, _accent);
                });
            }
            return imei;
        }

        private void RunWorker(RouterClient client, string startMessage, Action<RouterClient> work)
        {
            RunWorker(client, startMessage, work, null);
        }

        private void RunWorker(RouterClient client, string startMessage, Action<RouterClient> work, Action done)
        {
            if (_busy)
            {
                return;
            }

            SetBusy(true);
            UpdateInfo(startMessage, _text2);

            Task.Run(delegate
            {
                try
                {
                    work(client);
                }
                finally
                {
                    BeginInvoke((Action)delegate
                    {
                        SetBusy(false);
                        if (done != null)
                        {
                            done();
                        }
                    });
                }
            });
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (Button button in _buttons)
            {
                button.Enabled = !busy;
                button.Invalidate();
            }
        }

        private void UpdateInfo(string text, Color color)
        {
            _infoLabel.Text = text;
            _infoLabel.ForeColor = color;
        }

        private void Post(Action action)
        {
            if (IsDisposed)
            {
                return;
            }
            BeginInvoke(action);
        }

        private void PostInfo(string text, Color color)
        {
            Post(delegate { UpdateInfo(text, color); });
        }
    }

    internal sealed class RouterClient
    {
        private const int PayloadLen = 128;
        private const int GetTimeout = 3000;
        private const int PostTimeout = 8000;
        private const int XxremoTimeout = 10000;
        private const string GoformGet = "/goform/goform_get_cmd_process";
        private const string GoformSet = "/goform/goform_set_cmd_process";
        private const string XxremoPost = "/reqproc/proc_post";
        private const string AesKeyHex = "9d4d6f47f025c03a3838f2796d8a43e3";

        public RouterClient(string host)
        {
            Host = NormalizeHost(host);
            BaseUrl = "http://" + Host;
        }

        public string Host { get; private set; }
        public string BaseUrl { get; private set; }

        public string FetchImei()
        {
            string raw = SendRawHttp(
                "GET",
                GoformGet + "?cmd=imei",
                null,
                null,
                GetTimeout
            );
            return ExtractResponseField("imei", raw);
        }

        public bool SendPayload(string plaintext)
        {
            BuildResult built = BuildParams(plaintext);
            var fields = new Dictionary<string, string>();
            fields["goformId"] = "tw_telnet_config";
            fields["params"] = built.Params;
            fields["md5_check"] = built.Md5Check;

            string raw = SendRawHttp(
                "POST",
                GoformSet,
                FormEncode(fields),
                "application/x-www-form-urlencoded",
                PostTimeout
            );
            return raw.IndexOf("pass", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string SendXxremoPost(Dictionary<string, string> fields)
        {
            return SendRawHttp(
                "POST",
                XxremoPost,
                PlainJoin(fields),
                "application/x-www-form-urlencoded",
                XxremoTimeout
            );
        }

        public XxremoInfo FetchXxremoDebugInfo()
        {
            var fields = new Dictionary<string, string>();
            fields["goformId"] = "Getdebuginfo";
            string rawInfo = SendXxremoPost(fields);
            if (string.IsNullOrWhiteSpace(rawInfo))
            {
                throw new InvalidOperationException("无法连接设备或获取响应");
            }

            var info = new XxremoInfo();
            info.Imei = ExtractResponseField("imei", rawInfo);
            info.Version = ExtractResponseField("version", rawInfo);
            info.Ts = ExtractResponseField("debug_info", rawInfo);
            info.RawInfo = rawInfo.Trim();
            return info;
        }

        public bool RebootXxremoDevice()
        {
            var fields = new Dictionary<string, string>();
            fields["isTest"] = "false";
            fields["goformId"] = "REBOOT_DEVICE";
            SendXxremoPost(fields);
            return true;
        }

        public XxremoEnableResult EnableXxremoDebug(Action<string> progress)
        {
            XxremoInfo info = FetchXxremoDebugInfo();
            if (string.IsNullOrWhiteSpace(info.Imei) || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.Ts))
            {
                return new XxremoEnableResult(false, "REMO 关键字段缺失，无法生成 Key", info.Imei);
            }

            if (progress != null)
            {
                progress("REMO 信息已获取: IMEI " + info.Imei);
            }

            foreach (XxremoAlgorithm algo in XxremoAlgorithms())
            {
                if (progress != null)
                {
                    progress("正在尝试 REMO " + algo.Name + " 算法...");
                }

                var fields = new Dictionary<string, string>();
                fields["goformId"] = "SysCtlUtal";
                fields["action"] = "System_MODE";
                fields["debug_enable"] = "1";
                fields["key"] = algo.MakeKey(info.Imei, info.Ts, info.Version);

                string result = SendXxremoPost(fields);
                string compact = result.Replace(" ", "");
                if (compact.Contains("\"result\":\"0\"") || result.IndexOf("successfully", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new XxremoEnableResult(true, "REMO Debug 开启成功: " + algo.Name, info.Imei);
                }
            }

            return new XxremoEnableResult(false, "REMO Debug 开启失败，已知算法均未生效", info.Imei);
        }

        public static string ShortResponse(string text)
        {
            string value = Regex.Replace((text ?? "").Trim(), "\\s+", " ");
            return value.Length > 80 ? value.Substring(0, 80) + "..." : value;
        }

        private string SendRawHttp(string method, string path, string body, string contentType, int timeoutMs)
        {
            byte[] bodyBytes = body == null ? new byte[0] : Encoding.UTF8.GetBytes(body);
            var builder = new StringBuilder();
            builder.Append(method).Append(" ").Append(path).Append(" HTTP/1.0\r\n");
            builder.Append("Host: ").Append(Host).Append("\r\n");
            if (!string.IsNullOrEmpty(contentType))
            {
                builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
                builder.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n");
            }
            builder.Append("Connection: close\r\n");
            builder.Append("\r\n");

            byte[] headerBytes = Encoding.UTF8.GetBytes(builder.ToString());
            byte[] request = new byte[headerBytes.Length + bodyBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, request, 0, headerBytes.Length);
            if (bodyBytes.Length > 0)
            {
                Buffer.BlockCopy(bodyBytes, 0, request, headerBytes.Length, bodyBytes.Length);
            }

            SocketTargetInfo target = SocketTarget();
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            using (var stream = new MemoryStream())
            {
                socket.ReceiveTimeout = timeoutMs;
                socket.SendTimeout = timeoutMs;

                try
                {
                    socket.Connect(target.Host, target.Port);
                    socket.Send(request);
                    byte[] buffer = new byte[4096];
                    while (true)
                    {
                        try
                        {
                            int count = socket.Receive(buffer);
                            if (count <= 0)
                            {
                                break;
                            }
                            stream.Write(buffer, 0, count);
                        }
                        catch (SocketException)
                        {
                            break;
                        }
                    }
                }
                catch (SocketException)
                {
                    if (stream.Length == 0)
                    {
                        throw;
                    }
                }
                catch (IOException)
                {
                    if (stream.Length == 0)
                    {
                        throw;
                    }
                }

                byte[] raw = stream.ToArray();
                int bodyIndex = FindBodyIndex(raw);
                if (bodyIndex >= 0)
                {
                    raw = raw.Skip(bodyIndex).ToArray();
                }
                return Encoding.UTF8.GetString(raw);
            }
        }

        private SocketTargetInfo SocketTarget()
        {
            string host = Host;
            int port = 80;
            int index = host.LastIndexOf(':');
            int parsed;
            if (index > 0 && host.Count(ch => ch == ':') == 1 && int.TryParse(host.Substring(index + 1), out parsed))
            {
                host = host.Substring(0, index);
                port = parsed;
            }
            return new SocketTargetInfo(host, port);
        }

        private static string NormalizeHost(string host)
        {
            string value = (host ?? "").Trim();
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(7);
            }
            else if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(8);
            }
            value = value.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("请输入路由器 IP");
            }
            return value;
        }

        private static BuildResult BuildParams(string plaintext)
        {
            byte[] payload = Encoding.UTF8.GetBytes(plaintext);
            if (payload.Length >= PayloadLen)
            {
                throw new ArgumentException("明文过长，最多 " + (PayloadLen - 1) + " 字节");
            }

            byte[] padded = new byte[PayloadLen];
            Buffer.BlockCopy(payload, 0, padded, 0, payload.Length);

            byte[] encrypted;
            using (var aes = Aes.Create())
            {
                aes.Key = HexToBytes(AesKeyHex);
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                using (var encryptor = aes.CreateEncryptor())
                {
                    encrypted = encryptor.TransformFinalBlock(padded, 0, padded.Length);
                }
            }

            return new BuildResult(ToHex(encrypted), Md5HexBytes(payload));
        }

        private static string ExtractResponseField(string key, string text)
        {
            string escaped = Regex.Escape(key);
            string[] patterns =
            {
                "\"" + escaped + "\"\\s*:\\s*\"([^\"]*)\"",
                "'" + escaped + "'\\s*:\\s*'([^']*)'",
                "\"" + escaped + "\"\\s*:\\s*([^,}\\s]+)",
                "\\b" + escaped + "\\s*=\\s*([^&\\s,}]+)"
            };

            foreach (string pattern in patterns)
            {
                Match match = Regex.Match(text ?? "", pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim().Trim('"').Trim('\'');
                }
            }
            return "";
        }

        private static string FormEncode(Dictionary<string, string> fields)
        {
            return string.Join("&", fields.Select(delegate(KeyValuePair<string, string> item)
            {
                return Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value ?? "");
            }).ToArray());
        }

        private static string PlainJoin(Dictionary<string, string> fields)
        {
            return string.Join("&", fields.Select(delegate(KeyValuePair<string, string> item)
            {
                return item.Key + "=" + (item.Value ?? "");
            }).ToArray());
        }

        private static int FindBodyIndex(byte[] raw)
        {
            for (int i = 0; i <= raw.Length - 4; i++)
            {
                if (raw[i] == 13 && raw[i + 1] == 10 && raw[i + 2] == 13 && raw[i + 3] == 10)
                {
                    return i + 4;
                }
            }
            return -1;
        }

        private static List<XxremoAlgorithm> XxremoAlgorithms()
        {
            return new List<XxremoAlgorithm>
            {
                new XxremoAlgorithm("fang", delegate(string imei, string ts, string version)
                {
                    return Md5Hex("fang" + imei + "po" + ts + "jie" + version + "666");
                }),
                new XxremoAlgorithm("xinxun", delegate(string imei, string ts, string version)
                {
                    return Md5Hex("xinxun8888" + imei + ts + version + "xinxun6666");
                }),
                new XxremoAlgorithm("zk", delegate(string imei, string ts, string version)
                {
                    return Md5Hex("zk333" + ts + imei + version + "zk444");
                })
            };
        }

        private static string Md5Hex(string text)
        {
            return Md5HexBytes(Encoding.UTF8.GetBytes(text));
        }

        private static string Md5HexBytes(byte[] bytes)
        {
            using (var md5 = MD5.Create())
            {
                return ToHex(md5.ComputeHash(bytes));
            }
        }

        private static byte[] HexToBytes(string hex)
        {
            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return result;
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (byte item in bytes)
            {
                builder.Append(item.ToString("X2"));
            }
            return builder.ToString();
        }
    }

    internal sealed class XxremoInfo
    {
        public string Imei { get; set; }
        public string Version { get; set; }
        public string Ts { get; set; }
        public string RawInfo { get; set; }
    }

    internal sealed class ModernButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public ModernButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true
            );
            BorderColor = Color.FromArgb(216, 222, 233);
            HoverBackColor = Color.FromArgb(216, 222, 233);
            ParentBackColor = SystemColors.Control;
            Radius = 8;
        }

        public Color BorderColor { get; set; }
        public Color HoverBackColor { get; set; }
        public Color ParentBackColor { get; set; }
        public int Radius { get; set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = true;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (SolidBrush parentBrush = new SolidBrush(ParentBackColor))
            {
                e.Graphics.FillRectangle(parentBrush, ClientRectangle);
            }

            Color fill = BackColor;
            if (!Enabled)
            {
                fill = ControlPaint.Light(BackColor, 0.08f);
            }
            else if (_pressed)
            {
                fill = ControlPaint.Dark(HoverBackColor, 0.03f);
            }
            else if (_hover)
            {
                fill = HoverBackColor;
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (System.Drawing.Drawing2D.GraphicsPath path = UiPainter.CreateRoundPath(rect, Radius))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            Rectangle textRect = new Rectangle(12, 0, Width - 18, Height);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (TextAlign == ContentAlignment.MiddleCenter)
            {
                textRect = ClientRectangle;
                flags |= TextFormatFlags.HorizontalCenter;
            }
            else
            {
                flags |= TextFormatFlags.Left;
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textRect,
                Enabled ? ForeColor : ControlPaint.Light(ForeColor, 0.5f),
                flags
            );
        }

    }

    internal sealed class RoundedPanel : Panel
    {
        public RoundedPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true
            );
            FillColor = Color.White;
            BorderColor = Color.FromArgb(216, 222, 233);
            Radius = 8;
        }

        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public int Radius { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (SolidBrush parentBrush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(parentBrush, ClientRectangle);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (System.Drawing.Drawing2D.GraphicsPath path = UiPainter.CreateRoundPath(rect, Radius))
            using (SolidBrush brush = new SolidBrush(FillColor))
            using (Pen pen = new Pen(BorderColor))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }

            base.OnPaint(e);
        }
    }

    internal static class UiPainter
    {
        public static System.Drawing.Drawing2D.GraphicsPath CreateRoundPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class UpdateInfoData
    {
        public string Version { get; set; }
        public string Url { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class XxremoEnableResult
    {
        public XxremoEnableResult(bool ok, string message, string imei)
        {
            Ok = ok;
            Message = message;
            Imei = imei;
        }

        public bool Ok { get; private set; }
        public string Message { get; private set; }
        public string Imei { get; private set; }
    }

    internal sealed class XxremoAlgorithm
    {
        public XxremoAlgorithm(string name, Func<string, string, string, string> makeKey)
        {
            Name = name;
            MakeKey = makeKey;
        }

        public string Name { get; private set; }
        public Func<string, string, string, string> MakeKey { get; private set; }
    }

    internal sealed class BuildResult
    {
        public BuildResult(string parameters, string md5Check)
        {
            Params = parameters;
            Md5Check = md5Check;
        }

        public string Params { get; private set; }
        public string Md5Check { get; private set; }
    }

    internal sealed class SocketTargetInfo
    {
        public SocketTargetInfo(string host, int port)
        {
            Host = host;
            Port = port;
        }

        public string Host { get; private set; }
        public int Port { get; private set; }
    }
}
