# syntax=docker/dockerfile:1
#
# Ninlil's container image.
#
# Nothing is compiled here. The image is a prebuilt, self-contained ninlil-linux-<arch>
# executable (REST API, DNS and DHCP engines, and the admin console, all in one file) on
# Microsoft's chiseled runtime-deps base image, which has no shell and no package manager and
# runs as a non-root user. The executables, tests and NuGet packages are built with the .NET SDK
# and Node.js by .forgejo/scripts/.
#
# Inputs:
#   build context "binaries"  a directory holding ninlil-linux-amd64 and/or ninlil-linux-arm64
#   build context "."         the repository (only docker/fetch-native-libraries.sh and
#                             docker/native-libraries.lock are used)
#   build arguments           RUNTIME_IMAGE and FETCH_IMAGE (base images, pinned by
#                             index digest and refreshed by bump-pins.yml), BUILD_VERSION,
#                             BUILD_REVISION and BUILD_SOURCE (OCI labels)
#
# Output: the "runtime" target. The final stage only copies files and the native-libraries stage
# runs on the build platform, so a multi-platform build (linux/amd64 + linux/arm64) needs no CPU
# emulation.
#
# Local usage, from the repository root:
#   .forgejo/scripts/build-admin-console.sh
#   .forgejo/scripts/publish-binary.sh linux-amd64 dist
#   docker buildx build --build-context binaries=dist -t ninlil .

ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra@sha256:566a1d5613279cf96a70bc456b85a5e1dda0cc53f394209680d54b55e4d13cf7
ARG FETCH_IMAGE=buildpack-deps:noble-curl@sha256:48a80dfb7e2f2d1e93dbfff934fd2f187f306eef36cb5359ef8bba0ffe9205b3

# ---------------------------------------------------------------------------------------------
# native-libraries: libmsquic (DNS-over-QUIC) and its libnuma dependency for the target
# architecture, plus the empty directories the shell-less runtime stage cannot create. It runs on
# the build platform and downloads the target's packages named in native-libraries.lock (see
# fetch-native-libraries.sh).
# ---------------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM ${FETCH_IMAGE} AS native-libraries

ARG TARGETARCH

# The lock pins each package by version and SHA-256; the script fails the build on a mismatch and
# finds the lock next to itself.
COPY docker/fetch-native-libraries.sh docker/native-libraries.lock /usr/local/bin/
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
# the data directory is writable. The web root is empty on purpose: the admin console is served
# from inside the executable, and an existing directory keeps ASP.NET Core from trying to create
# one in the read-only /app.
COPY --from=binaries --chmod=0755 ninlil-linux-${TARGETARCH} /app/ninlil
COPY --from=native-libraries /staging/wwwroot/ /app/wwwroot/
COPY --from=native-libraries --chown=1654:1654 /staging/data/ /app/data/

WORKDIR /app

# The executable extracts its bundled native libraries on first start; /tmp is writable for the
# non-root user, /app is not.
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp

# The base image's non-root "app" user (uid 1654). Docker lets an unprivileged process in a
# container bind ports below 1024 on bridge networking (it sets ip_unprivileged_port_start=0 in
# the container's network namespace), so the standard ports below work without root. With
# host networking, and for DHCP (raw sockets, port 67/547), run the container as root; Docker's
# default capabilities (NET_RAW, NET_BIND_SERVICE) are enough. See README.md and the
# ninlil-host-network service in docker-compose.yml.
USER 1654

# The HTTP/HTTPS ports of the REST API and admin console, and plain DNS -- the one DNS transport
# enabled by default. Every other transport and the DHCP services are off by default; publish
# their ports explicitly when enabling them.
EXPOSE 80/tcp 443/tcp 53/udp 53/tcp

# Everything the server persists lives here: the SQLite database, the Data Protection key ring
# (without which every stored secret becomes unreadable), the JWT signing key, certificates, and
# the appsettings.json the admin console's settings pages write to. Mount a named volume or a
# host directory here in any deployment whose data must outlive the container.
VOLUME ["/app/data"]

# The server keeps its data directory next to the executable, at /app/data. It doubles as the
# content root, so appsettings.json is read from and written to the persistent volume rather
# than the read-only application directory. Configuration is otherwise supplied through
# NINLIL_-prefixed environment variables (see ENVIRONMENT.md).
ENTRYPOINT ["/app/ninlil", "--contentRoot=/app/data", "--webroot=/app/wwwroot"]
