using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace NexKVM;

internal delegate void PayloadWriter(Span<byte> buf);

internal enum MsgType : byte
{
    MouseMoveAbs = 1, // ushort xNorm(0..65535), ushort yNorm(0..65535)  host -> target
    MouseButton  = 2, // byte button(0=L,1=R,2=M,3=X1,4=X2), byte down(1)/up(0)
    MouseWheel   = 3, // short delta, byte horizontal(0/1)
    Key          = 4, // byte down(1)/up(0), ushort scan, byte extended(0/1)
    Enter        = 5, // host -> target : control has entered the target
    Leave        = 6, // host -> target : control returned to host
    Clipboard    = 7, // int32 utf8-len, utf8 bytes
    ScreenInfo   = 8, // int32 width, int32 height              target -> host
    Ping         = 9, // keepalive, no payload
}

/// <summary>
/// A framed message link over a single TCP socket. Frame = [int32 payloadLen][byte type][payload].
/// Sends are low-allocation: a single locked scratch buffer is reused for every outgoing frame.
/// Nagle is disabled so each input event goes on the wire immediately.
/// </summary>
internal sealed class Link : IDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly object _sendLock = new();
    private readonly byte[] _sendBuf = new byte[512];
    private volatile bool _closed;

    public Link(Socket socket)
    {
        _socket = socket;
        _socket.NoDelay = true;               // Nagle off — latency over throughput.
        _socket.SendBufferSize = 1 << 16;
        _socket.ReceiveBufferSize = 1 << 16;
        _stream = new NetworkStream(socket, ownsSocket: true);
    }

    public bool IsClosed => _closed;

    // ---- Outgoing ----

    private void Send(byte type, int payloadLen, PayloadWriter writePayload)
    {
        if (_closed) return;
        lock (_sendLock)
        {
            int total = 4 + 1 + payloadLen;
            byte[] buf = total <= _sendBuf.Length ? _sendBuf : new byte[total];
            BinaryPrimitives.WriteInt32LittleEndian(buf, 1 + payloadLen);
            buf[4] = type;
            if (payloadLen > 0) writePayload(buf.AsSpan(5, payloadLen));
            try { _stream.Write(buf, 0, total); }
            catch { _closed = true; }
        }
    }

    public void SendMouseMoveAbs(ushort xNorm, ushort yNorm) =>
        Send((byte)MsgType.MouseMoveAbs, 4, s =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(s, xNorm);
            BinaryPrimitives.WriteUInt16LittleEndian(s[2..], yNorm);
        });

    public void SendMouseButton(byte button, bool down) =>
        Send((byte)MsgType.MouseButton, 2, s => { s[0] = button; s[1] = (byte)(down ? 1 : 0); });

    public void SendMouseWheel(short delta, bool horizontal) =>
        Send((byte)MsgType.MouseWheel, 3, s =>
        {
            BinaryPrimitives.WriteInt16LittleEndian(s, delta);
            s[2] = (byte)(horizontal ? 1 : 0);
        });

    public void SendKey(bool down, ushort scan, bool extended) =>
        Send((byte)MsgType.Key, 4, s =>
        {
            s[0] = (byte)(down ? 1 : 0);
            BinaryPrimitives.WriteUInt16LittleEndian(s[1..], scan);
            s[3] = (byte)(extended ? 1 : 0);
        });

    public void SendEnter() => Send((byte)MsgType.Enter, 0, _ => { });
    public void SendLeave() => Send((byte)MsgType.Leave, 0, _ => { });
    public void SendPing()  => Send((byte)MsgType.Ping, 0, _ => { });

    public void SendScreenInfo(int w, int h) =>
        Send((byte)MsgType.ScreenInfo, 8, s =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(s, w);
            BinaryPrimitives.WriteInt32LittleEndian(s[4..], h);
        });

    public void SendClipboard(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        Send((byte)MsgType.Clipboard, 4 + utf8.Length, s =>
        {
            BinaryPrimitives.WriteInt32LittleEndian(s, utf8.Length);
            utf8.CopyTo(s[4..]);
        });
    }

    // ---- Incoming ----

    /// <summary>Blocking receive loop. Invokes the handler for each decoded frame until the peer closes.</summary>
    public void ReceiveLoop(IMessageHandler handler)
    {
        var lenBuf = new byte[4];
        var payload = new byte[512];
        try
        {
            while (!_closed)
            {
                ReadExact(lenBuf, 4);
                int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
                if (len <= 0 || len > (1 << 20)) throw new IOException("bad frame length");
                if (payload.Length < len) payload = new byte[len];
                ReadExact(payload, len);
                Dispatch(payload, len, handler);
            }
        }
        catch { /* peer gone or bad frame */ }
        finally { _closed = true; handler.OnDisconnected(); }
    }

    private void ReadExact(byte[] buf, int count)
    {
        int off = 0;
        while (off < count)
        {
            int n = _stream.Read(buf, off, count - off);
            if (n <= 0) throw new IOException("closed");
            off += n;
        }
    }

    private static void Dispatch(byte[] buf, int len, IMessageHandler h)
    {
        var type = (MsgType)buf[0];
        var p = buf.AsSpan(1, len - 1);
        switch (type)
        {
            case MsgType.MouseMoveAbs:
                h.OnMouseMoveAbs(BinaryPrimitives.ReadUInt16LittleEndian(p),
                                 BinaryPrimitives.ReadUInt16LittleEndian(p[2..]));
                break;
            case MsgType.MouseButton:
                h.OnMouseButton(p[0], p[1] != 0);
                break;
            case MsgType.MouseWheel:
                h.OnMouseWheel(BinaryPrimitives.ReadInt16LittleEndian(p), p[2] != 0);
                break;
            case MsgType.Key:
                h.OnKey(p[0] != 0, BinaryPrimitives.ReadUInt16LittleEndian(p[1..]), p[3] != 0);
                break;
            case MsgType.Enter: h.OnEnter(); break;
            case MsgType.Leave: h.OnLeave(); break;
            case MsgType.ScreenInfo:
                h.OnScreenInfo(BinaryPrimitives.ReadInt32LittleEndian(p),
                               BinaryPrimitives.ReadInt32LittleEndian(p[4..]));
                break;
            case MsgType.Clipboard:
                int n = BinaryPrimitives.ReadInt32LittleEndian(p);
                h.OnClipboard(Encoding.UTF8.GetString(p.Slice(4, n)));
                break;
            case MsgType.Ping: break;
        }
    }

    public void Dispose()
    {
        _closed = true;
        try { _stream.Dispose(); } catch { }
        try { _socket.Dispose(); } catch { }
    }
}

/// <summary>Callbacks fired by <see cref="Link.ReceiveLoop"/>. Roles implement only what they use.</summary>
internal interface IMessageHandler
{
    void OnMouseMoveAbs(ushort xNorm, ushort yNorm) { }
    void OnMouseButton(byte button, bool down) { }
    void OnMouseWheel(short delta, bool horizontal) { }
    void OnKey(bool down, ushort scan, bool extended) { }
    void OnEnter() { }
    void OnLeave() { }
    void OnScreenInfo(int width, int height) { }
    void OnClipboard(string text) { }
    void OnDisconnected() { }
}
