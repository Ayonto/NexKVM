using System.Runtime.InteropServices;

namespace NexKVM;

/// <summary>
/// Minimal CF_UNICODETEXT clipboard get/set plus a change-poller. Poll-based (200 ms)
/// so it needs no window or STA message loop, and its overhead is negligible.
/// </summary>
internal static class ClipboardHelper
{
    private static readonly object Gate = new();

    public static string? GetText()
    {
        lock (Gate)
        {
            if (!Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT)) return null;
            if (!TryOpen()) return null;
            try
            {
                IntPtr h = Native.GetClipboardData(Native.CF_UNICODETEXT);
                if (h == IntPtr.Zero) return null;
                IntPtr ptr = Native.GlobalLock(h);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { Native.GlobalUnlock(h); }
            }
            finally { Native.CloseClipboard(); }
        }
    }

    public static void SetText(string text)
    {
        lock (Gate)
        {
            if (!TryOpen()) return;
            try
            {
                Native.EmptyClipboard();
                int bytes = (text.Length + 1) * 2;
                IntPtr hMem = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (UIntPtr)bytes);
                if (hMem == IntPtr.Zero) return;
                IntPtr dst = Native.GlobalLock(hMem);
                if (dst == IntPtr.Zero) return;
                try
                {
                    // Copy string + terminating null.
                    for (int i = 0; i < text.Length; i++)
                        Marshal.WriteInt16(dst, i * 2, text[i]);
                    Marshal.WriteInt16(dst, text.Length * 2, 0);
                }
                finally { Native.GlobalUnlock(hMem); }
                Native.SetClipboardData(Native.CF_UNICODETEXT, hMem); // ownership passes to clipboard
            }
            finally { Native.CloseClipboard(); }
        }
    }

    private static bool TryOpen()
    {
        // The clipboard can be briefly locked by another process; retry a few times.
        for (int i = 0; i < 10; i++)
        {
            if (Native.OpenClipboard(IntPtr.Zero)) return true;
            Thread.Sleep(5);
        }
        return false;
    }

    /// <summary>
    /// Watches for local clipboard changes and reports the new text. Ignores a value we just
    /// set ourselves (echo suppression) so the two machines don't ping-pong.
    /// </summary>
    public static void RunPoller(Func<bool> keepRunning, Action<string> onChanged, Func<string?> lastRemote)
    {
        uint last = Native.GetClipboardSequenceNumber();
        while (keepRunning())
        {
            Thread.Sleep(200);
            uint seq = Native.GetClipboardSequenceNumber();
            if (seq == last) continue;
            last = seq;
            string? text = GetText();
            if (string.IsNullOrEmpty(text)) continue;
            if (text == lastRemote()) continue; // we just applied this from the peer
            onChanged(text!);
        }
    }
}
