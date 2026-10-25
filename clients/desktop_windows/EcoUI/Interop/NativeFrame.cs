using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EcoUI.Interop
{
    /// <summary>
    /// The minimum Win32/DWM surface EcoUI needs to make a frameless window feel
    /// native. Everything is best-effort: an unsupported Windows build simply keeps
    /// the WPF-drawn chrome.
    ///
    /// <para>Window <i>behaviour</i> (drag, snap, double-click maximise, resize,
    /// maximise animation) is not implemented here - it comes from
    /// <see cref="System.Windows.Shell.WindowChrome"/>, which routes hit-testing
    /// through the real non-client code paths. This class only adjusts the frame's
    /// appearance.</para>
    /// </summary>
    internal static class NativeFrame
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;
        private const int DwmWindowCornerDoNotRound = 1;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// Applies dark/light frame attributes so the window border, the resize frame
        /// and the Windows 11 corner rounding match the active theme.
        /// </summary>
        internal static void Apply(Window window, bool dark)
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

            TrySet(handle, DwmwaUseImmersiveDarkMode, dark ? 1 : 0);
            TrySet(handle, DwmwaUseImmersiveDarkModeBefore20H1, dark ? 1 : 0);
            TrySet(handle, DwmwaWindowCornerPreference, DwmWindowCornerRound);
        }

        /// <summary>Removes rounding (used when a window goes full screen / maximised).</summary>
        internal static void SetRounded(Window window, bool rounded)
        {
            IntPtr handle = window is null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            TrySet(handle, DwmwaWindowCornerPreference, rounded ? DwmWindowCornerRound : DwmWindowCornerDoNotRound);
        }

        private static void TrySet(IntPtr handle, int attribute, int value)
        {
            try
            {
                DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            }
            catch (DllNotFoundException)
            {
                // Pre-Vista shell. Nothing to do.
            }
            catch (EntryPointNotFoundException)
            {
                // Attribute introduced after this Windows build: purely cosmetic.
            }
        }
    }
}
