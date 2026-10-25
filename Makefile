#
# Repository-wide convenience targets. Component Makefiles under server/
# remain usable on their own.
#

# MinGW64/MSYS2: the Windows rules build .exe files and compile the C core
# as static objects. No .so is involved on that platform.
ifeq ($(OS),Windows_NT)

.PHONY: build clean test desktop

build:
	$(MAKE) -C shared/binary_packer
	$(MAKE) -C server/cpp_gateway
	$(MAKE) -C client/ws_client

# Windows desktop client (WPF). Builds the shared C/ASM core into
# clients/desktop_windows/native/asm_crypto.dll, then the app, then stages the
# DLL next to the executable.
desktop:
	$(MAKE) -C clients/desktop_windows build

clean:
	$(MAKE) -C shared/binary_packer clean
	$(MAKE) -C server/cpp_gateway clean
	$(MAKE) -C client/ws_client clean
	-$(MAKE) -C clients/desktop_windows clean

test:
	@echo "Automated tests run on Linux. On Windows, launch build/gateway.exe"
	@echo "and exercise it with client/ws_client/build/ws_client.exe --roundtrip."

else

# Match the deployment CPU (Intel i5-3470, Ivy Bridge). Leave empty for a
# native build on the current machine: make build MARCH=
MARCH ?= -march=ivybridge -mtune=ivybridge

.PHONY: build clean test desktop desktop-native

build:
	$(MAKE) -C shared/binary_packer MARCH='$(MARCH)'
	$(MAKE) -C server/c_asm_crypto MARCH='$(MARCH)'
	$(MAKE) -C server/cpp_gateway MARCH='$(MARCH)'
	$(MAKE) -C client/ws_client MARCH='$(MARCH)'

clean:
	$(MAKE) -C shared/binary_packer clean
	$(MAKE) -C server/c_asm_crypto clean
	$(MAKE) -C server/cpp_gateway clean
	$(MAKE) -C client/ws_client clean

# Unit tests: builds everything, then runs the asm_crypto and
# binary_packer suites (reference vectors, corruption cases, alignments).
test: build
	$(MAKE) -C shared/binary_packer check
	$(MAKE) -C server/c_asm_crypto check

endif
