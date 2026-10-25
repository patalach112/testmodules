# testmodules

Self-hosted, low-level backend for a multiplatform ecosystem. The first
milestone is an asynchronous WebSocket gateway running on Debian 13 behind a
Cloudflare Tunnel, backed by a C/assembly core library.

## Architecture

```
client (Windows desktop / iOS / Android)
        │  WSS (HTTPS + WebSocket, TLS at the edge)
        ▼
Cloudflare edge ── outbound tunnel ──► cloudflared (container, no inbound ports)
                                          │  plain HTTP/WS on the Docker network
                                          ▼
                                     cpp_gateway (C++20, Boost.Beast/Asio)
                                          │  in-memory payload processing
                                          ▼
                                     libasm_crypto.so (C11 + x86-64 inline ASM)
```

No raw TCP/UDP is exposed: `cloudflared` dials out to the Cloudflare edge, and
the gateway publishes no host ports.

## Repository layout

```
.
├── Dockerfile                 multi-stage Debian 13 (trixie) image
├── docker-compose.yml         gateway service + optional cloudflared profile
├── Makefile                   repository-wide build/test entry points
├── scripts/
│   └── build-windows.sh       MinGW64/MSYS2 convenience wrapper
├── clients/
│   └── desktop_windows/     Windows desktop client (C# / WPF, net8.0-windows)
│       ├── EcosystemClient.csproj
│       ├── NativeCrypto.cs  P/Invoke wrapper around asm_crypto_xor
│       ├── NetworkEngine.cs ClientWebSocket supervisor (auto-reconnect)
│       ├── App.xaml         custom control templates / design system
│       ├── MainWindow.xaml  borderless shell: title bar, nav, dashboard
│       ├── Makefile         MSYS2 build: native DLL + app + staging
│       └── build.ps1        PowerShell build (same steps, no make)
└── server/
    ├── c_asm_crypto/          C11 core library with x86-64 inline assembly
    │   ├── include/asm_crypto.h
    │   ├── src/asm_crypto.c
    │   └── Makefile           builds libasm_crypto.so (versioned, SONAME .so.1)
    └── cpp_gateway/           C++20 WebSocket gateway
        ├── include/gateway/   config, logger, session, listener
        ├── src/               implementation + main()
        ├── Makefile           Linux: links libasm_crypto.so, RUNPATH $ORIGIN
        └── Makefile.win       Windows: static object + ws2_32/mswsock
```

## Prerequisites

Local builds:

- GCC/G++ 12 or newer (C11 and C++20)
- GNU Make
- Boost development headers (`apt install libboost-dev` on Debian; Beast and
  Asio are header-only, so no Boost libraries are linked)

Container builds: Docker with the Compose plugin.

## Local build

```sh
# Native build of both components (-march=native)
make build MARCH=

# Targeted build for the Intel i5-3470 (Ivy Bridge) — the Makefile default
make build

# Unit tests for the C/ASM core (all lengths and buffer alignments,
# in-place and out-of-place, compared against an independent reference)
make -C server/c_asm_crypto check
make test MARCH=
```

Artifacts:

- `server/c_asm_crypto/build/libasm_crypto.so.1.0.0` (with `.so.1` / `.so` symlinks)
- `server/cpp_gateway/build/gateway` (the shared library is staged next to it)

## Running locally

```sh
GATEWAY_PORT=8080 ASM_CRYPTO_KEY=0x0123456789ABCDEF \
    ./server/cpp_gateway/build/gateway
```

The gateway accepts WebSocket upgrades and echoes each frame back after
running it through the `asm_crypto` XOR transform. Quick check with
[`wscat`](https://github.com/websockets/wscat):

```sh
wscat --connect ws://127.0.0.1:8080/
Connected (press CTRL+C to quit)
> hello
< (XOR-transformed bytes)
```

### Environment variables

| Variable          | Default               | Description                                            |
|-------------------|-----------------------|--------------------------------------------------------|
| `GATEWAY_BIND`    | `0.0.0.0`             | Listen address (container network only)                |
| `GATEWAY_PORT`    | `8080`                | Listen port                                            |
| `GATEWAY_THREADS` | `0` (auto)            | I/O worker threads; `0` derives from the CPU count     |
| `ASM_CRYPTO_KEY`  | `0x0123456789ABCDEF`  | 64-bit transform key, decimal or `0x`-prefixed hex     |

Malformed values make the process fail fast with an explanatory `fatal:`
message.

## Native Windows build (MinGW64 / MSYS2)

The same sources compile to a native `gateway.exe` inside an **MINGW64**
terminal. Install the toolchain once:

```sh
pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-boost make
```

Then build from the repository root or from `server/cpp_gateway`:

```sh
# either, via the wrapper script
./scripts/build-windows.sh

# or directly — MSYS2 exports OS=Windows_NT, so Makefile includes
# Makefile.win automatically
cd server/cpp_gateway
make clean && make
```

Output: `server/cpp_gateway/build/gateway.exe`.

Platform differences from the Linux build, all isolated in `Makefile.win`:

- The C/ASM core is compiled to a COFF object
  (`../c_asm_crypto/build/asm_crypto.o`) and linked **directly into the
  executable** — there is no Windows `.so`/DLL step.
- The link line adds the native Winsock import libraries
  `-lws2_32 -lmswsock` (the latter provides `AcceptEx`, which Asio uses for
  overlapped accepts).
- Callbacks use C++20 `std::bind_front`; time conversion and console signals
  are selected with `#ifdef _WIN32` (`gmtime_s` / `SIGBREAK` versus
  `gmtime_r` / `SIGTERM`).

Run it the same way, from the MINGW64 shell:

```sh
GATEWAY_PORT=8080 ASM_CRYPTO_KEY=0x0123456789ABCDEF ./build/gateway.exe
```

## Windows desktop client (C# / WPF)

`clients/desktop_windows` is the native Windows client: a borderless,
custom-chromed dashboard (dark cyberpunk theme) that speaks WSS to the gateway,
decodes inbound frames with the same C/ASM core, and shows live latency,
throughput, packet and crypto counters plus a wire-level terminal.

```powershell
cd clients\desktop_windows

# PowerShell: builds asm_crypto.dll from ../../server/c_asm_crypto, then the app,
# and stages the DLL next to the executable
powershell -ExecutionPolicy Bypass -File .\build.ps1

# or in an MSYS2 / MINGW64 shell
make            # native DLL + WPF app
make run        # launch
make publish    # bin/Release/net8.0-windows/win-x64/publish
```

Requirements: Windows 10 1809+ and the .NET 8 SDK; MinGW-w64 gcc is optional
(without it the client runs on a bit-identical managed fallback and says so in
the UI). The native library is resolved from the application directory,
`native/`, `runtimes/win-x64/native/` or `%ECOSYSTEM_CRYPTO_LIBRARY%` — never
from the current working directory.

Protocol note: the gateway XORs every echoed frame, so the client sends plaintext
and decodes inbound frames with the shared key. `ASM_CRYPTO_KEY` must therefore
match on both sides. See `clients/desktop_windows/README.md` for the architecture,
threading model, latency-probe frame layout and performance modes.

## Docker

```sh
# Build the image (pinned to the Ivy Bridge ISA of the i5-3470)
docker build -t testmodules/gateway:dev .

# Run the gateway alone; it is reachable only on the compose network
docker compose up -d --build

# Include the Cloudflare Tunnel sidecar
cp .env.example .env   # then set CLOUDFLARE_TUNNEL_TOKEN
docker compose --profile tunnel up -d
```

The runtime stage is `debian:trixie-slim`, runs as a non-root user with all
Linux capabilities dropped and a read-only root filesystem.

### Cloudflare Tunnel routing

Point the tunnel's public hostname at the compose service, for example in the
dashboard or in a config-ingress rule:

```yaml
ingress:
  - hostname: ws.example.com
    service: http://gateway:8080
  - service: http_status:404
```

Cloudflare terminates TLS and forwards WSS connections as plain WebSocket
connections to the gateway.

## Portability of the C/ASM core

`asm_crypto.c` selects the x86-64 inline-assembly fast path only when both
the architecture and GNU extended-asm support are present
(`__x86_64__ && __GNUC__`, i.e. GCC/Clang/MinGW-w64). Otherwise a pure-C
path is used, so the file also compiles on MSVC x64, 32-bit x86 and ARM.
The assembly routine takes byte pointers, so callers need no alignment and
the code performs no strict-aliasing-violating `uint64_t *` accesses; the
repeating key is applied by byte phase on every path, making the result
endian-independent.

## Target CPU note

`-march=native` is used for local builds only. Container builds pass
`-march=ivybridge -mtune=ivybridge` (the microarchitecture of the Intel
i5-3470) so the image is not accidentally tuned for the build machine.
Override with the Docker build arg `--build-arg GCC_ARCH=...`.

## Security note

The assembly routine is a repeating-key XOR transform: a fast, reversible
in-memory primitive, **not** encryption. It must not be used for
confidentiality. Transport security is provided by TLS at the Cloudflare
edge; any future cryptographic workload should use a vetted library.
