using System.Drawing;
using System.Net;
using System.Windows.Forms;

namespace NexKVM;

internal sealed class MainForm : Form
{
    private readonly Settings _cfg = Settings.Load();
    private readonly string? _autoRole;

    // Every control with its position in 96-dpi units; LayoutUi scales these to the screen.
    private readonly List<(Control Control, Rectangle Rect)> _slots = new();

    private readonly StatusPill _pill = new();
    private readonly SegmentedControl _role = new("Host", "Receiver");
    private readonly Card _cardMode = new() { Title = "Mode" };
    private readonly Card _cardConn = new() { Title = "Connection" };
    private readonly Card _cardOptions = new() { Title = "Options" };
    private readonly Card _cardArrange = new() { Title = "Screen arrangement" };
    private readonly Card _cardActivity = new() { Title = "Activity" };
    private readonly Label _roleHint = Hint(), _lblAddr = Hint(), _lblPort = Hint(), _connHint = Hint(),
        _arrangeHint = Hint(), _footer = Hint();
    private readonly InputBox _txtIp = new(), _txtMyIp = new(), _txtPort = new();
    private readonly AccentButton _btnCopy = new() { Text = "Copy", Kind = ButtonKind.Ghost, Font = Theme.Body };
    private readonly AccentButton _btnRun = new() { Text = "Start" };
    private readonly ToggleSwitch _clip = new() { Text = "Sync clipboard text between the two PCs", Checked = true };
    private readonly ArrangementView _arrange = new();
    private readonly RichTextBox _log = new()
    {
        BorderStyle = BorderStyle.None, ReadOnly = true, DetectUrls = false, TabStop = false,
        BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Small,
        ScrollBars = RichTextBoxScrollBars.Vertical
    };

    private HostEngine? _host;
    private TargetEngine? _target;
    private bool _running;
    private EngineStatus _status = EngineStatus.Stopped;

    private bool IsHost => _role.SelectedIndex == 0;

    public MainForm(string? autoRole = null, string? autoIp = null)
    {
        _autoRole = autoRole;
        BuildUi();
        ApplySettings();
        if (autoRole == "host") { _role.SelectedIndex = 0; if (autoIp != null) _txtIp.Text = autoIp; }
        else if (autoRole == "receiver") _role.SelectedIndex = 1;
        UpdateRole();
    }

    // ---------------- construction ----------------

    private void BuildUi()
    {
        Text = "NexKVM";
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        using (var ico = typeof(MainForm).Assembly.GetManifestResourceStream("NexKVM.ico"))
            if (ico != null) Icon = new Icon(ico);

        Controls.Add(_pill);

        // Left column.
        Place(_cardMode, this, 24, 92, 400, 126);
        Place(_role, _cardMode, 20, 46, 360, 38);
        Place(_roleHint, _cardMode, 17, 94, 360, 20);

        Place(_cardConn, this, 24, 230, 400, 150);
        Place(_lblAddr, _cardConn, 17, 44, 250, 20);
        Place(_lblPort, _cardConn, 279, 44, 98, 20);
        Place(_txtIp, _cardConn, 20, 68, 250, 36);
        Place(_txtMyIp, _cardConn, 20, 68, 180, 36);
        Place(_btnCopy, _cardConn, 208, 68, 62, 36);
        Place(_txtPort, _cardConn, 282, 68, 98, 36);
        Place(_connHint, _cardConn, 17, 116, 360, 20);

        Place(_cardOptions, this, 24, 392, 400, 92);
        Place(_clip, _cardOptions, 20, 48, 360, 28);

        Place(_btnRun, this, 24, 500, 400, 46);
        Place(_footer, this, 21, 558, 400, 40);

        // Right column.
        Place(_cardArrange, this, 440, 92, 456, 288);
        Place(_arrangeHint, _cardArrange, 17, 40, 416, 20);
        Place(_arrange, _cardArrange, 20, 66, 416, 210);

        Place(_cardActivity, this, 440, 392, 456, 206);
        Place(_log, _cardActivity, 20, 46, 424, 146);

        _lblPort.Text = "Port";
        _arrangeHint.Text = "Drag the receiver to the side of this PC where it sits.";
        _txtIp.Box.PlaceholderText = "192.168.1.50";
        _txtMyIp.Box.ReadOnly = true;
        _txtPort.Box.MaxLength = 5;

        _role.SelectedIndexChanged += (_, _) => UpdateRole();
        _btnRun.Click += (_, _) => ToggleRun();
        _btnCopy.Click += async (_, _) =>
        {
            if (_txtMyIp.Text.Length == 0) return;
            Clipboard.SetText(_txtMyIp.Text);
            _btnCopy.Text = "Copied";
            await Task.Delay(1200);
            _btnCopy.Text = "Copy";
        };
        _log.HandleCreated += (_, _) => Theme.DarkScrollbars(_log);

        LayoutUi();
    }

    private static Label Hint() => new() { ForeColor = Theme.Muted, Font = Theme.Small, AutoSize = false };

    private void Place(Control c, Control parent, int x, int y, int w, int h)
    {
        parent.Controls.Add(c);
        _slots.Add((c, new Rectangle(x, y, w, h)));
    }

    private void LayoutUi()
    {
        float s = DeviceDpi / 96f;
        Rectangle R(int x, int y, int w, int h) => new((int)(x * s), (int)(y * s), (int)(w * s), (int)(h * s));

        ClientSize = R(0, 0, 920, 616).Size;
        foreach (var (c, r) in _slots) c.Bounds = R(r.X, r.Y, r.Width, r.Height);

        if (!IsHost)   // no arrangement to show on a receiver: the activity log takes the column
        {
            _cardActivity.Bounds = R(440, 92, 456, 506);
            _log.Bounds = R(20, 46, 424, 446);
        }
        _pill.Bounds = R(746, 28, 150, 30);
        ShowStatus(_status);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowChrome(this);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutUi();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_autoRole != null && !_running) ToggleRun();  // power-user auto-start once the UI is live
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        const TextFormatFlags flags = TextFormatFlags.NoPadding;
        int x = (int)(24 * s), y = (int)(14 * s);
        TextRenderer.DrawText(g, "Nex", Theme.Brand, new Point(x, y), Theme.Text, flags);
        x += TextRenderer.MeasureText(g, "Nex", Theme.Brand, Size.Empty, flags).Width;
        TextRenderer.DrawText(g, "KVM", Theme.Brand, new Point(x, y), Theme.Accent, flags);
        TextRenderer.DrawText(g, "Mouse and keyboard sharing over your local network", Theme.Body,
            new Point((int)(25 * s), (int)(54 * s)), Theme.Muted, flags);
    }

    // ---------------- role / state ----------------

    private void UpdateRole()
    {
        bool host = IsHost;
        _txtIp.Visible = host;
        _txtMyIp.Visible = _btnCopy.Visible = !host;
        _cardArrange.Visible = host;

        if (host)
        {
            _roleHint.Text = "This PC shares its mouse and keyboard with the receiver.";
            _lblAddr.Text = "Receiver address";
            _connHint.Text = "The address shown in NexKVM on the receiver PC.";
            _footer.Text = "Press Ctrl+Alt+F12 at any time to bring control back to this PC.";
        }
        else
        {
            var ips = NetUtil.LocalIPv4();
            _roleHint.Text = "This PC is controlled by the host's mouse and keyboard.";
            _lblAddr.Text = "This PC's address";
            _txtMyIp.Text = ips.Count > 0 ? ips[0] : "";
            _connHint.Text = ips.Count switch
            {
                0 => "No network address found. Check the network connection.",
                1 => "Enter this address in NexKVM on the host PC.",
                _ => "Also reachable at " + string.Join(", ", ips.Skip(1)),
            };
            _footer.Text = "Keep NexKVM running here while the host is in control. It can stay minimised.";
        }
        LayoutUi();
    }

    private void ApplySettings()
    {
        _role.SelectedIndex = _cfg.Role == "receiver" ? 1 : 0;
        _txtIp.Text = _cfg.TargetIp;
        _txtPort.Text = _cfg.Port.ToString();
        _arrange.Side = Enum.TryParse(_cfg.Side, out TargetSide side) ? side : TargetSide.Left;
        _clip.Checked = _cfg.Clipboard;
    }

    private void SaveSettings()
    {
        _cfg.Role = IsHost ? "host" : "receiver";
        _cfg.TargetIp = _txtIp.Text.Trim();
        _cfg.Port = int.TryParse(_txtPort.Text, out int p) ? p : 24800;
        _cfg.Side = _arrange.Side.ToString();
        _cfg.Clipboard = _clip.Checked;
        _cfg.Save();
    }

    private void ToggleRun()
    {
        if (_running) { StopEngines(); return; }

        if (!int.TryParse(_txtPort.Text, out int port) || port is <= 0 or >= 65536)
        {
            AppendLog("ERROR: the port must be a number between 1 and 65535.");
            _txtPort.Box.Focus();
            return;
        }

        if (IsHost)
        {
            string ip = _txtIp.Text.Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                AppendLog("ERROR: enter the receiver's address first, for example 192.168.1.50.");
                _txtIp.Box.Focus();
                return;
            }
            var host = new HostEngine(ip, port, _arrange.Side, _clip.Checked);
            host.Log += AppendLog;
            host.StatusChanged += ShowStatus;
            _host = host;
            host.Start();
        }
        else
        {
            var target = new TargetEngine(port, _clip.Checked);
            target.Log += AppendLog;
            target.StatusChanged += ShowStatus;
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
        ShowStatus(EngineStatus.Stopped);
    }

    private void SetRunningUi(bool running)
    {
        _btnRun.Text = running ? "Stop" : "Start";
        _btnRun.Kind = running ? ButtonKind.Danger : ButtonKind.Primary;
        _role.Enabled = _txtIp.Enabled = _txtPort.Enabled = _clip.Enabled = _arrange.Enabled = !running;
    }

    private void ShowStatus(EngineStatus s)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => ShowStatus(s))); return; }
        _status = s;
        (string text, Color color) = s switch
        {
            EngineStatus.Listening => ("Waiting for host", Theme.Warning),
            EngineStatus.Connecting => ("Connecting", Theme.Warning),
            EngineStatus.Connected => ("Connected", Theme.Success),
            EngineStatus.Controlling => ("Controlling receiver", Theme.Accent),
            _ => ("Not running", Theme.Muted),
        };
        _pill.Set(text, color);
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => AppendLog(line))); return; }

        if (_log.Lines.Length > 500)   // keep the log bounded: drop the oldest lines
        {
            _log.ReadOnly = false;
            _log.Select(0, _log.GetFirstCharIndexFromLine(200));
            _log.SelectedText = "";
            _log.ReadOnly = true;
        }
        _log.Select(_log.TextLength, 0);
        _log.SelectionColor = Theme.Muted;
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  ");
        _log.SelectionColor = line.StartsWith("ERROR") ? Theme.Danger : Theme.Text;
        _log.AppendText(line + "\n");
        _log.ScrollToCaret();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { CursorHider.Restore(); } catch { }
        StopEngines();
        base.OnFormClosing(e);
    }
}
