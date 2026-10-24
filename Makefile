#
# Repository-wide convenience targets. Component Makefiles under server/
# remain usable on their own.
#

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

# Smoke test: builds everything and exercises the XOR transform round trip.
test: build
	@tmp=$$(mktemp --suffix=.c) \
	&& printf '%s\n' \
	  '#include "asm_crypto.h"' \
	  '#include <assert.h>' \
	  'int main(void) {' \
	  '    unsigned char buf[19] = "testmodules-payload";' \
	  '    unsigned char orig[19];' \
	  '    for (int i = 0; i < 19; ++i) orig[i] = buf[i];' \
	  '    asm_crypto_xor_in_place(buf, 19, 0x0123456789ABCDEFull);' \
	  '    asm_crypto_xor_in_place(buf, 19, 0x0123456789ABCDEFull);' \
	  '    for (int i = 0; i < 19; ++i) assert(buf[i] == orig[i]);' \
	  '    return 0;' \
	  '}' > "$$tmp" \
	&& cc -Iserver/c_asm_crypto/include "$$tmp" \
	      -Lserver/c_asm_crypto/build -lasm_crypto \
	      -Wl,-rpath,server/c_asm_crypto/build -o /tmp/asm_crypto_smoke \
	&& /tmp/asm_crypto_smoke \
	&& rm -f "$$tmp" /tmp/asm_crypto_smoke \
	&& echo "smoke test OK"
