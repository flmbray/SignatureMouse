using System;
using System.Runtime.InteropServices;

namespace SignatureMouse.Replay;

internal sealed class WindowsDrawRectSelector : IDrawRectSelector
{
    public ScreenRect? SelectRectangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Draw-rect selection is only supported on Windows.");
        }

        var state = new SelectionState();
        state.Run();

        if (state.Cancelled || state.Result == null)
        {
            return null;
        }

        var answer = MessageBoxW(IntPtr.Zero, "Use the selected area for the signature?", "SignatureMouse", MB_YESNO | MB_ICONQUESTION | MB_TOPMOST);
        return answer == IDYES ? state.Result : null;
    }

    private sealed class SelectionState
    {
        private static readonly WndProcDelegate WndProc = WindowProc;
        private static bool _classRegistered;

        public bool Cancelled { get; private set; }
        public ScreenRect? Result { get; private set; }

        private int _virtualLeft;
        private int _virtualTop;
        private int _virtualWidth;
        private int _virtualHeight;

        private bool _dragging;
        private POINT _start;
        private POINT _current;
        private IntPtr _hwnd;

        public void Run()
        {
            _virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            _virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            _virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            _virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            if (!_classRegistered)
            {
                var wc = new WNDCLASS
                {
                    style = CS_HREDRAW | CS_VREDRAW,
                    lpfnWndProc = WndProc,
                    hInstance = GetModuleHandleW(null),
                    hbrBackground = GetStockObject(BLACK_BRUSH),
                    lpszClassName = "SignatureMouseRectSelector"
                };

                if (RegisterClassW(ref wc) == 0)
                {
                    throw new InvalidOperationException("Unable to register selector window class.");
                }
                _classRegistered = true;
            }

            var handle = GCHandle.Alloc(this);
            try
            {
                _hwnd = CreateWindowExW(
                    WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_LAYERED,
                    "SignatureMouseRectSelector",
                    "",
                    WS_POPUP,
                    _virtualLeft,
                    _virtualTop,
                    _virtualWidth,
                    _virtualHeight,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    GetModuleHandleW(null),
                    GCHandle.ToIntPtr(handle));

                if (_hwnd == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Unable to create selector window.");
                }

                // Dim the entire virtual desktop so the selection rectangle stands out.
                SetLayeredWindowAttributes(_hwnd, 0, 160, LWA_ALPHA);
                ShowWindow(_hwnd, SW_SHOW);
                UpdateWindow(_hwnd);

                MSG msg;
                while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }
            }
            finally
            {
                if (_hwnd != IntPtr.Zero)
                {
                    DestroyWindow(_hwnd);
                }
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            var state = GetState(hwnd);
            switch (msg)
            {
                case WM_NCCREATE:
                {
                    var create = Marshal.PtrToStructure<CREATESTRUCT>(lParam);
                    SetWindowLongPtr(hwnd, GWLP_USERDATA, create.lpCreateParams);
                    return new IntPtr(1);
                }
                case WM_SETCURSOR:
                    SetCursor(LoadCursorW(IntPtr.Zero, IDC_CROSS));
                    return new IntPtr(1);
                case WM_LBUTTONDOWN:
                    if (state != null)
                    {
                        state._dragging = true;
                        state._start = GetPoint(lParam);
                        state._current = state._start;
                        SetCapture(hwnd);
                        state.UpdateRegion();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case WM_MOUSEMOVE:
                    if (state != null && state._dragging)
                    {
                        state._current = GetPoint(lParam);
                        state.UpdateRegion();
                        InvalidateRect(hwnd, IntPtr.Zero, false);
                    }
                    return IntPtr.Zero;
                case WM_LBUTTONUP:
                    if (state != null && state._dragging)
                    {
                        state._dragging = false;
                        ReleaseCapture();
                        state._current = GetPoint(lParam);
                        var rect = state.GetSelectionRect();
                        if (rect.Width > 3 && rect.Height > 3)
                        {
                            state.Result = rect;
                        }
                        else
                        {
                            // Treat tiny drags as accidental clicks.
                            state.Cancelled = true;
                        }
                        PostQuitMessage(0);
                    }
                    return IntPtr.Zero;
                case WM_KEYDOWN:
                    if (wParam == new IntPtr(VK_ESCAPE) && state != null)
                    {
                        state.Cancelled = true;
                        PostQuitMessage(0);
                        return IntPtr.Zero;
                    }
                    break;
                case WM_PAINT:
                    if (state != null)
                    {
                        state.Paint(hwnd);
                        return IntPtr.Zero;
                    }
                    break;
                case WM_ERASEBKGND:
                    if (state != null)
                    {
                        var rect = new RECT { left = 0, top = 0, right = state._virtualWidth, bottom = state._virtualHeight };
                        FillRect(wParam, ref rect, GetStockObject(BLACK_BRUSH));
                        return new IntPtr(1);
                    }
                    break;
                case WM_DESTROY:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            return DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        private void Paint(IntPtr hwnd)
        {
            if (!GetSelectionRectClient(out var rect))
            {
                ValidateRect(hwnd, IntPtr.Zero);
                return;
            }

            var ps = new PAINTSTRUCT();
            var hdc = BeginPaint(hwnd, ref ps);

            var pen = CreatePen(PS_SOLID, 2, 0x00FFFFFF);
            var oldPen = SelectObject(hdc, pen);
            var oldBrush = SelectObject(hdc, GetStockObject(HOLLOW_BRUSH));

            Rectangle(hdc, rect.left, rect.top, rect.right, rect.bottom);

            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
            DeleteObject(pen);
            EndPaint(hwnd, ref ps);
        }

        private void UpdateRegion()
        {
            if (!GetSelectionRectClient(out var rect))
            {
                SetWindowRgn(_hwnd, IntPtr.Zero, true);
                return;
            }

            var full = CreateRectRgn(0, 0, _virtualWidth, _virtualHeight);
            var hole = CreateRectRgn(rect.left, rect.top, rect.right, rect.bottom);
            CombineRgn(full, full, hole, RGN_DIFF);
            SetWindowRgn(_hwnd, full, true);
            DeleteObject(hole);
        }

        private ScreenRect GetSelectionRect()
        {
            int left = Math.Min(_start.X, _current.X) + _virtualLeft;
            int right = Math.Max(_start.X, _current.X) + _virtualLeft;
            int top = Math.Min(_start.Y, _current.Y) + _virtualTop;
            int bottom = Math.Max(_start.Y, _current.Y) + _virtualTop;
            return new ScreenRect(left, top, right, bottom);
        }

        private bool GetSelectionRectClient(out RECT rect)
        {
            if (!_dragging)
            {
                rect = default;
                return false;
            }

            int left = Math.Min(_start.X, _current.X);
            int right = Math.Max(_start.X, _current.X);
            int top = Math.Min(_start.Y, _current.Y);
            int bottom = Math.Max(_start.Y, _current.Y);
            rect = new RECT { left = left, top = top, right = right, bottom = bottom };
            return right - left > 1 && bottom - top > 1;
        }

        private static SelectionState? GetState(IntPtr hwnd)
        {
            var ptr = GetWindowLongPtr(hwnd, GWLP_USERDATA);
            if (ptr == IntPtr.Zero) return null;
            var handle = GCHandle.FromIntPtr(ptr);
            return handle.Target as SelectionState;
        }

        private static POINT GetPoint(IntPtr lParam)
        {
            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            return new POINT { X = x, Y = y };
        }
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const int CS_HREDRAW = 0x0002;
    private const int CS_VREDRAW = 0x0001;

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;

    private const int SW_SHOW = 5;

    private const int LWA_ALPHA = 0x00000002;

    private const int GWLP_USERDATA = -21;

    private const int WM_NCCREATE = 0x0081;
    private const int WM_SETCURSOR = 0x0020;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_DESTROY = 0x0002;

    private const int VK_ESCAPE = 0x1B;

    private const int IDYES = 6;
    private const int MB_YESNO = 0x00000004;
    private const int MB_ICONQUESTION = 0x00000020;
    private const int MB_TOPMOST = 0x00040000;

    private const int PS_SOLID = 0;
    private const int BLACK_BRUSH = 4;
    private const int HOLLOW_BRUSH = 5;
    private const int RGN_DIFF = 4;

    private const int IDC_CROSS = 32515;

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREATESTRUCT
    {
        public IntPtr lpCreateParams;
        public IntPtr hInstance;
        public IntPtr hMenu;
        public IntPtr hwndParent;
        public int cy;
        public int cx;
        public int y;
        public int x;
        public int style;
        public IntPtr lpszName;
        public IntPtr lpszClass;
        public uint dwExStyle;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCapture(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    private static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreatePen(int fnPenStyle, int nWidth, int crColor);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool Rectangle(IntPtr hdc, int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int i);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
