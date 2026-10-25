# testmodules

Self-hosted, low-level backend for a multiplatform ecosystem. The first
milestone is an asynchronous WebSocket gateway running on Debian 13 behind a
Cloudflare Tunnel, backed by a C/assembly core library.

## Architecture

```
client (Windows / iOS / Android)
        │  WSS (HTTPS + WebSocket, TLS at the edge)
        ▼
Cloudflare edge ── outbound tunnel ──► cloudflared (container, no inbound ports)
                                          │  plain HTTP/WS on the Docker network
                                          ▼
                                     cpp_gateway (C++20, Boost.Beast/Asio)
                                          │  BPP framing (shared/binary_packer)
                                          │  in-memory payload processing
                                          ▼
                     libbinary_packer.a (CRC32-C, SSE4.2) + libasm_crypto.so
```

No raw TCP/UDP is exposed: `cloudflared` dials out to the Cloudflare edge, and
the gateway publishes no host ports.

## Repository layout

```
.
├── Dockerfile                 multi-stage Debian 13 (trixie) image
├── docker-compose.yml         gateway service + optional cloudflared profile
├── Makefile                   repository-wide build/test entry points
├── client/
│   └── ws_client/             native WebSocket test client (Linux + Windows)
│       ├── include/wsclient/  reusable synchronous Client class
│       ├── src/               implementation + CLI
│       ├── Makefile           Linux: .so + $ORIGIN runpath
│       └── Makefile.win       Windows: static asm object + ws2_32
├── scripts/
│   └── build-windows.sh       MinGW64/MSYS2 convenience wrapper
├── shared/
│   └── binary_packer/         Binary Packet Protocol (C ABI + C++ wrapper)
│       ├── include/binary_packer/  binary_pack.h, packet.hpp, opcodes.h
│       ├── src/               CRC32-C SSE4.2/soft, endian, pack/parse
│       ├── tests/             reference vectors, framing, corruption
│       ├── Makefile           Linux: static archive + `make check`
│       └── Makefile.win       Windows: COFF objects + `make check`
└── server/
    ├── c_asm_crypto/          C11 core library with x86-64 inline assembly
    │   ├── include/asm_crypto.h
    │   ├── src/asm_crypto.c
    │   ├── tests/             reference-compared unit tests
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

# Unit tests for both C cores (CRC vectors, framing, corruption cases,
# XOR round trips across every length and buffer alignment)
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
running it through the `asm_crypto` XOR transform. The transformed output is
arbitrary bytes rather than valid UTF-8, so replies always use the binary
opcode (RFC 6455 text frames require valid UTF-8).

## Binary Packet Protocol (`shared/binary_packer`)

Application traffic uses a compact binary wire format instead of JSON. Every
packet is a fixed 12-byte header, the payload, and a trailing checksum; all
multi-byte integers are network byte order (big endian):

| offset | size | field                                  |
|--------|------|----------------------------------------|
| 0      | 2    | magic `0x50 0x4B` (`'P' 'K'`)          |
| 2      | 1    | opcode / packet type                   |
| 3      | 1    | flags                                  |
| 4      | 4    | sequence number, big endian            |
| 8      | 4    | payload length, big endian             |
| 12     | N    | payload (may be empty)                 |
| 12+N   | 4    | CRC32-C over header + payload, big endian |

The checksum is **CRC-32C (Castagnoli)**, the variant of the SSE4.2 `crc32`
instruction. The implementation probes CPUID once and dispatches at runtime:
hardware `crc32q/crc32l/crc32b` when SSE4.2 is present, a portable bitwise
routine otherwise (also used on non-x86). A `-DBP_CRC_HAVE_X86=0` override
forces the software path for testing.

The C core never allocates and never casts wire bytes to structs, so buffers
of any alignment are safe. The C++ wrapper is zero-allocation:
`PacketBuilder` writes into caller-provided storage, and `PacketParser`
returns a `std::span` view into the receive buffer. The gateway transforms
the payload directly into the builder's reserved region, so the data path
incurs no extra copy beyond the transform itself.

```sh
# Unit tests: CRC reference vectors ("123456789" -> 0xE3069283), hardware/
# software agreement, framing round trips, every corruption mode.
make -C shared/binary_packer check
```

Opcodes live in `binary_packer/opcodes.h`: `BP_OP_ECHO` (transformed echo),
`BP_OP_ACK`, and `BP_OP_ERROR` (the gateway answers malformed packets with an
ERROR frame whose one-byte payload is the parser's status code).

The client speaks the protocol with `--packet`; `--roundtrip` then validates
the checksum, the echoed sequence number and the XOR round trip:

```sh
./client/ws_client/build/ws_client --packet --roundtrip \
    --count 100 ws://127.0.0.1:8080/ "hello bpp"
# frame 100: PASS (seq 100, 9 bytes transformed and restored, CRC32-C valid)
```

## Native WebSocket client module

`client/ws_client` is a dependency-free (Boost headers only) command-line
client and a reusable `wsclient::Client` class. It links the same
`asm_crypto` core as the gateway, so it can verify the whole data path
without any external tooling:

```sh
make -C client/ws_client

# one frame, prints the transformed reply as hex
./client/ws_client/build/ws_client ws://127.0.0.1:8080/ hello

# end-to-end check: reverses the XOR on the reply and compares it to
# the original payload; exits non-zero on any mismatch
./client/ws_client/build/ws_client --roundtrip ws://127.0.0.1:8080/ hello

# 100 frames over one connection, key given as decimal
./client/ws_client/build/ws_client --roundtrip --count 100 \
    --key 81985529216486895 ws://127.0.0.1:8080/

# pipe a payload through stdin
cat payload.bin | ./client/ws_client/build/ws_client --binary ws://127.0.0.1:8080/ -
```

Exit codes are scriptable: `0` success, `1` connection/verification failure,
`2` usage error. Use `--help` for the full option list.

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

Then build from the repository root or from a component directory —
MSYS2 exports `OS=Windows_NT`, so every `Makefile` includes its
`Makefile.win` automatically:

```sh
# gateway only, via the wrapper script
./scripts/build-windows.sh

# or build everything (gateway + ws_client) from the repository root
make clean && make
```

Outputs:

- `server/cpp_gateway/build/gateway.exe`
- `client/ws_client/build/ws_client.exe`

Test them together in the MINGW64 shell:

```sh
GATEWAY_PORT=8080 ./server/cpp_gateway/build/gateway.exe &
./client/ws_client/build/ws_client.exe --packet --roundtrip \
    ws://127.0.0.1:8080/ "windows native check"
```

The packer's own suite runs in MINGW64 too:
`make -C shared/binary_packer -f Makefile.win check`.

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
