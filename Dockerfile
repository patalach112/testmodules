# syntax=docker/dockerfile:1

#
# Multi-stage image for the testmodules WebSocket gateway.
# Target host: Debian 13 on an Intel i5-3470 (Ivy Bridge), hence the
# ivybridge -march/-mtune flags instead of -march=native, which would
# encode the build machine's CPU rather than the deployment target.
#

# ---------- build stage ----------
FROM debian:trixie-slim AS build

ARG GCC_ARCH=ivybridge

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
        build-essential \
        libboost-dev \
        make \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY server ./server

RUN make -C server/c_asm_crypto \
        MARCH="-march=${GCC_ARCH} -mtune=${GCC_ARCH}" \
 && make -C server/cpp_gateway \
        MARCH="-march=${GCC_ARCH} -mtune=${GCC_ARCH}"

# ---------- runtime stage ----------
FROM debian:trixie-slim AS runtime

# Runtime defaults; docker-compose.yml overrides them per deployment.
ENV GATEWAY_BIND=0.0.0.0 \
    GATEWAY_PORT=8080

RUN groupadd --system app \
 && useradd --system --gid app --home-dir /nonexistent --shell /usr/sbin/nologin app

# ldconfig derives the libasm_crypto.so.1 SONAME symlink from the real file.
COPY --from=build /src/server/c_asm_crypto/build/libasm_crypto.so.1.0.0 \
                  /usr/local/lib/libasm_crypto.so.1.0.0
COPY --from=build /src/server/cpp_gateway/build/gateway \
                  /usr/local/bin/gateway
RUN ldconfig

USER app

# Documented for container networking; nothing is published to the host
# because cloudflared reaches the gateway over the internal Docker network.
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/${GATEWAY_PORT:-8080}' || exit 1

ENTRYPOINT ["/usr/local/bin/gateway"]
