namespace NexKVM;

/// <summary>
/// Synthesises mouse/keyboard input on the target machine via SendInput.
/// Positions are absolute-normalised (0..65535) against the primary screen.
/// </summary>
internal static class Injector
{
    private static void Send(Native.INPUT input)
    {
        var arr = new[] { input };
        Native.SendInput(1, arr, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
    }

    public static void MoveAbsolute(ushort xNorm, ushort yNorm)
    {
        Send(new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.INPUTUNION
            {
                mi = new Native.MOUSEINPUT
                {
                    dx = xNorm,
                    dy = yNorm,
                    dwFlags = Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE,
                    dwExtraInfo = Native.InjectTag,
                }
            }
        });
    }

    public static void Button(byte button, bool down)
    {
        uint flags = button switch
        {
            0 => down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP,
            1 => down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP,
            2 => down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP,
            _ => down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP,
        };
        uint mouseData = button switch { 3 => Native.XBUTTON1, 4 => Native.XBUTTON2, _ => 0u };
        Send(new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.INPUTUNION
            {
                mi = new Native.MOUSEINPUT { dwFlags = flags, mouseData = mouseData, dwExtraInfo = Native.InjectTag }
            }
        });
    }

    public static void Wheel(short delta, bool horizontal)
    {
        Send(new Native.INPUT
        {
            type = Native.INPUT_MOUSE,
            u = new Native.INPUTUNION
            {
                mi = new Native.MOUSEINPUT
                {
                    dwFlags = horizontal ? Native.MOUSEEVENTF_HWHEEL : Native.MOUSEEVENTF_WHEEL,
                    mouseData = unchecked((uint)delta),
                    dwExtraInfo = Native.InjectTag,
                }
            }
        });
    }

    public static void Key(bool down, ushort scan, bool extended)
    {
        uint flags = Native.KEYEVENTF_SCANCODE;
        if (extended) flags |= Native.KEYEVENTF_EXTENDEDKEY;
        if (!down) flags |= Native.KEYEVENTF_KEYUP;
        Send(new Native.INPUT
        {
            type = Native.INPUT_KEYBOARD,
            u = new Native.INPUTUNION
            {
                ki = new Native.KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags, dwExtraInfo = Native.InjectTag }
            }
        });
    }
}
