#
# Platform dispatcher: MSYS2/MINGW64 exports OS=Windows_NT and builds
# COFF objects via Makefile.win; everywhere else builds the Linux archive.
#
ifeq ($(OS),Windows_NT)
include Makefile.win
else

#
# Linux build for the Binary Packet Protocol core.
#
# The module ships as a position-independent static archive so downstream
# binaries (gateway, clients) embed it without an extra runtime library.
# The SSE4.2 path is selected at runtime via CPUID, so the archive itself
# is built for baseline x86-64 and needs no -msse4.2 flag.
#
MARCH      ?= -march=native
CC         ?= cc
CXX        ?= c++
CFLAGS     ?= -std=c11 -O3 $(MARCH) -Wall -Wextra -Wpedantic \
              -fPIC -Iinclude -MMD -MP
CXXFLAGS   ?= -std=c++20 -O3 $(MARCH) -Wall -Wextra -Wpedantic \
              -Iinclude -MMD -MP

LIB        := libbinary_packer.a
BUILD_DIR  := build
SRC        := $(wildcard src/*.c)
OBJ        := $(patsubst src/%.c,$(BUILD_DIR)/%.o,$(SRC))
DEP        := $(OBJ:.o=.d)
TEST_BIN   := $(BUILD_DIR)/packer_test
CXXTEST    := $(BUILD_DIR)/cpp_wrapper_check

.PHONY: all clean check

all: $(BUILD_DIR)/$(LIB)

$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)

$(BUILD_DIR)/$(LIB): $(OBJ)
	$(AR) rcs $@ $^

$(BUILD_DIR)/%.o: src/%.c | $(BUILD_DIR)
	$(CC) $(CFLAGS) -c $< -o $@

# C ABI tests link the raw objects; the C++ wrapper check links the archive.
check: $(OBJ) $(BUILD_DIR)/$(LIB) | $(BUILD_DIR)
	$(CC) $(CFLAGS) tests/packer_test.c $(OBJ) -o $(TEST_BIN)
	./$(TEST_BIN)
	$(CXX) $(CXXFLAGS) tests/cpp_wrapper_check.cpp $(BUILD_DIR)/$(LIB) \
	    -o $(CXXTEST)
	./$(CXXTEST)

clean:
	rm -rf $(BUILD_DIR)

-include $(DEP)

endif
