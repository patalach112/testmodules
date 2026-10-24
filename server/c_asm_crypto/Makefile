#
# Build rules for libasm_crypto.so
#
# Standard versioned shared-library layout:
#
#   libasm_crypto.so.1.0.0   real file (SONAME libasm_crypto.so.1)
#   libasm_crypto.so.1       SONAME symlink, used by the runtime loader
#   libasm_crypto.so         linker symlink, used at build time (-lasm_crypto)
#
# Override MARCH for portable or container builds, e.g.:
#   make MARCH="-march=ivybridge -mtune=ivybridge"
#
MARCH      ?= -march=native
CC         ?= cc
CFLAGS     ?= -std=c11 -O3 $(MARCH) -Wall -Wextra -Wpedantic \
              -fPIC -fstack-protector-strong -Iinclude -MMD -MP
LDFLAGS    ?=
LDLIBS     ?=

LIB_NAME   := asm_crypto
LIB_MAJOR  := 1
LIB_FILE   := lib$(LIB_NAME).so.$(LIB_MAJOR).0.0
LIB_SONAME := lib$(LIB_NAME).so.$(LIB_MAJOR)
LIB_LINK   := lib$(LIB_NAME).so

BUILD_DIR  := build
OBJ        := $(BUILD_DIR)/asm_crypto.o
DEP        := $(OBJ:.o=.d)
TEST_BIN   := $(BUILD_DIR)/asm_crypto_test

PREFIX     ?= /usr/local

.PHONY: all clean install check

all: $(BUILD_DIR)/$(LIB_FILE)

$(BUILD_DIR):
	mkdir -p $(BUILD_DIR)

$(BUILD_DIR)/$(LIB_FILE): $(OBJ)
	$(CC) -shared -Wl,-soname,$(LIB_SONAME) $(LDFLAGS) -o $@ $^ $(LDLIBS)
	ln -sf $(LIB_FILE) $(BUILD_DIR)/$(LIB_SONAME)
	ln -sf $(LIB_SONAME) $(BUILD_DIR)/$(LIB_LINK)

$(BUILD_DIR)/%.o: src/%.c | $(BUILD_DIR)
	$(CC) $(CFLAGS) -c $< -o $@

install: all
	install -d $(DESTDIR)$(PREFIX)/lib $(DESTDIR)$(PREFIX)/include
	install -m 0755 $(BUILD_DIR)/$(LIB_FILE) $(DESTDIR)$(PREFIX)/lib/
	ln -sf $(LIB_FILE) $(DESTDIR)$(PREFIX)/lib/$(LIB_SONAME)
	ln -sf $(LIB_SONAME) $(DESTDIR)$(PREFIX)/lib/$(LIB_LINK)
	install -m 0644 include/asm_crypto.h $(DESTDIR)$(PREFIX)/include/

clean:
	rm -rf $(BUILD_DIR)

# Link the test against the object directly, so it needs neither the
# shared library nor an rpath/loader configuration.
check: $(OBJ) | $(BUILD_DIR)
	$(CC) $(CFLAGS) tests/asm_crypto_test.c $(OBJ) -o $(TEST_BIN)
	./$(TEST_BIN)

-include $(DEP)
