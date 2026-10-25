using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EcosystemClient.Interop
{
    /// <summary>
    /// The small amount of Win32 the shell needs.
    ///
    /// <para><b>Why this exists:</b> a <c>WindowStyle="None"</c> +
    /// <c>AllowsTransparency="True</c> window is a layered window, and Windows
    /// maximises those across the whole monitor - including the taskbar. Handling
    /// <c>WM_GETMINMAXINFO</c> and clamping to the monitor's work area is the only
    /// reliable fix. The DWM calls are best-effort cosmetic extras (dark frame,
    /// rounded corners, Mica backdrop when the window is opaque).</para>
    ///
    /// <para>Every call is guarded: this file must never throw on an unsupported
    /// Windows build, and it is only reached on Windows.</para>
    /// </summary>
    internal static class NativeWindow
    {
        private const int WmGetMinMaxInfo = 0x0024;
        private const int MonitorDefaultToNearest = 0x00000002;

        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeLegacy = 19;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaSystemBackdropType = 38;
        private const int DwmcpRound = 2;
        private const int DwmsbtMainWindow = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point32
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public Point32 Reserved;
            public Point32 MaxSize;
            public Point32 MaxPosition;
            public Point32 MinTrackSize;
            public Point32 MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect32 Monitor;
            public Rect32 Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// Attaches the maximise fix and the cosmetic DWM attributes to a realised window.
        /// Safe to call more than once.
        /// </summary>
        internal static void Attach(Window window, bool opaque)
        {
            if (window is null)
            {
                return;
            }

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            HwndSource? source = HwndSource.FromHwnd(handle);
            if (source is not null)
            {
                source.RemoveHook(OnWindowMessage); // idempotent
                source.AddHook(OnWindowMessage);
            }

            TrySetDarkMode(handle);
            TrySetCornerPreference(handle);

            if (opaque)
            {
                TrySetMicaBackdrop(handle);
            }
        }

        private static IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                if (monitor == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info))
                {
                    return IntPtr.Zero;
                }

                MinMaxInfo limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);

                // ptMaxPosition is relative to the monitor's top-left corner, so the
                // work-area offset is what keeps the title bar under the taskbar.
                limits.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                limits.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                limits.MaxSize.X = info.Work.Right - info.Work.Left;
                limits.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                limits.MaxTrackSize.X = info.Monitor.Right - info.Monitor.Left;
                limits.MaxTrackSize.Y = info.Monitor.Bottom - info.Monitor.Top;

                Marshal.StructureToPtr(limits, lParam, true);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is MarshalDirectiveException)
            {
                // Never let cosmetics break window chrome.
            }

            // Let the default processing continue with the values we just wrote.
            handled = false;
            return IntPtr.Zero;
        }

        private static void TrySetDarkMode(IntPtr handle)
        {
            int enabled = 1;
            TryDwm(handle, DwmwaUseImmersiveDarkMode, enabled);
            TryDwm(handle, DwmwaUseImmersiveDarkModeLegacy, enabled);
        }

        private static void TrySetCornerPreference(IntPtr handle) =>
            TryDwm(handle, DwmwaWindowCornerPreference, DwmcpRound);

        private static void TrySetMicaBackdrop(IntPtr handle) =>
            TryDwm(handle, DwmwaSystemBackdropType, DwmsbtMainWindow);

        private static void TryDwm(IntPtr handle, int attribute, int value)
        {
            try
            {
                DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            }
            catch (DllNotFoundException)
            {
                // Pre-Vista layout. Nothing to do.
            }
            catch (EntryPointNotFoundException)
            {
                // Attribute not supported by this build; it is purely cosmetic.
            }
        }
    }
}
