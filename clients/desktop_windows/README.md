# Ecosystem Client — Windows desktop (WPF, .NET 8)

The native Windows client for the testmodules ecosystem: a borderless,
custom-chromed dashboard that talks WSS to the C++ gateway and runs every inbound
frame through the same C/ASM core (`asm_crypto_xor`) the server uses.

```
   ┌──────────────────────────────────────────────────────────────────────┐
   │  ◆ ECOSYSTEM        ● ONLINE  ws://127.0.0.1:8080/        21:04:11  ─ □ ✕ │
   ├───────────────┬──────────────────────────────────────────────────────┤
   │ NAVIGATION    │  Command Deck                                        │
   │ ▎Command Deck │  ┌──────────┬──────────┬──────────┬──────────┐       │
   │  Wire Term.   │  │ LATENCY  │ PACKETS  │ THROUGHPUT│ CRYPTO  │       │
   │  Crypto Core  │  │  12.4 ms │ 96 pkt/s │ 41 KB/s  │ 96 op/s │       │
   │  Settings     │  └──────────┴──────────┴──────────┴──────────┘       │
   │               │  ┌──────────────────────────┬────────────────┐       │
   │ CRYPTO CORE   │  │ throughput sparkline     │ latency trace  │       │
   │ native v1.0.0 │  ├──────────────────────────┴────────────────┤       │
   │ 1 284 ops     │  │ recent activity      │  session telemetry │       │
   ├───────────────┴──────────────────────────────────────────────────────┤
   │ ● ONLINE · ws://127.0.0.1:8080/          v1.0.0 · net8.0-windows     │
   └──────────────────────────────────────────────────────────────────────┘
```

## What is in the box

| Area | Implementation |
|------|----------------|
| Custom chrome | `WindowStyle="None"` + `AllowsTransparency="True"`, hand-built title bar, drag/double-click/snap, `WindowChrome` resize border, `WM_GETMINMAXINFO` fix so maximise respects the taskbar |
| Design system | ~900 lines of `App.xaml`: colour ramp, gradients, glows, `ControlTemplate`s for buttons/nav/inputs/toggles/scrollbars/list rows, animated hover and selection states |
| Dashboard | Four KPI cards with arc gauges, throughput + latency sparklines rendered by custom `FrameworkElement`s, live activity feed, session panel |
| Terminal | Virtualised, filtered, colour-coded wire log with follow-tail, pause, copy and a command line that sends real frames |
| Crypto page | Transform playground, native/managed parity self-test, 4 MiB benchmark with speedup |
| Settings | Endpoint, key, probes, buffer size, health timeout, opaque-window (GPU) mode, persisted JSON |
| Transport | `NetworkEngine`: `ClientWebSocket` supervisor with auto-reconnect, backoff+jitter, outbound `Channel`, `ArrayPool` buffers, health watchdog, latency probes |

## Requirements

* Windows 10 1809 or newer (Windows 11 recommended for Mica/rounded DWM corners)
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (runtime alone is not enough to build)
* Optional: MinGW-w64 gcc to build `asm_crypto.dll` — any of
  * MSYS2: `pacman -S --needed mingw-w64-x86_64-gcc` (also gives you `make`)
  * or any `gcc.exe` on `PATH` (WinLibs, MSYS2/UCRT64, …)

Without gcc the app still builds and runs; it reports
`native crypto core not found - running the managed fallback` and uses a
bit-identical C# implementation.

## Build

### PowerShell (no make required)

```powershell
cd clients\desktop_windows
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The script

1. locates `dotnet` and (optionally) `gcc`,
2. compiles `..\..\server\c_asm_crypto\src\asm_crypto.c` into `native\asm_crypto.dll`,
3. runs `dotnet build -c Release`,
4. copies the DLL next to `EcosystemClient.exe`.

Output: `clients\desktop_windows\bin\Release\net8.0-windows\EcosystemClient.exe`.

Options:

```powershell
.\build.ps1 -Configuration Debug          # debug build
.\build.ps1 -Publish                     # win-x64 publish folder
.\build.ps1 -SkipNative                  # managed fallback only
```

### MSYS2 / MINGW64 (make)

```sh
pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-dotnet-sdk make
cd clients/desktop_windows
make                # native DLL + app, DLL staged beside the exe
make run            # build and launch
make publish        # bin/Release/net8.0-windows/win-x64/publish
make verify         # asserts asm_crypto.dll is deployed
make clean
```

### Plain dotnet

```powershell
dotnet build EcosystemClient.csproj -c Release
dotnet run   --project EcosystemClient.csproj -c Release
```

`dotnet build` alone does **not** produce the native DLL: run `make native`,
`build.ps1`, or copy `asm_crypto.dll` into the output folder yourself.

## Native interop (`NativeCrypto.cs`)

```csharp
[DllImport("asm_crypto", EntryPoint = "asm_crypto_xor", ExactSpelling = true,
           CallingConvention = CallingConvention.Cdecl)]
private static extern unsafe UIntPtr AsmCryptoXor(byte* src, byte* dst, UIntPtr length, ulong key);
```

* `size_t` → `UIntPtr`, `uint64_t` → `ulong`, buffers → `byte*` pinned with `fixed`
  for the duration of the call only (no stored pointers, no `GCHandle` leaks).
* Length invariants are checked **before** any pointer is formed; the transform is
  length-delimited, so a caller can never walk past a buffer.
* A `NativeLibrary` resolver restricts loading to `AppContext.BaseDirectory`,
  `native/`, `runtimes/win-x64/native/` and `%ECOSYSTEM_CRYPTO_LIBRARY%`.
  The current working directory is never searched (DLL-planting defence).
* On load, a **self-test** transforms 8 bytes and compares the result with the
  managed reference implementation. A mismatched or incompatible binary is
  rejected and the managed path takes over instead of corrupting frames.
* Every interop failure (`DllNotFoundException`, `BadImageFormatException`, …) is
  contained in `NativeCrypto`: the UI thread never sees an exception, and the
  backend in use is shown in the sidebar, the status bar and the crypto page.
* 32-bit processes are refused the native path explicitly — the shipped DLL is x64.

`NativeCrypto.XorManaged` is a byte-for-byte port of the C core
(`dst[i] = src[i] ^ (key >> (8 * (i & 7)))`), which is what makes the runtime
parity self-test meaningful. That contract is verified in this repository on
Linux: `asm_crypto_xor` and the managed mirror agree across 3 592 length /
alignment / key combinations, including in-place transforms.

## Transport (`NetworkEngine.cs`)

**Threading.** `StartAsync` returns immediately; a supervisor loop runs on the
thread pool and owns four pumps per connection — receive, send, probe, health.
Every `await` uses `ConfigureAwait(false)`, so nothing can capture the WPF
dispatcher. The dispatcher only ever sees counter snapshots and log lines.

**Reconnect policy.** Any pump ending (server close, socket error, health
timeout, caller stop) cancels its siblings; the supervisor then waits
`min(20 s, 400 ms × 2^n)` ±25 % jitter and reconnects. A session that survives
20 s resets the ladder, so a long-lived connection never inherits a stale
20-second delay. `MaxReconnectAttempts = 0` means retry forever.

**Memory.** The receive buffer is rented once per connection; fragmented
messages grow by doubling and are returned to the pool the moment the handler
returns. Outbound frames carry pooled arrays with transfer-of-ownership
(`OutboundFrame`), disposed exactly once on the send, drop, or teardown path. The
outbound queue is bounded (2 048 frames); when it saturates, frames are dropped
and counted rather than growing the heap.

**Health.** One `ClientWebSocket` ping configuration plus an application-level
probe every 2 s. If no frame arrives for `HealthTimeout` (default 15 s) the
socket is torn down and reconnected — a half-open TCP connection can never leave
the UI showing "ONLINE" forever.

### Wire protocol

The gateway XORs every frame it echoes, so the client sends **plaintext** and
decodes **inbound** frames with the shared key:

```
client  --(plaintext frame)-->  gateway  --(frame XOR key)-->  client
                                                              XorInPlace()
```

Probe frames are 32-byte binary messages:

| bytes | meaning |
|-------|---------|
| 0–3 | magic `EC 05 01 00` |
| 4–7  | little-endian sequence number |
| 8–15 | little-endian `Stopwatch` timestamp at send |
| 16–31| random filler (so ciphertext never repeats) |

The echo carries the same bytes back; the round-trip time is measured from the
embedded timestamp, which makes the latency figure cover the whole path:
WebSocket write → gateway transform → WebSocket read → client decode.

## Performance notes

* **Two window modes.** The default uses `AllowsTransparency="True"` for rounded
  corners and a floating shadow — Windows composites layered windows on the CPU,
  which caps throughput. Set **Settings → Opaque window** (or
  `ECOSYSTEM_OPAQUE_WINDOW=1`) and restart for a normal hardware-accelerated
  window with Mica on Windows 11.
* Effects are deliberately rationed: one shadow layer behind the shell, glows only
  on small accents, and the ambient blooms are pre-built radial gradients whose
  opacity (not blur) animates. No `BlurEffect` anywhere on a hot path.
* Charts are `OnRender`-only `FrameworkElement`s with frozen geometries — no
  visual-tree churn per sample.
* Sampling runs at 4 Hz on the dispatcher from lock-free counters; the log store
  batches writes on a 120 ms timer so a frame storm cannot flood the UI.
* The terminal uses `VirtualizingStackPanel` with recycling; the log itself is
  capped at 2 500 lines.

## Keyboard

| Keys | Action |
|------|--------|
| `Ctrl+1..4` | Command Deck / Wire Terminal / Crypto Core / Settings |
| `Enter` in the terminal command line | send the frame |
| double-click title bar | maximise / restore |
| drag title bar | move (restores a maximised window first) |

## Configuration

`%APPDATA%\EcosystemClient\settings.json`

| Field | Default | Meaning |
|-------|---------|---------|
| `Endpoint` | `ws://127.0.0.1:8080/` | gateway URL (`wss://` behind Cloudflare) |
| `CryptoKey` | `0x0123456789ABCDEF` | must equal the gateway's `ASM_CRYPTO_KEY` |
| `AutoConnect` | `true` | connect at startup |
| `DecodeInbound` | `true` | XOR inbound frames with the key |
| `EnableLatencyProbes` | `true` | round-trip probes / watchdog traffic |
| `ReceiveBufferKb` | `32` | pooled receive buffer per connection |
| `HealthTimeoutSeconds` | `15` | dead-socket detection window |
| `PreferOpaqueWindow` | `false` | hardware-accelerated window (restart required) |

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `native crypto core not found` in the sidebar | `asm_crypto.dll` is not next to the exe — run `make native` or `build.ps1`, or set `ECOSYSTEM_CRYPTO_LIBRARY` |
| `BadImageFormatException` logged at startup | the DLL is not x64, or the process is 32-bit; rebuild with MinGW-w64 x86_64 (`make native`) |
| Frames arrive as gibberish | `CryptoKey` ≠ the gateway's `ASM_CRYPTO_KEY` |
| Reconnect loop with `handshake did not complete` | wrong port/scheme, or the tunnel is down; `wss://` requires a valid edge certificate |
| Window cannot be resized | `WindowChrome.ResizeBorderThickness` was overridden; keep `ResizeMode="CanResize"` |
| Values freeze while the window is minimised | intentional: WPF throttles rendering for occluded windows; reconnects and counters continue |

## Verification status

The WPF layer (`net8.0-windows`) cannot be compiled or launched on Linux, so this
directory was validated statically:

* every `.cs` file parses cleanly with the ROSLYN C# grammar (tree-sitter);
* every `.xaml`, `.csproj` and `.manifest` is well-formed XML;
* `StaticResource`/`DynamicResource` keys resolve, in declaration order;
* binding paths resolve against the real view-model property graph;
* `Click`/`MouseLeftButtonDown` handlers exist in the matching code-behind;
* `x:Name`s are unique, `ElementName` targets exist;
* every `Path` geometry is lexically valid;
* the C/ASM core builds with the exact flags in the `Makefile` and its
  native/managed parity was proven against the managed implementation.

Build and run it on Windows to exercise the UI itself.

## Layout

```
clients/desktop_windows/
├── EcosystemClient.csproj      net8.0-windows, WPF, x64, unsafe (interop)
├── app.manifest                PerMonitorV2 DPI, no elevation
├── App.xaml / App.xaml.cs      design system + composition root, exception guards
├── MainWindow.xaml / .cs       borderless shell, title bar, nav, status bar
├── NativeCrypto.cs             P/Invoke wrapper, resolver, self-test, fallback
├── NetworkEngine.cs            ClientWebSocket supervisor, pumps, counters
├── Core/                       settings, session, log store, MVVM primitives
├── ViewModels/                 shell, dashboard, terminal, crypto, settings
├── Views/                      the four pages (XAML + code-behind)
├── Controls/                   SparklineChart, RingGauge (custom rendering)
├── Converters/                 value converters, frozen palette
├── Interop/                    WM_GETMINMAXINFO / DWM helpers
├── Makefile                    MSYS2 build (native + app + staging)
└── build.ps1                   PowerShell build (native + app + staging)
```

## Security note

`asm_crypto_xor` is a reversible repeating-key XOR — an obfuscation primitive for
in-process data, **not** encryption. Confidentiality comes from TLS terminated at
the Cloudflare edge; the client never disables certificate validation and ships
no pinned keys or secrets.
