# syntax=docker/dockerfile:1
#
# Ninlil build and container image definition.
#
# This one file produces both of Ninlil's distributable outputs, selected with --target:
#
#   runtime   (default) The hardened container image: the combined host (REST API, DNS and DHCP
#             engines, admin console) on Microsoft's chiseled ASP.NET Core base image, which has
#             no shell and no package manager and runs as a non-root user.
#   binaries  The self-contained, single-file executables for Linux and macOS, written out with
#             `--output type=local,dest=<dir>` rather than packaged as an image.
#   packages  The NuGet packages for every library project (everything except the Ninlil.Web
#             host itself), written out the same way.
#   test-admin-console
#             Lint, formatting, and the Jasmine/Karma suite for the admin console. It produces no
#             output: building the target succeeds only if all of it passes.
#   test-dotnet
#             Formatting and the full Release build of the solution, as an image whose default
#             command runs every xUnit test project. The tests run in a container rather than
#             during the build because some of them start their own database containers
#             (Testcontainers), which needs the Docker socket. Run them as an unprivileged user:
#             on the host network that user cannot bind a port below 1024 or open a raw socket,
#             so no DNS, DHCP, or Router Advertisement test can ever reach a real service or
#             client on the machine's network (those tests skip themselves when they cannot bind):
#               docker build --target test-dotnet -t ninlil-test .
#               docker run --rm --network host --user "$(id -u):$(id -g)" \
#                 --group-add "$(stat -c %g /var/run/docker.sock)" \
#                 -v /var/run/docker.sock:/var/run/docker.sock ninlil-test
#
# Every build stage runs on the build machine's own architecture ($BUILDPLATFORM) and
# cross-compiles for the target, so a multi-platform build (linux/amd64 + linux/arm64) needs no
# CPU emulation at all: the runtime stage is the only one that belongs to the target platform,
# and it only copies files.
#
# Local usage, from the repository root:
#   docker build -t ninlil .
#   docker buildx build --target binaries --output type=local,dest=dist .
#
# The pipeline passes the computed build version in through the NINLIL_* build arguments below;
# a local build without them is versioned 0.0.0-local.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
ARG NODE_IMAGE=node:22-bookworm-slim

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
# source: the .NET sources with the admin console already in place under wwwroot/.
# ---------------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM ${SDK_IMAGE} AS source

WORKDIR /workspace

# Directory.Build.props and global.json are found by MSBuild's upward directory search, so they
# keep their place at the root of the tree. Test projects are never referenced by the host and
# are left out of the context.
COPY Directory.Build.props global.json ./
COPY src/dotnet/src/ ./src/dotnet/src/
COPY --from=admin-console /workspace/src/angular/dist/angular/browser/ ./src/dotnet/src/Ninlil.Web/wwwroot/

ARG NINLIL_VERSION=0.0.0-local
ARG NINLIL_PACKAGE_VERSION=0.0.0-local
ARG NINLIL_ASSEMBLY_VERSION=0.0.0.0

# Shared by every publish below. NinlilSkipAngularBuild stops Ninlil.Web.csproj from trying to
# rebuild the admin console itself: its output was copied in above.
ENV NINLIL_PUBLISH_PROPERTIES="-p:Version=${NINLIL_PACKAGE_VERSION} \
    -p:AssemblyVersion=${NINLIL_ASSEMBLY_VERSION} \
    -p:FileVersion=${NINLIL_ASSEMBLY_VERSION} \
    -p:InformationalVersion=${NINLIL_VERSION} \
    -p:ContinuousIntegrationBuild=true \
    -p:NinlilSkipAngularBuild=true"

# ---------------------------------------------------------------------------------------------
# test-dotnet: formatting and the full solution build; running the image runs every test.
# ---------------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM ${SDK_IMAGE} AS test-dotnet

# libmsquic, from Microsoft's package repository, for the DNS-over-QUIC tests.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
        -o /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends libmsquic \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

# The package cache lives inside the image (not under /root), so the unprivileged user the tests
# run as can read it.
ENV NUGET_PACKAGES=/nuget

COPY .config/ ./.config/
RUN dotnet tool restore

COPY .editorconfig Directory.Build.props global.json Ninlil.slnx ./
COPY src/dotnet/ ./src/dotnet/
# The web host's integration tests serve the real admin console (SPA fallback routing, static
# assets), exactly as a published host does.
COPY --from=admin-console /workspace/src/angular/dist/angular/browser/ ./src/dotnet/src/Ninlil.Web/wwwroot/

# The tests run as an unprivileged user (see test-dotnet in the header above), who needs to write
# test results and temporary files next to the build output.
RUN dotnet csharpier check . \
    && dotnet build Ninlil.slnx --configuration Release -p:NinlilSkipAngularBuild=true \
    && chmod -R a+rwX /workspace /nuget

ENV HOME=/tmp \
    DOTNET_CLI_HOME=/tmp \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

CMD ["dotnet", "test", "Ninlil.slnx", "--no-build", "--configuration", "Release"]

# ---------------------------------------------------------------------------------------------
# publish-image: the framework-dependent host for the container image's architecture.
# ---------------------------------------------------------------------------------------------
FROM source AS publish-image

ARG TARGETARCH

RUN case "${TARGETARCH}" in \
        amd64) rid='linux-x64' ;; \
        arm64) rid='linux-arm64' ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && dotnet publish src/dotnet/src/Ninlil.Web/Ninlil.Web.csproj \
        --configuration Release \
        --runtime "${rid}" \
        --no-self-contained \
        ${NINLIL_PUBLISH_PROPERTIES} \
        --output /app \
    # The persistent data directory has to exist, owned by the image's non-root user, before
    # the shell-less runtime stage copies it in.
    && mkdir -p /staging/data

# ---------------------------------------------------------------------------------------------
# native-libraries: libmsquic (DNS-over-QUIC) and its libnuma dependency for the target.
# ---------------------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM ${SDK_IMAGE} AS native-libraries

ARG TARGETARCH

COPY docker/fetch-native-libraries.sh /usr/local/bin/fetch-native-libraries.sh
RUN sh /usr/local/bin/fetch-native-libraries.sh "${TARGETARCH}" /native

# ---------------------------------------------------------------------------------------------
# publish-binaries: self-contained single-file executables for every release platform.
# ---------------------------------------------------------------------------------------------
FROM source AS publish-binaries

# Each binary carries the .NET runtime, every native library (extracted to a per-user cache
# directory on first start), and the admin console (NinlilEmbedWebRoot) inside one file. macOS
# binaries are ad-hoc code signed by the SDK itself, which Apple Silicon requires before it will
# run them at all.
RUN set -eu; \
    mkdir -p /binaries; \
    for target in linux-amd64:linux-x64 linux-arm64:linux-arm64 macos-amd64:osx-x64 macos-arm64:osx-arm64; do \
        name="${target%%:*}"; \
        rid="${target#*:}"; \
        dotnet publish src/dotnet/src/Ninlil.Web/Ninlil.Web.csproj \
            --configuration Release \
            --runtime "${rid}" \
            --self-contained \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true \
            -p:EnableCompressionInSingleFile=true \
            -p:NinlilEmbedWebRoot=true \
            -p:DebugType=none \
            ${NINLIL_PUBLISH_PROPERTIES} \
            --output "/publish/${rid}"; \
        cp "/publish/${rid}/Ninlil.Web" "/binaries/ninlil-${name}"; \
        chmod 0755 "/binaries/ninlil-${name}"; \
    done; \
    ls -l /binaries

# ---------------------------------------------------------------------------------------------
# binaries: just the executables, for `--output type=local`.
# ---------------------------------------------------------------------------------------------
FROM scratch AS binaries

COPY --from=publish-binaries /binaries/ /

# ---------------------------------------------------------------------------------------------
# publish-packages: NuGet packages for every library project.
# ---------------------------------------------------------------------------------------------
FROM source AS publish-packages

# Every project under src/dotnet/src except the Ninlil.Web host is a reusable library. A project
# reference between two of them becomes a NuGet dependency on the same version, so a set of
# packages from one build always depends on exactly that build's other packages. Debug symbols
# are embedded in each assembly so a consumer can step into the code with no symbol server.
RUN set -eu; \
    dotnet new sln --name packages --format slnx --output /packing; \
    for project in src/dotnet/src/*/*.csproj; do \
        case "${project}" in \
            */Ninlil.Web.csproj) continue ;; \
        esac; \
        dotnet sln /packing/packages.slnx add "${project}"; \
    done; \
    dotnet pack /packing/packages.slnx \
        --configuration Release \
        -p:DebugType=embedded \
        ${NINLIL_PUBLISH_PROPERTIES} \
        --output /packages; \
    ls -l /packages

# ---------------------------------------------------------------------------------------------
# packages: just the .nupkg files, for `--output type=local`.
# ---------------------------------------------------------------------------------------------
FROM scratch AS packages

COPY --from=publish-packages /packages/ /

# ---------------------------------------------------------------------------------------------
# runtime: the hardened container image.
# ---------------------------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS runtime

ARG NINLIL_VERSION=0.0.0-local
ARG NINLIL_REVISION=unknown
ARG NINLIL_SOURCE=https://git.lantea.one/Lantea.One/ninlil

LABEL org.opencontainers.image.title="Ninlil" \
      org.opencontainers.image.description="DNS, DHCPv4/DHCPv6 and Router Advertisement server with a REST API and web admin console" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.source="${NINLIL_SOURCE}" \
      org.opencontainers.image.version="${NINLIL_VERSION}" \
      org.opencontainers.image.revision="${NINLIL_REVISION}"

COPY --from=native-libraries /native/ /

# The application itself stays root-owned and therefore read-only to the non-root user it runs
# as; only the data directory is writable.
COPY --from=publish-image /app/ /app/
COPY --from=publish-image --chown=1654:1654 /staging/data/ /app/data/

WORKDIR /app

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

# The data directory doubles as the content root, so appsettings.json is read from and written
# to the persistent volume rather than the read-only application directory. Configuration is
# otherwise supplied through NINLIL_-prefixed environment variables (see ENVIRONMENT.md).
ENTRYPOINT ["dotnet", "/app/Ninlil.Web.dll", "--contentRoot=/app/data", "--webroot=/app/wwwroot"]
