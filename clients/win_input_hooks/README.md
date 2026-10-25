# Windows Game Detector & Global Hotkey Engine (`win_input_hooks`)

Companion native Win32 C module to `win_telemetry`. Detects active foreground windows and games with zero polling overhead and captures system-wide keyboard shortcuts during gameplay, streaming encrypted event payloads to the WebSocket gateway (`server/cpp_gateway`).

## Key Features

- **Active Window & Game Tracker (`SetWinEventHook`)**:
  - Uses `EVENT_SYSTEM_FOREGROUND` with `WINEVENT_OUTOFCONTEXT` to receive window focus transitions directly into the thread message loop with **0% polling CPU overhead**.
  - Extracts active window title, PID (`GetWindowThreadProcessId`), and executable path (`QueryFullProcessImageNameA`).
  - Detects full-screen game mode by inspecting window boundary rectangles against primary monitor dimensions.
- **Global Hotkey Engine (`RegisterHotKey`)**:
  - Registers system-wide shortcuts that trigger even when running full-screen, exclusive-mode 3D games:
    - `CTRL + ALT + S`: In-game snapshot event
    - `CTRL + ALT + M`: Mute / signal toggle event
    - `CTRL + ALT + O`: In-game overlay toggle event
    - `CTRL + ALT + Q`: Clean engine shutdown
- **Crypto & WebSocket Networking**:
  - Links directly against `server/c_asm_crypto` core object (`asm_crypto.o`).
  - Automatically encrypts event frames in-place using the 64-bit key (`0x0123456789ABCDEF`).
  - Encrypted frames are echoed back by `cpp_gateway`, decrypted via XOR involution, and validated for complete roundtrip integrity.
  - Zero dynamic heap allocation in event dispatching.
- **Ultra-Lightweight Footprint**:
  - **Resident Working Set: ~0.07 MB RAM** (far below the `<1.0 MB` limit).
  - Standalone binary size: **~280 KB**.
  - Zero background CPU usage while idle: uses Win32 `GetMessage` event-driven message pump.

---

## Repository Structure

```
clients/win_input_hooks/
├── include/
│   ├── config.h          # Command-line configuration & environment variables
│   ├── hotkey_engine.h   # RegisterHotKey definitions & dispatcher
│   ├── protocol.h        # JSON & packed binary event serialization
│   ├── window_tracker.h  # SetWinEventHook foreground tracker & process querying
│   └── ws_client.h       # Zero-dependency Winsock2 RFC 6455 client
├── src/
│   ├── config.c          # CLI arguments parser
│   ├── hotkey_engine.c   # Global hotkey registration & lifecycle
│   ├── main.c            # Event pump, console handler, and streaming
│   ├── protocol.c        # Event framing & c_asm_crypto encryption
│   ├── window_tracker.c  # WinEventProc foreground hook & full-screen detection
│   └── ws_client.c       # Native Winsock WebSocket client
├── tests/
│   └── test_input_hooks.c# Automated unit test suite (32 assertions)
├── build.ps1             # PowerShell build script for MinGW and MSVC
├── Makefile              # Platform dispatcher
├── Makefile.win          # GNU Make / MinGW-w64 build rules
└── README.md
```

---

## Build & Test

### PowerShell Build Script
```powershell
# Build win_input_hooks.exe and run test suite
.\build.ps1

# Clean build directory
.\build.ps1 -Clean

# Native machine tuning
.\build.ps1 -March native
```

### GNU Make / MinGW-w64
```sh
# Build executable
mingw32-make -f Makefile.win all

# Run unit tests (32 assertions)
mingw32-make -f Makefile.win test

# Clean artifacts
mingw32-make -f Makefile.win clean
```

---

## Usage

### Run Daemon Interactively
```sh
# Start monitoring with defaults (ws://127.0.0.1:8080/, JSON format)
build/win_input_hooks.exe

# Stream packed binary events
build/win_input_hooks.exe --binary

# Verbose logging with roundtrip payload inspection
build/win_input_hooks.exe --verbose

# Run in test simulation mode (emulates events and exits)
build/win_input_hooks.exe --simulate --count 2
```

### Command Line Options

| Option | Default | Description |
|---|---|---|
| `--host <host>` | `127.0.0.1` | Gateway IP or hostname |
| `--port <port>` | `8080` | Gateway WebSocket port |
| `--key <hex>` | `0x0123456789ABCDEF` | 64-bit XOR encryption key |
| `--binary` | `false` (JSON) | Send binary packed event struct (`INPT`) |
| `--count, -n <num>` | `-1` (infinite) | Exit after processing `<num>` events |
| `--simulate` | `false` | Dispatch synthetic events for automated testing |
| `--verbose`, `-v` | `false` | Log hex payloads and roundtrip echo data |
| `--help`, `-h` | - | Display help menu |

---

## Event Payload Formats

### Window Focus Event (JSON)
```json
{
  "type": "window_focus",
  "timestamp_ms": 1792923070544,
  "pid": 4321,
  "process_name": "EldenRing.exe",
  "exe_path": "C:\\Games\\Elden Ring\\Game\\eldenring.exe",
  "window_title": "ELDEN RING (TM)",
  "is_fullscreen": true
}
```

### Hotkey Press Event (JSON)
```json
{
  "type": "hotkey_press",
  "timestamp_ms": 1792923070544,
  "hotkey_id": 1,
  "hotkey_name": "SNAPSHOT",
  "combination": "CTRL + ALT + S"
}
```

### Binary Event Frame (`INPT`, 118 bytes)
Packed struct with magic `0x494E5054` ('INPT'), version 1, event type (1: window focus, 2: hotkey), millisecond timestamp, process ID, fullscreen flag, hotkey ID, process executable name, and event label.
