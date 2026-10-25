using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace EcosystemClient
{
    /// <summary>
    /// Which code path performed the most recent transform.
    /// </summary>
    public enum CryptoBackend
    {
        /// <summary>No usable native library was found; the managed fallback runs.</summary>
        Managed = 0,

        /// <summary>The C/ASM shared library (<c>asm_crypto.dll</c>) serviced the call.</summary>
        Native = 1
    }

    /// <summary>
    /// Immutable snapshot of the crypto subsystem counters. Read with
    /// <see cref="NativeCrypto.GetStatistics"/>; all values are monotonic except
    /// <see cref="LastError"/>, which describes the most recent interop failure.
    /// </summary>
    public readonly record struct CryptoStatistics(
        bool NativeAvailable,
        long NativeOperations,
        long ManagedOperations,
        long BytesTransformed,
        string LibraryVersion,
        string LibraryPath,
        string LastError)
    {
        public long TotalOperations => NativeOperations + ManagedOperations;
    }

    /// <summary>
    /// Safe, allocation-free managed wrapper around the C/ASM core library.
    ///
    /// <para><b>Contract of <c>asm_crypto_xor</c></b> (mirrored exactly by the
    /// managed fallback): every destination byte equals
    /// <c>source[i] XOR key_byte(i % 8)</c>, where <c>key_byte(k)</c> is the
    /// <c>k</c>-th least-significant byte of the 64-bit key. The transform is
    /// reversible and supports in-place operation (<c>source == destination</c>).</para>
    ///
    /// <para><b>Memory safety.</b> Every call validates span lengths before a
    /// pointer is formed, pins the managed buffers with <c>fixed</c> for the
    /// duration of the call only (never stores a pointer), passes <c>size_t</c>
    /// as <see cref="UIntPtr"/>, and never lets an interop failure escape: a
    /// missing/incompatible DLL degrades to the managed implementation instead of
    /// throwing into the UI thread. Access violations inside a buggy native
    /// library are not catchable by design - which is why the length invariants
    /// are enforced here, on the managed side, first.</para>
    ///
    /// <para><b>Library resolution.</b> A <see cref="NativeLibrary"/> resolver is
    /// installed for this assembly so the DLL is only ever loaded from explicit,
    /// predictable locations (application directory, <c>native/</c>,
    /// <c>runtimes/win-x64/native/</c>, or <c>ECOSYSTEM_CRYPTO_LIBRARY</c>).
    /// The current working directory is deliberately not searched - that is the
    /// classic DLL-planting vector.</para>
    /// </summary>
    public static class NativeCrypto
    {
        /// <summary>Logical library name used by the <see cref="DllImportAttribute"/>s below.</summary>
        public const string ImportName = "asm_crypto";

        /// <summary>Environment variable that can point at an explicit library file.</summary>
        public const string LibraryOverrideVariable = "ECOSYSTEM_CRYPTO_LIBRARY";

        /// <summary>
        /// Default 64-bit transform key. Must match the gateway's
        /// <c>ASM_CRYPTO_KEY</c> (default <c>0x0123456789ABCDEF</c>), otherwise
        /// inbound frames cannot be decoded.
        /// </summary>
        public const ulong DefaultKey = 0x0123456789ABCDEFUL;

        private const string VersionEntryPoint = "asm_crypto_version";
        private const string XorEntryPoint = "asm_crypto_xor";
        private const string XorInPlaceEntryPoint = "asm_crypto_xor_in_place";

        private static readonly string NativeFileName = ResolveNativeFileName();

        private static readonly Lazy<bool> Initialisation =
            new Lazy<bool>(InitialiseCore, LazyThreadSafetyMode.ExecutionAndPublication);

        private static int _nativeDisabled;
        private static long _nativeOperations;
        private static long _managedOperations;
        private static long _bytesTransformed;

        private static string _libraryVersion = "unavailable";
        private static string _libraryPath = string.Empty;
        private static string _lastError = string.Empty;

        // ---------------------------------------------------------------------
        // Native imports
        // ---------------------------------------------------------------------

        /*
         * EntryPoint + ExactSpelling keep the symbol lookup byte-for-byte the
         * exported C name (no charset suffix probing, no stdcall name mangling).
         * CallingConvention.Cdecl matches the C ABI on every platform; on x64
         * Windows both conventions collapse to the same machine code.
         *
         * `size_t` is pointer-sized, hence UIntPtr. `uint64_t` maps to ulong and
         * byte pointers are the only unsafe surface.
         */
        [DllImport(ImportName, EntryPoint = VersionEntryPoint, ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr AsmCryptoVersion();

        [DllImport(ImportName, EntryPoint = XorEntryPoint, ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe UIntPtr AsmCryptoXor(byte* src, byte* dst, UIntPtr length, ulong key);

        [DllImport(ImportName, EntryPoint = XorInPlaceEntryPoint, ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void AsmCryptoXorInPlace(byte* buffer, UIntPtr length, ulong key);

        // ---------------------------------------------------------------------
        // Public surface
        // ---------------------------------------------------------------------

        /// <summary>True when the native C/ASM library was located and answered <c>asm_crypto_version</c>.</summary>
        public static bool IsAvailable => Initialisation.Value;

        /// <summary>
        /// True while the hot path may call into the native library. A recoverable
        /// interop failure (unexpected return value, loader hiccup) disables it for the
        /// rest of the session instead of throwing on every frame.
        /// </summary>
        private static bool NativeReady => IsAvailable && Volatile.Read(ref _nativeDisabled) == 0;

        /// <summary>Semantic version reported by the native library (or "unavailable").</summary>
        public static string LibraryVersion => _libraryVersion;

        /// <summary>Absolute path of the loaded library, or an empty string.</summary>
        public static string LibraryPath => _libraryPath;

        /// <summary>
        /// Forces library discovery to run now. Call this once from a background
        /// thread during startup so the first real frame never pays the cost of a
        /// loader round-trip on the UI thread.
        /// </summary>
        public static void WarmUp()
        {
            _ = IsAvailable;
        }

        /// <summary>Reads the interop counters. Cheap and lock-free.</summary>
        public static CryptoStatistics GetStatistics() => new CryptoStatistics(
            NativeAvailable: IsAvailable,
            NativeOperations: Interlocked.Read(ref _nativeOperations),
            ManagedOperations: Interlocked.Read(ref _managedOperations),
            BytesTransformed: Interlocked.Read(ref _bytesTransformed),
            LibraryVersion: _libraryVersion,
            LibraryPath: _libraryPath,
            LastError: _lastError);

        /// <summary>
        /// Transforms <paramref name="source"/> into <paramref name="destination"/>,
        /// using the native library when available and the managed fallback otherwise.
        /// </summary>
        /// <param name="source">Input bytes. May alias <paramref name="destination"/>.</param>
        /// <param name="destination">Output bytes; must be at least as long as <paramref name="source"/>.</param>
        /// <param name="key">Repeating 64-bit transform key.</param>
        /// <returns>The backend that actually executed the transform.</returns>
        /// <exception cref="ArgumentException">The destination span is too small.</exception>
        public static unsafe CryptoBackend Xor(ReadOnlySpan<byte> source, Span<byte> destination, ulong key)
        {
            if (source.Length > destination.Length)
            {
                throw new ArgumentException(
                    "Destination buffer is smaller than the source buffer.",
                    nameof(destination));
            }

            if (source.Length == 0)
            {
                // The C ABI returns 0 without touching memory; keep the same shape.
                return NativeReady ? CryptoBackend.Native : CryptoBackend.Managed;
            }

            CryptoBackend backend = CryptoBackend.Managed;

            if (NativeReady)
            {
                try
                {
                    fixed (byte* src = source)
                    fixed (byte* dst = destination)
                    {
                        UIntPtr written = AsmCryptoXor(src, dst, ToSizeT(source.Length), key);
                        if (written != ToSizeT(source.Length))
                        {
                            // A well-behaved core returns len. Anything else means we
                            // are talking to an unexpected binary: fall back, loudly.
                            throw new InvalidOperationException(
                                "asm_crypto_xor returned an unexpected byte count (" +
                                written.ToString() + " != " + source.Length.ToString(CultureInfo.InvariantCulture) + ").");
                        }
                    }

                    Interlocked.Increment(ref _nativeOperations);
                    backend = CryptoBackend.Native;
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    RecordFailure(ex);
                    backend = CryptoBackend.Managed;
                }
            }

            if (backend == CryptoBackend.Managed)
            {
                XorManaged(source, destination, key);
                Interlocked.Increment(ref _managedOperations);
            }

            Interlocked.Add(ref _bytesTransformed, source.Length);
            return backend;
        }

        /// <summary>
        /// In-place variant. Prefer this for receive buffers: it touches exactly one
        /// span and lets the core use its <c>asm_crypto_xor_in_place</c> entry point.
        /// </summary>
        public static unsafe CryptoBackend XorInPlace(Span<byte> buffer, ulong key)
        {
            if (buffer.Length == 0)
            {
                return NativeReady ? CryptoBackend.Native : CryptoBackend.Managed;
            }

            if (NativeReady)
            {
                try
                {
                    fixed (byte* data = buffer)
                    {
                        AsmCryptoXorInPlace(data, ToSizeT(buffer.Length), key);
                    }

                    Interlocked.Increment(ref _nativeOperations);
                    Interlocked.Add(ref _bytesTransformed, buffer.Length);
                    return CryptoBackend.Native;
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    RecordFailure(ex);
                }
            }

            XorManaged(buffer, buffer, key);
            Interlocked.Increment(ref _managedOperations);
            Interlocked.Add(ref _bytesTransformed, buffer.Length);
            return CryptoBackend.Managed;
        }

        /// <summary>
        /// Bit-compatible managed implementation of <c>asm_crypto_xor</c>. Public so
        /// the crypto page can prove parity between the native core and C# at runtime.
        /// </summary>
        public static void XorManaged(ReadOnlySpan<byte> source, Span<byte> destination, ulong key)
        {
            if (source.Length > destination.Length)
            {
                throw new ArgumentException(
                    "Destination buffer is smaller than the source buffer.",
                    nameof(destination));
            }

            for (int i = 0; i < source.Length; i++)
            {
                // Byte phase is the least-significant byte first, matching the C core
                // on little-endian (x86-64) hosts and staying endian-independent.
                byte mask = (byte)(key >> (8 * (i & 7)));
                destination[i] = (byte)(source[i] ^ mask);
            }
        }

        // ---------------------------------------------------------------------
        // Internals
        // ---------------------------------------------------------------------

        private static unsafe bool InitialiseCore()
        {
            InstallResolver();

            // Declared outside the guarded block: stackalloc inside a try is legal,
            // but hoisting keeps the scope unambiguous for every compiler version.
            Span<byte> probeSource = stackalloc byte[8] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
            Span<byte> probeResult = stackalloc byte[8];
            Span<byte> expected = stackalloc byte[8];

            try
            {
                IntPtr versionPointer = AsmCryptoVersion();
                _libraryVersion = versionPointer == IntPtr.Zero
                    ? "unknown"
                    : Marshal.PtrToStringUTF8(versionPointer) ?? "unknown";

                // One tiny round-trip validates the whole ABI before the hot path
                // ever runs: symbol present, convention correct, buffers intact.
                fixed (byte* src = probeSource)
                fixed (byte* dst = probeResult)
                {
                    UIntPtr written = AsmCryptoXor(src, dst, ToSizeT(probeSource.Length), DefaultKey);
                    if (written != ToSizeT(probeSource.Length))
                    {
                        _lastError = "self-test: asm_crypto_xor returned an unexpected length";
                        return false;
                    }
                }

                // Parity self-test: the native core and the managed fallback must
                // produce identical bytes, otherwise an unknown binary is on disk.
                XorManaged(probeSource, expected, DefaultKey);
                if (!expected.SequenceEqual(probeResult))
                {
                    _lastError = "self-test: native output does not match the reference implementation";
                    return false;
                }

                _lastError = string.Empty;
                return true;
            }
            catch (Exception ex) when (IsLoadFailure(ex) || IsRecoverable(ex))
            {
                RecordFailure(ex);
                return false;
            }
        }

        private static void InstallResolver()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(NativeCrypto).Assembly, ResolveLibrary);
            }
            catch (InvalidOperationException)
            {
                // A resolver is already installed for this assembly (hot reload,
                // repeated initialisation). The existing one does the same job.
            }
        }

        private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, ImportName, StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            foreach (string candidate in EnumerateCandidates())
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
                {
                    _libraryPath = candidate;
                    return handle;
                }
            }

            // Not found in our own locations: let the default probing policy try
            // (application directory and OS paths only - never the CWD).
            return IntPtr.Zero;
        }

        private static IEnumerable<string> EnumerateCandidates()
        {
            string? overridePath = Environment.GetEnvironmentVariable(LibraryOverrideVariable);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                yield return overridePath!;
            }

            string baseDirectory = AppContext.BaseDirectory;

            yield return Path.Combine(baseDirectory, NativeFileName);
            yield return Path.Combine(baseDirectory, "native", NativeFileName);
            yield return Path.Combine(baseDirectory, "runtimes", "win-x64", "native", NativeFileName);
            yield return Path.Combine(baseDirectory, "runtimes", "linux-x64", "native", NativeFileName);
        }

        private static string ResolveNativeFileName()
        {
            if (OperatingSystem.IsWindows())
            {
                return "asm_crypto.dll";
            }

            if (OperatingSystem.IsMacOS())
            {
                return "libasm_crypto.dylib";
            }

            return "libasm_crypto.so";
        }

        private static UIntPtr ToSizeT(int length) => new UIntPtr((ulong)length);

        private static bool IsLoadFailure(Exception ex) =>
            ex is DllNotFoundException ||
            ex is EntryPointNotFoundException ||
            ex is BadImageFormatException ||
            ex is MarshalDirectiveException;

        private static bool IsRecoverable(Exception ex) =>
            IsLoadFailure(ex) ||
            ex is InvalidOperationException ||
            ex is NotSupportedException;

        private static void RecordFailure(Exception ex)
        {
            _lastError = ex.GetType().Name + ": " + ex.Message;

            // One strike is enough: a library that produced a bad result (or vanished)
            // must never be trusted again in this process. The managed fallback is
            // bit-compatible, so this is a performance decision, not a correctness one.
            Interlocked.Exchange(ref _nativeDisabled, 1);
            _libraryVersion = "unavailable";

            Debug.WriteLine("[NativeCrypto] native path disabled: " + _lastError);
        }
    }
}
