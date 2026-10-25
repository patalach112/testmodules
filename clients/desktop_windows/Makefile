# ---------------------------------------------------------------------------
# Ecosystem Client - Windows desktop build (WPF, net8.0-windows)
#
# Recommended: an MSYS2 / MINGW64 shell
#
#     pacman -S --needed mingw-w64-x86_64-gcc mingw-w64-x86_64-dotnet-sdk make
#     make                # native asm core + WPF app, DLL staged next to the exe
#     make run            # launch the client
#     make publish        # x64 publish folder
#
# No make? build.ps1 performs exactly the same steps:
#
#     powershell -ExecutionPolicy Bypass -File .\build.ps1
#
# The native library is built from the shared C/ASM sources in
# ../../server/c_asm_crypto so the desktop client and the gateway always run the
# same core.
# ---------------------------------------------------------------------------

DOTNET     ?= dotnet
CONFIG     ?= Release
CC         ?= gcc

CRYPTO_DIR := ../../server/c_asm_crypto
NATIVE_DIR := native
OUT_DIR    := bin/$(CONFIG)/net8.0-windows

ifeq ($(OS),Windows_NT)
  NATIVE_LIB   := $(NATIVE_DIR)/asm_crypto.dll
  NATIVE_FLAGS := -shared -Wl,--out-implib,$(NATIVE_DIR)/libasm_crypto.a
  NATIVE_COPY  := cp -f
else
  # Non-Windows: the WPF app cannot be built here, but producing the shared
  # library keeps the C/ASM core buildable (and testable) on Linux CI.
  NATIVE_LIB   := $(NATIVE_DIR)/libasm_crypto.so
  NATIVE_FLAGS := -shared -fPIC
  NATIVE_COPY  := cp -f
endif

.PHONY: all native build stage run publish clean verify help

all: build

help:
	@echo "targets: native | build (default) | stage | run | publish | verify | clean"
	@echo "         CONFIG=$(CONFIG) DOTNET=$(DOTNET) CC=$(CC)"

# --- native core -----------------------------------------------------------
native: $(NATIVE_LIB)

$(NATIVE_LIB): $(CRYPTO_DIR)/src/asm_crypto.c $(CRYPTO_DIR)/include/asm_crypto.h
	@echo "==> building $(NATIVE_LIB) from the shared C/ASM core"
	@mkdir -p $(NATIVE_DIR)
	$(CC) -std=c11 -O3 -Wall -Wextra -I$(CRYPTO_DIR)/include $(NATIVE_FLAGS) -o $@ $<

# --- managed app -----------------------------------------------------------
build: native
	@echo "==> dotnet build ($(CONFIG))"
	$(DOTNET) build EcosystemClient.csproj -c $(CONFIG)
	@$(MAKE) --no-print-directory stage

# The csproj copies native\asm_crypto.dll when it exists at evaluation time;
# this explicit copy guarantees the DLL lands next to the exe even on a
# second, incremental build.
stage:
	@mkdir -p $(OUT_DIR)
	@if [ -f $(NATIVE_LIB) ]; then \
		$(NATIVE_COPY) $(NATIVE_LIB) $(OUT_DIR)/ && \
		echo "==> staged $(NATIVE_LIB) -> $(OUT_DIR)/"; \
	else \
		echo "==> no native library: the client will use the managed fallback"; \
	fi

run: build
	$(DOTNET) run --project EcosystemClient.csproj -c $(CONFIG) --no-build

publish: native
	@echo "==> dotnet publish ($(CONFIG), win-x64)"
	$(DOTNET) publish EcosystemClient.csproj -c $(CONFIG) -r win-x64 \
		--self-contained false -p:PublishReadyToRun=true
	@echo "==> published to bin/$(CONFIG)/net8.0-windows/win-x64/publish"

verify: build
	@echo "==> verifying native library is deployed"
	@test -f $(OUT_DIR)/asm_crypto.dll && echo "ok: $(OUT_DIR)/asm_crypto.dll" || \
		(echo "FAIL: asm_crypto.dll is missing from $(OUT_DIR)" && exit 1)

clean:
	rm -rf bin obj $(NATIVE_DIR)
