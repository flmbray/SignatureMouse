using System;
using System.Runtime.InteropServices;

namespace SignatureMouse.Replay;

internal sealed class WindowsReplayBackend : IReplayBackend
{
    public string Name => "windows";

    public int ScreenWidth => GetSystemMetrics(SM_CXSCREEN);
    public int ScreenHeight => GetSystemMetrics(SM_CYSCREEN);

    public (int X, int Y) GetCursorPosition()
    {
        if (!GetCursorPos(out var point))
        {
            return (0, 0);
        }
        return (point.X, point.Y);
    }

    public void MoveTo(int x, int y)
    {
        SendMouseMove(x, y);
    }

    public void MouseDown()
    {
        SendMouseInput(MOUSEEVENTF_LEFTDOWN);
    }

    public void MouseUp()
    {
        SendMouseInput(MOUSEEVENTF_LEFTUP);
    }

    private static void SendMouseInput(uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dwFlags = flags
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private void SendMouseMove(int x, int y)
    {
        int width = Math.Max(1, ScreenWidth - 1);
        int height = Math.Max(1, ScreenHeight - 1);
        int mappedX = (int)Math.Round(x * 65535.0 / width);
        int mappedY = (int)Math.Round(y * 65535.0 / height);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = mappedX,
                dy = mappedY,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
