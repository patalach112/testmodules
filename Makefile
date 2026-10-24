#
# Repository-wide convenience targets. Component Makefiles under server/
# remain usable on their own.
#

# MinGW64/MSYS2: the Windows rules build .exe files and compile the C core
# as static objects. No .so is involved on that platform.
ifeq ($(OS),Windows_NT)

.PHONY: build clean test

build:
	$(MAKE) -C server/cpp_gateway
	$(MAKE) -C client/ws_client

clean:
	$(MAKE) -C server/cpp_gateway clean
	$(MAKE) -C client/ws_client clean

test:
	@echo "Automated tests run on Linux. On Windows, launch build/gateway.exe"
	@echo "and exercise it with client/ws_client/build/ws_client.exe --roundtrip."

else

# Match the deployment CPU (Intel i5-3470, Ivy Bridge). Leave empty for a
# native build on the current machine: make build MARCH=
MARCH ?= -march=ivybridge -mtune=ivybridge

.PHONY: build clean test

build:
	$(MAKE) -C server/c_asm_crypto MARCH='$(MARCH)'
	$(MAKE) -C server/cpp_gateway MARCH='$(MARCH)'
	$(MAKE) -C client/ws_client MARCH='$(MARCH)'

clean:
	$(MAKE) -C server/c_asm_crypto clean
	$(MAKE) -C server/cpp_gateway clean
	$(MAKE) -C client/ws_client clean

# Unit tests: builds everything, then runs the asm_crypto test suite
# (all lengths and alignments, in-place and out-of-place, reference compared).
test: build
	$(MAKE) -C server/c_asm_crypto check

endif
