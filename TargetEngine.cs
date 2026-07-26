using System.Net;
using System.Net.Sockets;

namespace NexKVM;

/// <summary>
/// Target side (the machine being controlled). Listens for the host, injects the received
/// input via SendInput, reports its screen size, and mirrors clipboard text.
/// </summary>
internal sealed class TargetEngine : IMessageHandler
{
    public event Action<string>? Log;
    public event Action<EngineStatus>? StatusChanged;

    private readonly int _port;
    private readonly bool _clipboard;
    private TcpListener? _listener;
    private volatile Link? _link;
    private volatile bool _running;
    private Thread? _acceptThread, _clipThread;
    private string? _lastRemoteClip;
    private int _w, _h;

    public TargetEngine(int port, bool clipboard) { _port = port; _clipboard = clipboard; }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _w = Native.GetSystemMetrics(Native.SM_CXSCREEN);
        _h = Native.GetSystemMetrics(Native.SM_CYSCREEN);

        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
        }
        catch (Exception ex)
        {
            _running = false;
            Log?.Invoke($"ERROR: cannot listen on port {_port}: {ex.Message}");
            StatusChanged?.Invoke(EngineStatus.Stopped);
            return;
        }

        Log?.Invoke($"Receiver started. Screen {_w}x{_h}. Listening on port {_port}.");
        StatusChanged?.Invoke(EngineStatus.Listening);

        _clipThread = new Thread(ClipboardLoop) { IsBackground = true };
        _clipThread.Start();
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true };
        _acceptThread.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _link?.Dispose(); } catch { }
        _link = null;
        StatusChanged?.Invoke(EngineStatus.Stopped);
        Log?.Invoke("Receiver stopped.");
    }

    private void AcceptLoop()
    {
        while (_running)
        {
            Socket sock;
            try { sock = _listener!.AcceptSocket(); }
            catch { break; }

            Log?.Invoke($"Host connected from {sock.RemoteEndPoint}.");
            StatusChanged?.Invoke(EngineStatus.Connected);
            var link = new Link(sock);
            _link = link;
            link.SendScreenInfo(_w, _h);
            link.ReceiveLoop(this);
            _link = null;
            if (_running)
            {
                StatusChanged?.Invoke(EngineStatus.Listening);
                Log?.Invoke("Host disconnected — listening again.");
            }
        }
    }

    // ---------- Incoming input ----------

    public void OnMouseMoveAbs(ushort xNorm, ushort yNorm) => Injector.MoveAbsolute(xNorm, yNorm);
    public void OnMouseButton(byte button, bool down) => Injector.Button(button, down);
    public void OnMouseWheel(short delta, bool horizontal) => Injector.Wheel(delta, horizontal);
    public void OnKey(bool down, ushort scan, bool extended) => Injector.Key(down, scan, extended);

    public void OnEnter() => Log?.Invoke("<-- control is HERE");
    public void OnLeave() => Log?.Invoke("--> control returned to host");

    public void OnClipboard(string text)
    {
        if (!_clipboard) return;
        _lastRemoteClip = text;
        ClipboardHelper.SetText(text);
    }

    public void OnDisconnected() { }

    private void ClipboardLoop()
    {
        if (!_clipboard) return;
        ClipboardHelper.RunPoller(
            keepRunning: () => _running,
            onChanged: text => { _lastRemoteClip = text; _link?.SendClipboard(text); },
            lastRemote: () => _lastRemoteClip);
    }
}
