# Windows Telemetry & System Monitor Daemon (`win_telemetry`)

High-performance, ultra-lightweight Windows telemetry daemon written in **C11** and **x86_64 inline assembly**. Monitors low-level system statistics (CPU, RAM, Uptime, CPU clock cycles) and streams encrypted telemetry frames over WebSockets to the gateway (`server/cpp_gateway`).

## Features

- **Low-Level Hardware Stats (C11 + x86_64 ASM)**:
  - `RDTSC` preceded by serializing `CPUID` instruction in inline assembly for sub-nanosecond clock cycle tracking and measurement precision profiling.
  - Native Windows APIs (`GetSystemTimes`, `GlobalMemoryStatusEx`, `GetTickCount64`) for kernel/user CPU usage %, physical RAM utilization, and system uptime.
  - Self-monitoring via `GetProcessMemoryInfo` verifying minimal memory footprint.
- **Payload Encryption (`c_asm_crypto`)**:
  - Links directly against the repository's `c_asm_crypto` core object (`asm_crypto.o`).
  - XOR-transforms every outgoing telemetry frame using the configured 64-bit key (`0x0123456789ABCDEF`).
  - Verified end-to-end against `cpp_gateway`, where the gateway's identical transform automatically decrypts the frame and echoes it back.
- **Zero-Dependency RFC 6455 WebSocket Client**:
  - Pure Winsock 2 (`ws2_32`) implementation with HTTP 101 Upgrade and client-to-server 4-byte frame masking.
  - `TCP_NODELAY` enabled for immediate telemetry dispatch.
  - Automatic reconnect with backoff when the gateway is offline.
  - Zero heap allocations in the streaming loop: uses static bounded buffers.
  - Supports both **JSON** and packed **Binary** (`TELM`) frame formats.
- **Dual Execution Mode**:
  - **Interactive Daemon**: Runs in foreground console with graceful handling of `Ctrl+C`, `Ctrl+Break`, and window close (`SetConsoleCtrlHandler`).
  - **Windows Service**: Integrates with Windows Service Control Manager (SCM) via `--service`, with `--install` and `--uninstall` helper flags.
- **Ultra-Low Resource Footprint**:
  - Resident memory working set: **~0.1 MB RAM** (exceeding the `<5 MB` constraint).
  - Standalone binary size: **~280 KB**.

---

## Repository Structure

```
clients/win_telemetry/
├── include/
│   ├── config.h           # Command-line & environment configuration
│   ├── hw_stats.h         # RDTSC/CPUID ASM routines & Windows metrics
│   ├── protocol.h         # JSON formatting & binary telemetry definitions
│   ├── service.h          # Windows Service & console signal dispatching
│   └── ws_client.h        # Native Winsock RFC 6455 WebSocket client
├── src/
│   ├── config.c           # CLI arguments and environment variable parser
│   ├── hw_stats.c         # CPUID/RDTSC inline ASM, GetSystemTimes, Memory APIs
│   ├── main.c             # Daemon streaming loop & roundtrip verification
│   ├── protocol.c         # JSON and packed struct serialization & crypto invocation
│   ├── service.c          # Windows SCM & console control handlers
│   └── ws_client.c        # Winsock socket lifecycle, HTTP upgrade, frame masking
├── tests/
│   └── test_telemetry.c   # 26 unit & integration tests
├── build.ps1              # PowerShell build and test script (GCC & MSVC)
├── Makefile               # Top-level platform dispatcher
├── Makefile.win           # GNU Make / MinGW-w64 build rules
└── README.md
```

---

## Build & Test

### Prerequisites
- MinGW-w64 GCC (included with MSYS2) or MSVC (`cl.exe`)
- `mingw32-make` or GNU `make`

### Building with PowerShell
```powershell
# Build win_telemetry.exe and execute the test suite
.\build.ps1

# Clean build artifacts
.\build.ps1 -Clean

# Native machine tuning
.\build.ps1 -March native
```

### Building with GNU Make
```sh
# Build executable
mingw32-make -f Makefile.win all

# Run unit tests (26 assertions)
mingw32-make -f Makefile.win test

# Clean artifacts
mingw32-make -f Makefile.win clean
```

Artifacts are produced in `clients/win_telemetry/build/`:
- `win_telemetry.exe`: Production daemon executable
- `test_telemetry.exe`: Automated test runner

---

## Usage

### Interactive Daemon Mode
```sh
# Run with defaults (ws://127.0.0.1:8080/, 1000ms interval, JSON format)
build/win_telemetry.exe

# Custom target and 500ms streaming interval
build/win_telemetry.exe --host 192.168.1.100 --port 8080 --interval 500

# Stream packed binary telemetry frames
build/win_telemetry.exe --binary

# Verbose output showing decrypted gateway echo frames
build/win_telemetry.exe --verbose

# Run for a fixed number of samples (useful for automated testing)
build/win_telemetry.exe --count 10
```

### Windows Service Management
Run from an elevated (Administrator) command prompt or PowerShell:
```sh
# Register and install as a Windows Service (auto-start)
win_telemetry.exe --install

# Start the service
net start WinTelemetryDaemon

# Stop the service
net stop WinTelemetryDaemon

# Unregister and uninstall the service
win_telemetry.exe --uninstall
```

### Command Line Options

| Option | Default | Description |
|---|---|---|
| `--host <host>` | `127.0.0.1` | Gateway IP or hostname |
| `--port <port>` | `8080` | Gateway WebSocket port |
| `--interval <ms>` | `1000` | Telemetry sampling/streaming interval in ms |
| `--key <hex>` | `0x0123456789ABCDEF` | 64-bit XOR encryption key |
| `--binary` | `false` (JSON) | Send binary packed telemetry struct (`TELM`) |
| `--count <n>` | `-1` (infinite) | Stream `n` frames and terminate cleanly |
| `--verbose`, `-v` | `false` | Log hex payloads and roundtrip echo data |
| `--service` | `false` | Run under Windows Service Control Manager |
| `--install` | `false` | Register daemon as a Windows Service |
| `--uninstall` | `false` | Delete Windows Service registration |
| `--help`, `-h` | - | Display help menu |

### Environment Variables
- `GATEWAY_HOST`: Fallback host address
- `GATEWAY_PORT`: Fallback port
- `ASM_CRYPTO_KEY`: Fallback 64-bit key (e.g., `0x0123456789ABCDEF`)
- `TELEMETRY_INTERVAL`: Fallback interval in milliseconds

---

## Telemetry Payload Formats

### JSON Telemetry Frame
```json
{
  "hostname": "DESKTOP-DJU675U",
  "os": "Windows",
  "timestamp_ms": 1792917605260,
  "uptime_ms": 2693015,
  "cpu": {
    "usage_pct": 14.50,
    "tsc_cycles": 8668870725506,
    "tsc_delta": 1985916040,
    "tsc_overhead_cycles": 116
  },
  "memory": {
    "total_bytes": 25714823168,
    "avail_bytes": 12170391552,
    "used_bytes": 13544431616,
    "usage_pct": 52.00
  },
  "process": {
    "pid": 4568,
    "ws_bytes": 69632
  }
}
```

### Binary Telemetry Frame (`TELM`, 152 bytes)
Packed struct with magic `0x54454C4D` ('TELM'), protocol version 1, microsecond/millisecond timestamps, 64-bit TSC counters, memory metrics, and daemon PID.
