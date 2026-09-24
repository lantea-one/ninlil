# syntax=docker/dockerfile:1
#
# Ninlil's container image, and the admin console's Docker-based build and tests.
#
# The .NET side of Ninlil -- tests, the self-contained binaries, and the NuGet packages -- is
# built with the .NET SDK directly (see .forgejo/scripts/). This file covers the rest, selected
# with --target:
#
#   runtime (default)
#             The hardened container image: a prebuilt, self-contained ninlil-linux-<arch>
#             executable (REST API, DNS and DHCP engines, and the admin console, all in one file)
#             on Microsoft's chiseled runtime-deps base image, which has no shell and no package
#             manager and runs as a non-root user. The executables come from a named build
#             context called "binaries", a directory holding ninlil-linux-amd64 and/or
#             ninlil-linux-arm64 (see .forgejo/scripts/publish-binary.sh).
#   admin-console-dist
#             The admin console's production build, written out with
#             `--output type=local,dest=src/dotnet/src/Ninlil.Web/wwwroot` so a .NET build can
#             embed or serve it.
#   test-admin-console
#             Lint, formatting, and the Jasmine/Karma suite for the admin console. It produces no
#             output: building the target succeeds only if all of it passes.
#
# The runtime image contains no compiled code of its own, only files copied into place, so a
# multi-platform build (linux/amd64 + linux/arm64) needs no CPU emulation.
#
# Local usage, from the repository root:
#   docker buildx build --target admin-console-dist \
#     --output type=local,dest=src/dotnet/src/Ninlil.Web/wwwroot .
#   .forgejo/scripts/publish-binary.sh linux-amd64 dist
#   docker buildx build --build-context binaries=dist -t ninlil .

ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra
ARG NODE_IMAGE=node:22-bookworm-slim
ARG FETCH_IMAGE=buildpack-deps:noble-curl

# ---------------------------------------------------------------------------------------------
# admin-console-source: the Angular workspace with its dependencies installed.
# ---------------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM ${NODE_IMAGE} AS admin-console-source

# `npm run build` first regenerates the OpenAPI client from the committed
# openapi/swagger.json snapshot with openapi-generator-cli, which is a Java tool.
RUN apt-get update \
    && apt-get install -y --no-install-recommends default-jre-headless \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace/src/angular

# The manifests alone first, so the dependency install is its own cached layer.
COPY src/angular/package.json src/angular/package-lock.json ./
RUN npm ci

COPY src/angular/ ./

# ---------------------------------------------------------------------------------------------
# admin-console: the Angular admin console's production build.
# ---------------------------------------------------------------------------------------------
FROM admin-console-source AS admin-console

RUN npm run build

# ---------------------------------------------------------------------------------------------
# admin-console-dist: just the production build, for `--output type=local`.
# ---------------------------------------------------------------------------------------------
FROM scratch AS admin-console-dist

COPY --from=admin-console /workspace/src/angular/dist/angular/browser/ /

# ---------------------------------------------------------------------------------------------
# test-admin-console: lint, formatting, and the Jasmine/Karma suite in headless Chromium.
# ---------------------------------------------------------------------------------------------
FROM admin-console-source AS test-admin-console

RUN apt-get update \
    && apt-get install -y --no-install-recommends chromium \
    && rm -rf /var/lib/apt/lists/*

ENV CHROME_BIN=/usr/bin/chromium

RUN npm run lint \
    && npm run format:check \
    && npm run test -- --watch=false --browsers=ChromeHeadlessNoSandbox

# ---------------------------------------------------------------------------------------------
# native-libraries: libmsquic (DNS-over-QUIC) and its libnuma dependency for the target, plus
# the empty directories the shell-less runtime stage cannot create itself.
# ---------------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM ${FETCH_IMAGE} AS native-libraries

ARG TARGETARCH

COPY docker/fetch-native-libraries.sh /usr/local/bin/fetch-native-libraries.sh
RUN sh /usr/local/bin/fetch-native-libraries.sh "${TARGETARCH}" /native \
    && mkdir -p /staging/data /staging/wwwroot

# ---------------------------------------------------------------------------------------------
# runtime: the hardened container image.
# ---------------------------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS runtime

ARG TARGETARCH
ARG BUILD_VERSION=0.0.0-local
ARG BUILD_REVISION=unknown
ARG BUILD_SOURCE=https://git.lantea.one/Lantea.One/ninlil

LABEL org.opencontainers.image.title="Ninlil" \
      org.opencontainers.image.description="DNS, DHCPv4/DHCPv6 and Router Advertisement server with a REST API and web admin console" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.source="${BUILD_SOURCE}" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.revision="${BUILD_REVISION}"

COPY --from=native-libraries /native/ /

# The executable stays root-owned and therefore read-only to the non-root user it runs as; only
# the data directory is writable. The web root is deliberately empty: the admin console is
# served from inside the executable, and an existing directory keeps ASP.NET Core from trying
# to create one in the read-only /app.
COPY --from=binaries --chmod=0755 ninlil-linux-${TARGETARCH} /app/ninlil
COPY --from=native-libraries /staging/wwwroot/ /app/wwwroot/
COPY --from=native-libraries --chown=1654:1654 /staging/data/ /app/data/

WORKDIR /app

# The executable extracts its bundled native libraries on first start.
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp

# The base image's non-root "app" user (uid 1654). Docker lets an unprivileged process in a
# container bind ports below 1024 on bridge networking (it sets ip_unprivileged_port_start=0 in
# the container's network namespace), so the standard ports below work without root. With
# host networking, and for DHCP (raw sockets, port 67/547), run the container as root and grant
# NET_ADMIN/NET_RAW; see README.md.
USER 1654

# The HTTP/HTTPS ports of the REST API and admin console, and plain DNS -- the one DNS transport
# enabled by default. Every other transport and the DHCP services are off by default; publish
# their ports explicitly when enabling them.
EXPOSE 80/tcp 443/tcp 53/udp 53/tcp

# Everything the server persists lives here: the SQLite database, the Data Protection key ring
# (without which every stored secret becomes unreadable), the JWT signing key, certificates, and
# the appsettings.json the admin console's settings pages write to. Mount a named volume or a
# host directory here in any real deployment.
VOLUME ["/app/data"]

# The server keeps its data directory next to the executable, at /app/data. It doubles as the
# content root, so appsettings.json is read from and written to the persistent volume rather
# than the read-only application directory. Configuration is otherwise supplied through
# NINLIL_-prefixed environment variables (see ENVIRONMENT.md).
ENTRYPOINT ["/app/ninlil", "--contentRoot=/app/data", "--webroot=/app/wwwroot"]
