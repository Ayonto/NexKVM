using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace NexKVM;

internal sealed class MainForm : Form
{
    // Palette.
    private static readonly Color Bg = Color.FromArgb(30, 30, 36);
    private static readonly Color Panel = Color.FromArgb(40, 40, 48);
    private static readonly Color TextCol = Color.FromArgb(232, 232, 238);
    private static readonly Color SubText = Color.FromArgb(150, 150, 162);
    private static readonly Color Border = Color.FromArgb(62, 62, 74);
    private static readonly Color Accent = Color.FromArgb(61, 200, 140);   // green
    private static readonly Color StopCol = Color.FromArgb(226, 92, 92);   // red
    private static readonly Color Amber = Color.FromArgb(230, 180, 70);
    private static readonly Color Blue = Color.FromArgb(90, 150, 245);

    private readonly Settings _cfg = Settings.Load();

    private RadioButton _rbHost = null!, _rbReceiver = null!;
    private Panel _pnlConfig = null!;
    private TextBox _txtIp = null!, _txtPort = null!, _txtLog = null!, _txtMyIps = null!;
    private ComboBox _cmbSide = null!;
    private CheckBox _chkClip = null!;
    private Label _lblHostIp = null!, _lblHostSide = null!, _lblMyIps = null!, _lblStatus = null!, _lblDot = null!;
    private Button _btnStartStop = null!, _btnCopyIp = null!;

    private HostEngine? _host;
    private TargetEngine? _target;
    private bool _running;
    private readonly string? _autoRole;

    public MainForm(string? autoRole = null, string? autoIp = null)
    {
        _autoRole = autoRole;
        BuildUi();
        ApplySettings();
        if (autoRole == "host") { _rbHost.Checked = true; if (autoIp != null) _txtIp.Text = autoIp; }
        else if (autoRole == "receiver") { _rbReceiver.Checked = true; }
        UpdateRoleVisibility();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_autoRole != null && !_running) ToggleRun();  // power-user auto-start once the UI is live
    }

    // ---------------- UI construction ----------------

    private void BuildUi()
    {
        Text = "NexKVM";
        BackColor = Bg;
        ForeColor = TextCol;
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(540, 640);
        MinimumSize = new Size(540, 560);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Bg, Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));  // header
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));  // role
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176)); // config
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // action
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // log
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // footer
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildRole(), 0, 1);
        root.Controls.Add(BuildConfig(), 0, 2);
        root.Controls.Add(BuildAction(), 0, 3);
        root.Controls.Add(BuildLog(), 0, 4);
        root.Controls.Add(BuildFooter(), 0, 5);
    }

    private Control BuildHeader()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "NexKVM", Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
            ForeColor = TextCol, AutoSize = true, Location = new Point(0, 0)
        };
        var sub = new Label
        {
            Text = "Share one mouse & keyboard across your PCs over the LAN",
            UseMnemonic = false,   // don't treat '&' as an accelerator
            ForeColor = SubText, AutoSize = true, Location = new Point(2, 38)
        };
        p.Controls.Add(title);
        p.Controls.Add(sub);
        return p;
    }

    private Control BuildRole()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        _rbHost = MakeRadio("This PC controls (Host)", 0);
        _rbReceiver = MakeRadio("This PC is controlled (Receiver)", 262);
        _rbHost.CheckedChanged += (_, _) => UpdateRoleVisibility();
        _rbReceiver.CheckedChanged += (_, _) => UpdateRoleVisibility();
        p.Controls.Add(_rbHost);
        p.Controls.Add(_rbReceiver);
        return p;
    }

    private RadioButton MakeRadio(string text, int x) => new()
    {
        Text = text, Location = new Point(x, 12), AutoSize = true,
        ForeColor = TextCol, Font = new Font("Segoe UI", 10f), FlatStyle = FlatStyle.Standard
    };

    private Control BuildConfig()
    {
        _pnlConfig = new Panel { Dock = DockStyle.Fill, BackColor = Panel, Padding = new Padding(16) };
        _pnlConfig.Paint += (s, e) => DrawBorder(e.Graphics, _pnlConfig);

        // --- Host controls ---
        _lblHostIp = MakeLabel("Receiver's IP address", 16, 16);
        _txtIp = MakeTextBox(16, 40, 300);
        _txtIp.PlaceholderText = "e.g. 192.168.1.50";

        _lblHostSide = MakeLabel("Receiver is on my", 16, 78);
        _cmbSide = new ComboBox
        {
            Location = new Point(150, 74), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat, BackColor = Bg, ForeColor = TextCol
        };
        _cmbSide.Items.AddRange(new object[] { "Left side", "Right side" });
        _cmbSide.SelectedIndex = 0;

        // --- Receiver controls ---
        _lblMyIps = MakeLabel("Tell the host one of these IP addresses:", 16, 16);
        _txtMyIps = new TextBox
        {
            Location = new Point(16, 40), Width = 300, ReadOnly = true, BackColor = Bg, ForeColor = Accent,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10f)
        };
        _btnCopyIp = MakeSmallButton("Copy", 324, 39);
        _btnCopyIp.Click += (_, _) => { if (_txtMyIps.Text.Length > 0) Clipboard.SetText(_txtMyIps.Text); };

        // --- Shared controls (port + clipboard) ---
        var lblPort = MakeLabel("Port", 16, 120);
        _txtPort = MakeTextBox(60, 116, 80);
        _txtPort.Text = "24800";
        _chkClip = new CheckBox
        {
            Text = "Sync clipboard text", Location = new Point(170, 118), AutoSize = true,
            ForeColor = TextCol, Checked = true
        };

        _pnlConfig.Controls.AddRange(new Control[]
        {
            _lblHostIp, _txtIp, _lblHostSide, _cmbSide,
            _lblMyIps, _txtMyIps, _btnCopyIp,
            lblPort, _txtPort, _chkClip
        });
        return _pnlConfig;
    }

    private Control BuildAction()
    {
        var p = new Panel { Dock = DockStyle.Fill };
        _btnStartStop = new Button
        {
            Text = "Start", Location = new Point(0, 10), Size = new Size(150, 40),
            FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.Black,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold), Cursor = Cursors.Hand
        };
        _btnStartStop.FlatAppearance.BorderSize = 0;
        _btnStartStop.Click += (_, _) => ToggleRun();

        _lblDot = new Label
        {
            Text = "●", ForeColor = SubText, AutoSize = true, Font = new Font("Segoe UI", 12f),
            Location = new Point(172, 18)
        };
        _lblStatus = new Label
        {
            Text = "Stopped", ForeColor = SubText, AutoSize = true, Font = new Font("Segoe UI", 10.5f),
            Location = new Point(194, 20)
        };
        p.Controls.Add(_btnStartStop);
        p.Controls.Add(_lblDot);
        p.Controls.Add(_lblStatus);
        return p;
    }

    private Control BuildLog()
    {
        _txtLog = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(22, 22, 27), ForeColor = Color.FromArgb(200, 205, 210),
            BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f)
        };
        return _txtLog;
    }

    private Control BuildFooter() => new Label
    {
        Dock = DockStyle.Fill, Text = "Failsafe: press Ctrl+Alt+F12 to force control back to this PC.",
        ForeColor = SubText, Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleLeft
    };

    // ---------------- helpers ----------------

    private static Label MakeLabel(string text, int x, int y) => new()
    {
        Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = SubText
    };

    private static TextBox MakeTextBox(int x, int y, int w) => new()
    {
        Location = new Point(x, y), Width = w, BackColor = Bg, ForeColor = TextCol,
        BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10f)
    };

    private Button MakeSmallButton(string text, int x, int y)
    {
        var b = new Button
        {
            Text = text, Location = new Point(x, y), Size = new Size(64, 26),
            FlatStyle = FlatStyle.Flat, BackColor = Panel, ForeColor = TextCol, Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Border;
        return b;
    }

    private static void DrawBorder(Graphics g, Control c)
    {
        using var pen = new Pen(Border);
        g.DrawRectangle(pen, 0, 0, c.Width - 1, c.Height - 1);
    }

    // ---------------- role / state ----------------

    private void UpdateRoleVisibility()
    {
        bool host = _rbHost.Checked;
        _lblHostIp.Visible = _txtIp.Visible = _lblHostSide.Visible = _cmbSide.Visible = host;
        _lblMyIps.Visible = _txtMyIps.Visible = _btnCopyIp.Visible = !host;
        if (!host)
        {
            var ips = NetUtil.LocalIPv4();
            _txtMyIps.Text = ips.Count > 0 ? string.Join("   ", ips) : "(no LAN IP found)";
        }
    }

    private void ApplySettings()
    {
        if (_cfg.Role == "receiver") _rbReceiver.Checked = true; else _rbHost.Checked = true;
        _txtIp.Text = _cfg.TargetIp;
        _txtPort.Text = _cfg.Port.ToString();
        _cmbSide.SelectedIndex = _cfg.Side == "Right" ? 1 : 0;
        _chkClip.Checked = _cfg.Clipboard;
    }

    private void SaveSettings()
    {
        _cfg.Role = _rbReceiver.Checked ? "receiver" : "host";
        _cfg.TargetIp = _txtIp.Text.Trim();
        _cfg.Port = int.TryParse(_txtPort.Text, out int p) ? p : 24800;
        _cfg.Side = _cmbSide.SelectedIndex == 1 ? "Right" : "Left";
        _cfg.Clipboard = _chkClip.Checked;
        _cfg.Save();
    }

    private void ToggleRun()
    {
        if (_running) { StopEngines(); return; }

        int port = int.TryParse(_txtPort.Text, out int p) && p is > 0 and < 65536 ? p : 24800;
        bool clip = _chkClip.Checked;

        if (_rbHost.Checked)
        {
            string ip = _txtIp.Text.Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                AppendLog("Please enter a valid receiver IP address (e.g. 192.168.1.50).");
                return;
            }
            var side = _cmbSide.SelectedIndex == 1 ? TargetSide.Right : TargetSide.Left;
            var host = new HostEngine(ip, port, side, clip);
            host.Log += AppendLog;
            host.StatusChanged += OnStatus;
            _host = host;
            host.Start();
        }
        else
        {
            var target = new TargetEngine(port, clip);
            target.Log += AppendLog;
            target.StatusChanged += OnStatus;
            _target = target;
            target.Start();
        }

        SaveSettings();
        _running = true;
        SetRunningUi(true);
    }

    private void StopEngines()
    {
        _host?.Stop(); _host = null;
        _target?.Stop(); _target = null;
        _running = false;
        SetRunningUi(false);
        OnStatus(EngineStatus.Stopped);
    }

    private void SetRunningUi(bool running)
    {
        _btnStartStop.Text = running ? "Stop" : "Start";
        _btnStartStop.BackColor = running ? StopCol : Accent;
        _rbHost.Enabled = _rbReceiver.Enabled = !running;
        _txtIp.Enabled = _cmbSide.Enabled = _txtPort.Enabled = _chkClip.Enabled = !running;
    }

    private void OnStatus(EngineStatus s)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => OnStatus(s))); return; }
        (string text, Color col) = s switch
        {
            EngineStatus.Listening => ("Listening for host…", Amber),
            EngineStatus.Connecting => ("Connecting…", Amber),
            EngineStatus.Connected => ("Connected", Accent),
            EngineStatus.Controlling => ("Controlling receiver", Blue),
            _ => ("Stopped", SubText),
        };
        _lblStatus.Text = text;
        _lblStatus.ForeColor = col;
        _lblDot.ForeColor = col;
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(line))); return; }
        string stamp = DateTime.Now.ToString("HH:mm:ss");
        _txtLog.AppendText($"[{stamp}] {line}\r\n");
        if (_txtLog.TextLength > 16000)
            _txtLog.Text = _txtLog.Text[^12000..];
        _txtLog.SelectionStart = _txtLog.TextLength;
        _txtLog.ScrollToCaret();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { CursorHider.Restore(); } catch { }
        StopEngines();
        base.OnFormClosing(e);
    }
}
