using System.Runtime.InteropServices;

namespace Framelet;

internal sealed class ScrollTarget : IDisposable
{
    private readonly Rectangle region;
    private readonly Point scrollPoint;
    private readonly IntPtr rootWindow;
    private readonly IntPtr childWindow;
    private readonly Rectangle initialBounds;
    private readonly uint targetThread;
    private readonly uint targetProcess;
    private Point originalPointer;
    private bool pointerPlaced;
    private bool prepared;
    private bool disposed;
    internal Point Position => scrollPoint;

    private ScrollTarget(Rectangle region, Point scrollPoint, IntPtr rootWindow, IntPtr childWindow)
    {
        this.region = region;
        this.scrollPoint = scrollPoint;
        this.rootWindow = rootWindow;
        this.childWindow = childWindow;
        initialBounds = WindowRectangle(rootWindow);
        targetThread = GetWindowThreadProcessId(rootWindow, out targetProcess);
    }

    internal static ScrollTarget FromRegion(Rectangle region, Point? targetPoint = null)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region), "请先选择有效的滚动区域。");
        var center = targetPoint ?? new Point(region.Left + region.Width / 2, region.Top + region.Height / 2);
        if (!region.Contains(center))
            throw new ArgumentOutOfRangeException(nameof(targetPoint), "滚动目标必须位于捕捉区域内。");
        var child = WindowAt(center);
        var root = child == IntPtr.Zero ? IntPtr.Zero : GetAncestor(child, 2);
        return new ScrollTarget(region, center, root, child);
    }

    internal bool Prepare(out string error)
    {
        if (!ValidateWindow(out error) || !ValidateInputState(out error)) return false;
        if (!GetCursorPos(out var pointer))
        {
            error = "无法读取鼠标位置，请重新选择滚动区域。";
            return false;
        }
        if (!pointerPlaced) originalPointer = new Point(pointer.X, pointer.Y);
        bool activationRequested = SetForegroundWindow(rootWindow);
        if (activationRequested && SendMessageTimeout(rootWindow, 0, IntPtr.Zero, IntPtr.Zero, 0x23, 750, out _) == IntPtr.Zero)
        {
            error = "所选窗口未及时响应激活请求，请等待窗口恢复后重试。";
            return false;
        }
        if (NativeCapture.GetForegroundWindow() != rootWindow)
        {
            error = "无法激活所选窗口，请关闭遮挡窗口后重新选择滚动区域。";
            return false;
        }
        FocusChild();
        if (!SetCursorPos(scrollPoint.X, scrollPoint.Y))
        {
            error = "无法将鼠标移入滚动区域，请重试。";
            return false;
        }
        pointerPlaced = true;
        if (!ValidateReady(out error)) return false;
        prepared = true;
        return true;
    }

    internal bool ScrollDown(out string error)
    {
        if (!prepared)
        {
            error = "滚动目标尚未准备好，请重新开始捕捉。";
            return false;
        }
        if (!ValidateReady(out error) || !ValidateInputState(out error)) return false;
        Input[] inputs = [new()
        {
            Type = 0,
            Data = new InputData
            {
                Mouse = new MouseInput { MouseData = unchecked((uint)-120), Flags = 0x0800 }
            }
        }];
        if (SendInput(1, inputs, Marshal.SizeOf<Input>()) != 1)
        {
            error = "系统未接受滚动输入；若目标以管理员身份运行，请让两个程序使用相同权限后重试。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool ValidateReady(out string error)
    {
        if (!ValidateWindow(out error)) return false;
        if (NativeCapture.GetForegroundWindow() != rootWindow)
        {
            error = "当前已切换到其他窗口，自动滚动已停止。";
            return false;
        }
        if (!GetCursorPos(out var pointer) || !region.Contains(pointer.X, pointer.Y))
        {
            error = "鼠标已移出所选区域，自动滚动已停止。";
            return false;
        }
        var underPointer = WindowAt(new Point(pointer.X, pointer.Y));
        if (underPointer == IntPtr.Zero || GetAncestor(underPointer, 2) != rootWindow ||
            (underPointer != childWindow && !IsChild(childWindow, underPointer) && !IsChild(underPointer, childWindow)))
        {
            error = "滚动区域被遮挡或鼠标已移到其他控件，自动滚动已停止。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private bool ValidateWindow(out string error)
    {
        if (disposed || rootWindow == IntPtr.Zero || childWindow == IntPtr.Zero ||
            !NativeCapture.IsWindow(rootWindow) || !NativeCapture.IsWindow(childWindow) ||
            GetAncestor(childWindow, 2) != rootWindow)
        {
            error = "所选滚动窗口已关闭或不再可用，请重新选择。";
            return false;
        }
        uint currentThread = GetWindowThreadProcessId(rootWindow, out uint currentProcess);
        if (currentThread != targetThread || currentProcess != targetProcess ||
            !IsWindowVisible(rootWindow) || !IsWindowVisible(childWindow) || NativeCapture.IsIconic(rootWindow))
        {
            error = "所选窗口已隐藏或发生变化，自动滚动已停止。";
            return false;
        }
        if (WindowRectangle(rootWindow) != initialBounds)
        {
            error = "所选窗口的位置或大小已改变，请重新框选滚动区域。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool ValidateInputState(out string error)
    {
        int[] keys = [0x01, 0x02, 0x04, 0x10, 0x11, 0x12, 0x5B, 0x5C];
        if (keys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0))
        {
            error = "检测到鼠标按键或修饰键按下，自动滚动已停止；松开按键后可重新开始。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private void FocusChild()
    {
        uint currentThread = GetCurrentThreadId();
        uint focusThread = GetWindowThreadProcessId(childWindow, out _);
        bool attached = currentThread != focusThread && AttachThreadInput(currentThread, focusThread, true);
        try
        {
            if (currentThread == focusThread || attached) SetFocus(childWindow);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, focusThread, false);
        }
    }

    private static IntPtr WindowAt(Point point)
    {
        var nativePoint = new NativeCapture.NativePoint { X = point.X, Y = point.Y };
        var found = WindowFromPoint(nativePoint);
        for (int depth = 0; depth < 32 && found != IntPtr.Zero; depth++)
        {
            var localPoint = nativePoint;
            if (!ScreenToClient(found, ref localPoint)) break;
            var child = ChildWindowFromPointEx(found, localPoint, 1 | 2 | 4);
            if (child == IntPtr.Zero || child == found) break;
            found = child;
        }
        return found;
    }

    private static Rectangle WindowRectangle(IntPtr window)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var bounds)) return Rectangle.Empty;
        return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (pointerPlaced && GetCursorPos(out var pointer) && pointer.X == scrollPoint.X && pointer.Y == scrollPoint.Y)
            SetCursorPos(originalPointer.X, originalPointer.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputData Data; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData { [FieldOffset(0)] public MouseInput Mouse; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int DeltaX, DeltaY;
        public uint MouseData, Flags, Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativeCapture.NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr window, NativeCapture.NativePoint point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeCapture.NativeRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr window, ref NativeCapture.NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr parameter, IntPtr extraParameter, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativeCapture.NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int positionX, int positionY);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachThreadInput(uint firstThread, uint secondThread, [MarshalAs(UnmanagedType.Bool)] bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
