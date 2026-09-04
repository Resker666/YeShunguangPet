using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace YeShunguangPet;

internal static class NativeMethods
{
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const long WsExTransparent = 0x00000020L;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static void SetClickThrough(Window window, bool enabled)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style = enabled
            ? style | WsExLayered | WsExTransparent
            : style & ~WsExTransparent;

        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
    }

    public static void ActivateWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    public static bool EnsureWindowInWorkArea(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromRect(ref windowRect, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        var targetLeft = ClampOrigin(
            windowRect.Left,
            monitorInfo.WorkArea.Left,
            monitorInfo.WorkArea.Right,
            width);
        var targetTop = ClampOrigin(
            windowRect.Top,
            monitorInfo.WorkArea.Top,
            monitorInfo.WorkArea.Bottom,
            height);

        if (targetLeft == windowRect.Left && targetTop == windowRect.Top)
        {
            return false;
        }

        return SetWindowPos(
            hwnd,
            IntPtr.Zero,
            targetLeft,
            targetTop,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static int ClampOrigin(int value, int start, int end, int size)
    {
        var lastValidOrigin = end - size;
        return lastValidOrigin <= start
            ? start
            : Math.Clamp(value, start, lastValidOrigin);
    }

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : new IntPtr(GetWindowLong32(hwnd, index));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
