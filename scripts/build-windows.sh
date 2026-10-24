#!/usr/bin/env bash
#
# Native MinGW64/MSYS2 build of gateway.exe.
#
# Run inside an MINGW64 terminal from the repository root:
#
#   pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-boost make
#   ./scripts/build-windows.sh
#
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v g++ >/dev/null 2>&1; then
    echo "error: g++ not found. Install the MSYS2 toolchain:" >&2
    echo "  pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-boost make" >&2
    exit 1
fi

# Boost.Beast ships as headers in the mingw-w64-x86_64-boost package; a
# missing header directory means the package is not installed.
if [ ! -d "/mingw64/include/boost/beast" ]; then
    echo "error: Boost.Beast headers not found under /mingw64/include." >&2
    echo "  pacman -S --needed mingw-w64-x86_64-boost" >&2
    exit 1
fi

# OS=Windows_NT makes the component Makefile include Makefile.win.
make -C server/cpp_gateway clean
make -C server/cpp_gateway

echo
echo "built: server/cpp_gateway/build/gateway.exe"
