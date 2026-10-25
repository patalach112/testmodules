#
# Repository-wide convenience targets. Component Makefiles under server/
# remain usable on their own.
#

# MinGW64/MSYS2: the Windows rules build gateway.exe and compile the C core
# as a static object. No .so is involved on that platform.
ifeq ($(OS),Windows_NT)

.PHONY: build clean test

build:
	$(MAKE) -C server/cpp_gateway
	$(MAKE) -C clients/win_telemetry

clean:
	$(MAKE) -C server/cpp_gateway clean
	$(MAKE) -C clients/win_telemetry clean

test:
	$(MAKE) -C clients/win_telemetry test

else

# Match the deployment CPU (Intel i5-3470, Ivy Bridge). Leave empty for a
# native build on the current machine: make build MARCH=
MARCH ?= -march=ivybridge -mtune=ivybridge

.PHONY: build clean test

build:
	$(MAKE) -C server/c_asm_crypto MARCH='$(MARCH)'
	$(MAKE) -C server/cpp_gateway MARCH='$(MARCH)'

clean:
	$(MAKE) -C server/c_asm_crypto clean
	$(MAKE) -C server/cpp_gateway clean

# Unit tests: builds everything, then runs the asm_crypto test suite
# (all lengths and alignments, in-place and out-of-place, reference compared).
test: build
	$(MAKE) -C server/c_asm_crypto check

endif
