#
# Platform dispatcher.
#
# MSYS2/MINGW64 exports OS=Windows_NT; the native Windows rules then build
# ws_client.exe with the asm_crypto object linked statically.
#
ifeq ($(OS),Windows_NT)
include Makefile.win
else

#
# Linux build rules for the ws_client module.
#
# Stages the libasm_crypto.so symlink chain into build/ and resolves it
# through a $ORIGIN runpath, so the binary is relocatable without setting
# LD_LIBRARY_PATH.
#
MARCH      ?= -march=native
CXX        ?= c++
PACKER_DIR := ../../shared/binary_packer
CXXFLAGS   ?= -std=c++20 -O3 $(MARCH) -Wall -Wextra -Wpedantic \
              -Iinclude -I../../server/c_asm_crypto/include \
              -I$(PACKER_DIR)/include -MMD -MP
LDFLAGS    ?= -L$(BUILD_DIR) -Wl,-rpath,'$$ORIGIN'
LDLIBS     ?= -lasm_crypto

BUILD_DIR  := build
BIN        := $(BUILD_DIR)/ws_client
CRYPTO_NAME := asm_crypto
CRYPTO_LIB := ../../server/c_asm_crypto/build/lib$(CRYPTO_NAME).so
PACKER_LIB := $(PACKER_DIR)/build/libbinary_packer.a
SRC        := $(wildcard src/*.cpp)
OBJ        := $(patsubst src/%.cpp,$(BUILD_DIR)/%.o,$(SRC))
DEP        := $(OBJ:.o=.d)

.PHONY: all clean

all: $(BIN)

$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)

$(BIN): $(OBJ) $(CRYPTO_LIB) $(PACKER_LIB) | $(BUILD_DIR)
	cp -a ../../server/c_asm_crypto/build/lib$(CRYPTO_NAME).so* $(BUILD_DIR)/
	$(CXX) $(OBJ) $(PACKER_LIB) -o $@ $(LDFLAGS) $(LDLIBS)

$(BUILD_DIR)/%.o: src/%.cpp | $(BUILD_DIR)
	$(CXX) $(CXXFLAGS) -c $< -o $@

$(CRYPTO_LIB):
	$(MAKE) -C ../../server/c_asm_crypto MARCH='$(MARCH)'

$(PACKER_LIB):
	$(MAKE) -C $(PACKER_DIR) MARCH='$(MARCH)'

clean:
	rm -rf $(BUILD_DIR)

-include $(DEP)

endif
