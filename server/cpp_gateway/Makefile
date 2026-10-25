#
# Platform dispatcher.
#
# MSYS2/MINGW64 exports OS=Windows_NT, in which case the native Windows
# rules (static object linking, ws2_32/mswsock) take over automatically so
# that `make clean && make` produces build/gateway.exe without extra flags.
#
ifeq ($(OS),Windows_NT)
include Makefile.win
else

#
# Linux build rules for the WebSocket gateway.
#
# The binary links against libasm_crypto.so from ../c_asm_crypto. The shared
# library is copied next to the binary so the $ORIGIN runpath resolves it
# both in local builds and in the runtime container.
#
# Override MARCH for portable or container builds, e.g.:
#   make MARCH="-march=ivybridge -mtune=ivybridge"
#
MARCH      ?= -march=native
CXX        ?= c++
PACKER_DIR := ../../shared/binary_packer
CXXFLAGS   ?= -std=c++20 -O3 $(MARCH) -Wall -Wextra -Wpedantic \
              -Iinclude -I../c_asm_crypto/include -I$(PACKER_DIR)/include \
              -MMD -MP
LDFLAGS    ?= -L$(BUILD_DIR) -Wl,-rpath,'$$ORIGIN'
LDLIBS     ?= -lasm_crypto

BUILD_DIR  := build
BIN        := $(BUILD_DIR)/gateway
CRYPTO_NAME := asm_crypto
CRYPTO_LIB := ../c_asm_crypto/build/lib$(CRYPTO_NAME).so
PACKER_LIB := $(PACKER_DIR)/build/libbinary_packer.a
SRC        := $(wildcard src/*.cpp)
OBJ        := $(patsubst src/%.cpp,$(BUILD_DIR)/%.o,$(SRC))
DEP        := $(OBJ:.o=.d)

.PHONY: all clean

all: $(BIN)

$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)

# Build the C dependency first, then stage the .so next to the gateway so
# the $ORIGIN runpath set in LDFLAGS finds it without LD_LIBRARY_PATH.
$(BIN): $(OBJ) $(CRYPTO_LIB) $(PACKER_LIB) | $(BUILD_DIR)
	# Stage the full symlink chain (.so -> .so.1 -> .so.1.0.0).
	cp -a ../c_asm_crypto/build/lib$(CRYPTO_NAME).so* $(BUILD_DIR)/
	$(CXX) $(OBJ) $(PACKER_LIB) -o $@ $(LDFLAGS) $(LDLIBS)

$(BUILD_DIR)/%.o: src/%.cpp | $(BUILD_DIR)
	$(CXX) $(CXXFLAGS) -c $< -o $@

$(CRYPTO_LIB):
	$(MAKE) -C ../c_asm_crypto MARCH='$(MARCH)'

$(PACKER_LIB):
	$(MAKE) -C $(PACKER_DIR) MARCH='$(MARCH)'

clean:
	rm -rf $(BUILD_DIR)
	$(MAKE) -C ../c_asm_crypto clean

-include $(DEP)

endif
