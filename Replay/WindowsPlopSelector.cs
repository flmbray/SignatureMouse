using System;
using System.Runtime.InteropServices;
using SignatureMouse.Models;

namespace SignatureMouse.Replay;

internal sealed class WindowsPlopSelector : IPlopSelector
{
    public PlopResult? SelectPlacement(SignaturePath signature)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Plop is only supported on Windows.");
        }

        var state = new SelectionState(signature);
        state.Run();
        return state.Cancelled ? null : state.Result;
    }

    private sealed class SelectionState
    {
        private static readonly WndProcDelegate WndProc = WindowProc;
        private static bool _classRegistered;

        private readonly SignaturePath _signature;
        private readonly float _minX;
        private readonly float _minY;
        private readonly float _width;
        private readonly float _height;

        public bool Cancelled { get; private set; }
        public PlopResult? Result { get; private set; }

        private int _virtualLeft;
        private int _virtualTop;
        private int _virtualWidth;
        private int _virtualHeight;

        private bool _dragging;
        private POINT _dragStart;
        private float _originStartX;
        private float _originStartY;

        private float _originX;
        private float _originY;
        private float _scale;
        private IntPtr _hwnd;

        public SelectionState(SignaturePath signature)
        {
            _signature = signature;
            var bounds = signature.GetBounds();
            _minX = bounds.MinX;
            _minY = bounds.MinY;
            _width = Math.Max(1, bounds.MaxX - bounds.MinX);
            _height = Math.Max(1, bounds.MaxY - bounds.MinY);
        }

        public void Run()
        {
            _virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
            _virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
            _virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            _virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // Start reasonably sized so the overlay is easy to grab and reposition.
            float fitScale = Math.Min((_virtualWidth * 0.4f) / _width, (_virtualHeight * 0.4f) / _height);
            if (float.IsNaN(fitScale) || fitScale <= 0) fitScale = 1f;
            _scale = Math.Min(1f, fitScale);

            _originX = _virtualLeft + (_virtualWidth - _width * _scale) / 2f;
            _originY = _virtualTop + (_virtualHeight - _height * _scale) / 2f;

            if (!_classRegistered)
            {
                var wc = new WNDCLASS
                {
                    style = CS_HREDRAW | CS_VREDRAW,
                    lpfnWndProc = WndProc,
                    hInstance = GetModuleHandleW(null),
                    hbrBackground = GetStockObject(BLACK_BRUSH),
                    lpszClassName = "SignatureMousePlopSelector"
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
                    "SignatureMousePlopSelector",
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

                // Dim the screen while keeping the overlay responsive.
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
                    // Crosshair matches screenshot-style selection UX.
                    SetCursor(LoadCursorW(IntPtr.Zero, IDC_CROSS));
                    return new IntPtr(1);
                case WM_LBUTTONDOWN:
                    if (state != null)
                    {
                        state._dragging = true;
                        state._dragStart = GetPoint(lParam);
                        state._originStartX = state._originX;
                        state._originStartY = state._originY;
                        SetCapture(hwnd);
                    }
                    return IntPtr.Zero;
                case WM_MOUSEMOVE:
                    if (state != null && state._dragging)
                    {
                        var current = GetPoint(lParam);
                        state._originX = state._originStartX + (current.X - state._dragStart.X);
                        state._originY = state._originStartY + (current.Y - state._dragStart.Y);
                        InvalidateRect(hwnd, IntPtr.Zero, true);
                    }
                    return IntPtr.Zero;
                case WM_LBUTTONUP:
                    if (state != null && state._dragging)
                    {
                        state._dragging = false;
                        ReleaseCapture();
                    }
                    return IntPtr.Zero;
                case WM_MOUSEWHEEL:
                    if (state != null)
                    {
                        state.HandleMouseWheel(wParam, lParam);
                    }
                    return IntPtr.Zero;
                case WM_KEYDOWN:
                    if (state != null)
                    {
                        if (wParam == new IntPtr(VK_ESCAPE))
                        {
                            state.Cancelled = true;
                            PostQuitMessage(0);
                            return IntPtr.Zero;
                        }
                        if (wParam == new IntPtr(VK_RETURN) || wParam == new IntPtr(VK_SPACE))
                        {
                            state.Commit();
                            PostQuitMessage(0);
                            return IntPtr.Zero;
                        }
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

        private void HandleMouseWheel(IntPtr wParam, IntPtr lParam)
        {
            long wParamValue = wParam.ToInt64();
            int delta = (short)((wParamValue >> 16) & 0xFFFF);
            if (delta == 0) return;

            // Ctrl + wheel = coarse scale; wheel alone = fine scale.
            bool ctrl = (wParamValue & MK_CONTROL) != 0;
            float step = ctrl ? 0.10f : 0.02f;
            float factor = 1f + step * MathF.Sign(delta);
            float newScale = _scale * factor;
            newScale = Math.Clamp(newScale, 0.05f, 10f);

            var centerX = _originX + _width * _scale / 2f;
            var centerY = _originY + _height * _scale / 2f;

            _scale = newScale;
            _originX = centerX - _width * _scale / 2f;
            _originY = centerY - _height * _scale / 2f;

            InvalidateRect(_hwnd, IntPtr.Zero, true);
        }

        private void Commit()
        {
            float offsetX = _originX - _minX * _scale;
            float offsetY = _originY - _minY * _scale;
            // Capture placement in signature coordinates so replay can start immediately.
            Result = new PlopResult(_scale, offsetX, offsetY);
        }

        private void Paint(IntPtr hwnd)
        {
            var ps = new PAINTSTRUCT { rgbReserved = new byte[32] };
            var hdc = BeginPaint(hwnd, ref ps);

            var rect = new RECT { left = 0, top = 0, right = _virtualWidth, bottom = _virtualHeight };
            FillRect(hdc, ref rect, GetStockObject(BLACK_BRUSH));

            var pen = CreatePen(PS_SOLID, 2, 0x00FFFFFF);
            var oldPen = SelectObject(hdc, pen);
            var oldBrush = SelectObject(hdc, GetStockObject(HOLLOW_BRUSH));

            DrawSignature(hdc);
            DrawBounds(hdc);

            SelectObject(hdc, oldBrush);
            SelectObject(hdc, oldPen);
            DeleteObject(pen);
            EndPaint(hwnd, ref ps);
        }

        private void DrawSignature(IntPtr hdc)
        {
            foreach (var stroke in _signature.Strokes)
            {
                if (stroke.Count == 0) continue;
                var p0 = stroke[0];
                var startX = (int)MathF.Round((p0.X - _minX) * _scale + _originX - _virtualLeft);
                var startY = (int)MathF.Round((p0.Y - _minY) * _scale + _originY - _virtualTop);
                MoveToEx(hdc, startX, startY, IntPtr.Zero);

                for (int i = 1; i < stroke.Count; i++)
                {
                    var p = stroke[i];
                    var x = (int)MathF.Round((p.X - _minX) * _scale + _originX - _virtualLeft);
                    var y = (int)MathF.Round((p.Y - _minY) * _scale + _originY - _virtualTop);
                    LineTo(hdc, x, y);
                }
            }
        }

        private void DrawBounds(IntPtr hdc)
        {
            int left = (int)MathF.Round(_originX - _virtualLeft);
            int top = (int)MathF.Round(_originY - _virtualTop);
            int right = (int)MathF.Round(_originX + _width * _scale - _virtualLeft);
            int bottom = (int)MathF.Round(_originY + _height * _scale - _virtualTop);
            Rectangle(hdc, left, top, right, bottom);
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
            long value = lParam.ToInt64();
            int x = (short)(value & 0xFFFF);
            int y = (short)((value >> 16) & 0xFFFF);
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
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_PAINT = 0x000F;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_DESTROY = 0x0002;

    private const int VK_ESCAPE = 0x1B;
    private const int VK_RETURN = 0x0D;
    private const int VK_SPACE = 0x20;

    private const int MK_CONTROL = 0x0008;

    private const int PS_SOLID = 0;
    private const int BLACK_BRUSH = 4;
    private const int HOLLOW_BRUSH = 5;

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
    private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr lpPoint);

    [DllImport("gdi32.dll")]
    private static extern bool LineTo(IntPtr hdc, int x, int y);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
