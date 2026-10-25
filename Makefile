#
# Repository-wide convenience targets. Component Makefiles under server/
# remain usable on their own.
#

# MinGW64/MSYS2: the Windows rules build gateway.exe and compile the C core
# as a static object. No .so is involved on that platform.
ifeq ($(OS),Windows_NT)

.PHONY: build clean test desktop

build:
	$(MAKE) -C server/cpp_gateway

# Windows desktop client (WPF). Builds the shared C/ASM core into
# clients/desktop_windows/native/asm_crypto.dll, then the app, then stages the
# DLL next to the executable.
desktop:
	$(MAKE) -C clients/desktop_windows build

clean:
	$(MAKE) -C server/cpp_gateway clean
	-$(MAKE) -C clients/desktop_windows clean

test:
	@echo "Automated smoke tests run on Linux; launch build/gateway.exe on Windows."

else

# Match the deployment CPU (Intel i5-3470, Ivy Bridge). Leave empty for a
# native build on the current machine: make build MARCH=
MARCH ?= -march=ivybridge -mtune=ivybridge

.PHONY: build clean test desktop desktop-native

build:
	$(MAKE) -C server/c_asm_crypto MARCH='$(MARCH)'
	$(MAKE) -C server/cpp_gateway MARCH='$(MARCH)'

clean:
	$(MAKE) -C server/c_asm_crypto clean
	$(MAKE) -C server/cpp_gateway clean

# The Windows desktop client targets net8.0-windows and needs a Windows host to
# build. Its native core, however, is portable and can be built here.
desktop:
	@echo "The WPF client builds on Windows only. On a Windows machine:"
	@echo "  MSYS2     : make -C clients/desktop_windows"
	@echo "  PowerShell: powershell -ExecutionPolicy Bypass -File clients/desktop_windows/build.ps1"
	@echo "This host can still build the client's native core: make desktop-native"

desktop-native:
	$(MAKE) -C clients/desktop_windows native

# Unit tests: builds everything, then runs the asm_crypto test suite
# (all lengths and alignments, in-place and out-of-place, reference compared).
test: build
	$(MAKE) -C server/c_asm_crypto check

endif
