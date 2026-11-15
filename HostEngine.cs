using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NexKVM;

/// <summary>
/// Host side (the PC with the physical mouse/keyboard). Installs low-level hooks, detects the
/// edge crossing on the configured side of the desktop, and while "controlling" the target it swallows local
/// input, hides the local cursor, and forwards everything over the link. The host owns the
/// authoritative virtual cursor position inside the target's screen, so movement never drifts.
/// </summary>
internal sealed class HostEngine : IMessageHandler
{
    private const int VK_CONTROL = 0x11, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
    private const int VK_MENU = 0x12, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    private const int VK_F12 = 0x7B;

    public event Action<string>? Log;
    public event Action<EngineStatus>? StatusChanged;

    private readonly string _targetIp;
    private readonly int _port;
    private readonly TargetSide _side;
    private readonly bool _clipboard;

    private Native.HookProc? _mouseProc, _keyProc;
    private IntPtr _mouseHook, _keyHook;
    private uint _hookThreadId;
    private Thread? _hookThread, _connThread, _clipThread;

    private volatile Link? _link;
    private volatile bool _running;

    private int _vL, _vT, _vW, _vH;      // bounds of the whole desktop (all monitors)
    private int _cx, _cy;                // where the hidden cursor is parked while away
    private volatile int _lapW, _lapH;

    private volatile bool _onRemote;
    private double _vx, _vy;
    private bool _ctrlDown, _altDown;
    private string? _lastRemoteClip;

    private readonly HashSet<ushort> _heldScans = new();
    private readonly HashSet<byte> _heldButtons = new();

    public HostEngine(string targetIp, int port, TargetSide side, bool clipboard)
    {
        _targetIp = targetIp; _port = port; _side = side; _clipboard = clipboard;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _vL = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        _vT = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        _vW = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        _vH = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
        _cx = Native.GetSystemMetrics(Native.SM_CXSCREEN) / 2;
        _cy = Native.GetSystemMetrics(Native.SM_CYSCREEN) / 2;

        Log?.Invoke($"Host started. Desktop {_vW}x{_vH}. Receiver on the {_side.ToString().ToLowerInvariant()} edge.");
        
        StartHookThread();
        _clipThread = new Thread(ClipboardLoop) { IsBackground = true };
        _clipThread.Start();
        _connThread = new Thread(ConnectionLoop) { IsBackground = true };
        _connThread.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        ForceLocal();
        try { _link?.Dispose(); } catch { }
        _link = null;
        // Wake the hook thread's message loop so it can unhook and exit.
        if (_hookThreadId != 0) Native.PostThreadMessage(_hookThreadId, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
        StatusChanged?.Invoke(EngineStatus.Stopped);
        Log?.Invoke("Host stopped.");
    }

    // ---------- Connection ----------

    private void ConnectionLoop()
    {
        while (_running)
        {
            try
            {
                StatusChanged?.Invoke(EngineStatus.Connecting);
                Log?.Invoke($"Connecting to {_targetIp}:{_port} ...");
                var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                sock.Connect(IPAddress.Parse(_targetIp), _port);
                sock.SendTimeout = 2000;
                var link = new Link(sock);
                _link = link;
                StatusChanged?.Invoke(EngineStatus.Connected);
                Log?.Invoke("Connected to the receiver.");
                link.ReceiveLoop(this);
            }
            catch (Exception ex)
            {
                if (_running) Log?.Invoke($"Connection error: {ex.Message}");
            }
            finally
            {
                ForceLocal();
                try { _link?.Dispose(); } catch { }
                _link = null;
                _lapW = _lapH = 0;
                if (_running) StatusChanged?.Invoke(EngineStatus.Connecting);
            }
            if (_running) Thread.Sleep(1000);
        }
    }

    // ---------- Hook thread ----------

    private void StartHookThread()
    {
        _hookThread = new Thread(() =>
        {
            _hookThreadId = Native.GetCurrentThreadId();
            IntPtr hMod = Native.GetModuleHandle(null);
            _mouseProc = MouseHook;
            _keyProc = KeyHook;
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, hMod, 0);
            _keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyProc, hMod, 0);
            if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
            {
                Log?.Invoke("ERROR: failed to install input hooks.");
                return;
            }
            while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }
            Native.UnhookWindowsHookEx(_mouseHook);
            Native.UnhookWindowsHookEx(_keyHook);
            _mouseHook = _keyHook = IntPtr.Zero;
        })
        { IsBackground = true, Priority = ThreadPriority.Highest };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    // ---------- Mouse hook ----------

    private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);

        var ms = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
        bool injected = (ms.flags & Native.LLMHF_INJECTED) != 0 || ms.dwExtraInfo == Native.InjectTag;
        int msg = (int)wParam;
        Link? link = _link;

        if (!_onRemote)
        {
            if (msg == Native.WM_MOUSEMOVE && !injected && link is { IsClosed: false } && _lapW > 0)
            {
                bool atEdge = _side switch
                {
                    TargetSide.Left => ms.pt.x <= _vL,
                    TargetSide.Right => ms.pt.x >= _vL + _vW - 1,
                    TargetSide.Top => ms.pt.y <= _vT,
                    _ => ms.pt.y >= _vT + _vH - 1,
                };
                if (atEdge)
                {
                    EnterRemote(ms.pt, link);
                    return (IntPtr)1;
                }
            }
            return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        if (injected) return (IntPtr)1;
        if (link is null || link.IsClosed) { ForceLocal(); return (IntPtr)1; }

        switch (msg)
        {
            case Native.WM_MOUSEMOVE:
                // A swallowed move never moves the real cursor, so it stays parked and the hook's
                // point is simply "parked position + this move". No re-centring, no warp, no jumps.
                Native.GetCursorPos(out var cur);
                int dx = ms.pt.x - cur.x, dy = ms.pt.y - cur.y;
                if (dx != 0 || dy != 0) UpdateVirtual(dx, dy, link);
                break;
            case Native.WM_LBUTTONDOWN: ForwardButton(0, true, link); break;
            case Native.WM_LBUTTONUP:   ForwardButton(0, false, link); break;
            case Native.WM_RBUTTONDOWN: ForwardButton(1, true, link); break;
            case Native.WM_RBUTTONUP:   ForwardButton(1, false, link); break;
            case Native.WM_MBUTTONDOWN: ForwardButton(2, true, link); break;
            case Native.WM_MBUTTONUP:   ForwardButton(2, false, link); break;
            case Native.WM_XBUTTONDOWN: ForwardButton((byte)(HiWord(ms.mouseData) == Native.XBUTTON1 ? 3 : 4), true, link); break;
            case Native.WM_XBUTTONUP:   ForwardButton((byte)(HiWord(ms.mouseData) == Native.XBUTTON1 ? 3 : 4), false, link); break;
            case Native.WM_MOUSEWHEEL:  link.SendMouseWheel((short)HiWord(ms.mouseData), horizontal: false); break;
            case Native.WM_MOUSEHWHEEL: link.SendMouseWheel((short)HiWord(ms.mouseData), horizontal: true); break;
        }
        return (IntPtr)1;
    }

    private void ForwardButton(byte button, bool down, Link link)
    {
        if (down) _heldButtons.Add(button); else _heldButtons.Remove(button);
        link.SendMouseButton(button, down);
    }

    private static int HiWord(uint v) => unchecked((short)(v >> 16));

    // ---------- Keyboard hook ----------

    private IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);

        var kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        bool injected = (kb.flags & Native.LLKHF_INJECTED) != 0 || kb.dwExtraInfo == Native.InjectTag;
        int msg = (int)wParam;
        bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
        bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
        uint vk = kb.vkCode;

        if (!injected)
        {
            if (vk is VK_CONTROL or VK_LCONTROL or VK_RCONTROL) { if (down) _ctrlDown = true; else if (up) _ctrlDown = false; }
            if (vk is VK_MENU or VK_LMENU or VK_RMENU) { if (down) _altDown = true; else if (up) _altDown = false; }

            if (down && vk == VK_F12 && _ctrlDown && _altDown)
            {
                ForceLocal();
                return (IntPtr)1;
            }
        }

        if (!_onRemote) return Native.CallNextHookEx(_keyHook, nCode, wParam, lParam);
        if (injected) return (IntPtr)1;

        Link? link = _link;
        if (link is null || link.IsClosed) { ForceLocal(); return (IntPtr)1; }

        bool extended = (kb.flags & Native.LLKHF_EXTENDED) != 0;
        var scan = (ushort)kb.scanCode;
        if (down) _heldScans.Add(scan); else if (up) _heldScans.Remove(scan);
        link.SendKey(down, scan, extended);
        return (IntPtr)1;
    }

    // ---------- State transitions ----------

    private bool Horizontal => _side is TargetSide.Left or TargetSide.Right;

    private void EnterRemote(Native.POINT pt, Link link)
    {
        // Enter on the receiver's edge that faces this PC, at the matching position along it,
        // so the cursor continues from where it left instead of appearing mid-screen.
        if (Horizontal)
        {
            _vx = _side == TargetSide.Left ? _lapW - 1 : 0;
            _vy = (pt.y - _vT) / (double)_vH * _lapH;
        }
        else
        {
            _vy = _side == TargetSide.Top ? _lapH - 1 : 0;
            _vx = (pt.x - _vL) / (double)_vW * _lapW;
        }
        _vx = Math.Clamp(_vx, 0, _lapW - 1);
        _vy = Math.Clamp(_vy, 0, _lapH - 1);

        Native.SetCursorPos(_cx, _cy);
        CursorHider.Hide();
        _onRemote = true;
        link.SendEnter();
        SendVirtual(link);
        StatusChanged?.Invoke(EngineStatus.Controlling);
        Log?.Invoke("Control moved to the receiver.");
    }

    private void UpdateVirtual(int dx, int dy, Link link)
    {
        _vx += dx;
        _vy += dy;

        // Pushed back past the edge we came in through → hand control back to this PC.
        bool leaving = _side switch
        {
            TargetSide.Left => _vx > _lapW - 1,
            TargetSide.Right => _vx < 0,
            TargetSide.Top => _vy > _lapH - 1,
            _ => _vy < 0,
        };
        _vx = Math.Clamp(_vx, 0, _lapW - 1);
        _vy = Math.Clamp(_vy, 0, _lapH - 1);
        if (leaving) { LeaveRemote(); return; }
        SendVirtual(link);
    }

    private void SendVirtual(Link link)
    {
        ushort xN = (ushort)Math.Clamp(_vx / Math.Max(1, _lapW - 1) * 65535.0, 0, 65535);
        ushort yN = (ushort)Math.Clamp(_vy / Math.Max(1, _lapH - 1) * 65535.0, 0, 65535);
        link.SendMouseMoveAbs(xN, yN);
    }

    private void LeaveRemote()
    {
        // Reappear just inside the edge we crossed, at the matching position along it.
        int x, y;
        if (Horizontal)
        {
            x = _side == TargetSide.Left ? _vL + 2 : _vL + _vW - 3;
            y = _vT + (int)(_vy / Math.Max(1, _lapH - 1) * (_vH - 1));
        }
        else
        {
            y = _side == TargetSide.Top ? _vT + 2 : _vT + _vH - 3;
            x = _vL + (int)(_vx / Math.Max(1, _lapW - 1) * (_vW - 1));
        }
        ReleaseHeld();
        _onRemote = false;
        _link?.SendLeave();
        Native.SetCursorPos(x, y);
        CursorHider.Restore();
        if (_link is { IsClosed: false }) StatusChanged?.Invoke(EngineStatus.Connected);
        Log?.Invoke("Control returned to this PC.");
    }

    private void ForceLocal()
    {
        if (!_onRemote) { CursorHider.Restore(); return; }
        ReleaseHeld();
        _onRemote = false;
        CursorHider.Restore();
        Native.SetCursorPos(_cx, _cy);
        Log?.Invoke("Control returned to this PC (forced).");
    }

    private void ReleaseHeld()
    {
        Link? link = _link;
        if (link is { IsClosed: false })
        {
            foreach (var s in _heldScans) link.SendKey(false, s, false);
            foreach (var b in _heldButtons) link.SendMouseButton(b, false);
        }
        _heldScans.Clear();
        _heldButtons.Clear();
    }

    // ---------- Clipboard ----------

    private void ClipboardLoop()
    {
        if (!_clipboard) return;
        ClipboardHelper.RunPoller(
            keepRunning: () => _running,
            onChanged: text => { _lastRemoteClip = text; _link?.SendClipboard(text); },
            lastRemote: () => _lastRemoteClip);
    }

    // ---------- Incoming from target ----------

    public void OnScreenInfo(int width, int height)
    {
        _lapW = width; _lapH = height;
        Log?.Invoke($"Receiver screen {width}x{height}. Edge crossing is active.");
    }

    public void OnClipboard(string text)
    {
        if (!_clipboard) return;
        _lastRemoteClip = text;
        ClipboardHelper.SetText(text);
    }

    public void OnDisconnected()
    {
        if (_running) Log?.Invoke("Receiver disconnected.");
        ForceLocal();
    }
}
