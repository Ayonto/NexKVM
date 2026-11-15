namespace NexKVM;

/// <summary>
/// Hides the mouse cursor across the whole system by swapping every system cursor for a
/// fully transparent one, and restores the defaults afterwards. Used on the host while the
/// pointer is "away" on the target, so no stray cursor is left sitting on the PC screen.
/// </summary>
internal static class CursorHider
{
    private static readonly uint[] AllCursors =
    {
        Native.OCR_NORMAL, Native.OCR_IBEAM, Native.OCR_WAIT, Native.OCR_CROSS, Native.OCR_UP,
        Native.OCR_SIZENWSE, Native.OCR_SIZENESW, Native.OCR_SIZEWE, Native.OCR_SIZENS,
        Native.OCR_SIZEALL, Native.OCR_NO, Native.OCR_HAND, Native.OCR_APPSTARTING,
    };

    private static bool _hidden;

    public static void Hide()
    {
        if (_hidden) return;
        _hidden = true;
        IntPtr hInst = Native.GetModuleHandle(null);
        foreach (uint id in AllCursors)
        {
            // A 32x32 monochrome cursor: AND plane all 1s + XOR plane all 0s == fully transparent.
            var and = new byte[128]; // 32 rows * 4 bytes
            var xor = new byte[128];
            for (int i = 0; i < and.Length; i++) and[i] = 0xFF;
            IntPtr blank = Native.CreateCursor(hInst, 0, 0, 32, 32, and, xor);
            if (blank != IntPtr.Zero)
                Native.SetSystemCursor(blank, id); // takes ownership of the handle
        }
    }

    public static void Restore()
    {
        if (!_hidden) return;
        _hidden = false;
        // Reload the user's default system cursors. No settings-change broadcast: that is a
        // synchronous message to every window and would stall the input hook at the hand-back.
        Native.SystemParametersInfo(Native.SPI_SETCURSORS, 0, IntPtr.Zero, 0);
    }
}
