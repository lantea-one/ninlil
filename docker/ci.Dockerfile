# syntax=docker/dockerfile:1
#
# The toolchains the pipeline's build and test steps run in, one target per toolchain. Nothing
# from the repository is copied in: .forgejo/scripts/in-container.sh builds a target from this
# file alone (tagged ninlil-ci-<target>) and runs a script in it with the checkout mounted, so
# the runner needs only Docker, and every build and test stays off the host.
#
#   dotnet  The .NET SDK, plus libmsquic from Microsoft's Ubuntu 24.04 repository for the
#           DNS-over-QUIC tests.
#   node    Node.js 24 with a Java runtime (`npm run build` regenerates the typed API client with
#           openapi-generator-cli, a Java tool) and Chromium for the Karma suite.
#   arch    makepkg, for the ninlil packages. Arch Linux publishes amd64 images only, so
#           in-container.sh runs this one as linux/amd64 everywhere.
#
# The base images are build arguments (SDK_IMAGE, NODE_IMAGE, ARCH_IMAGE), pinned by tag only.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-noble
ARG NODE_IMAGE=node:24-bookworm-slim
ARG ARCH_IMAGE=archlinux:base-devel

# ---------------------------------------------------------------------------------------------
# dotnet
# ---------------------------------------------------------------------------------------------
FROM ${SDK_IMAGE} AS dotnet

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl \
    && curl -fsSL https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
        -o /tmp/packages-microsoft-prod.deb \
    && dpkg -i /tmp/packages-microsoft-prod.deb \
    && rm /tmp/packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y --no-install-recommends libmsquic \
    && rm -rf /var/lib/apt/lists/*

# ---------------------------------------------------------------------------------------------
# node
# ---------------------------------------------------------------------------------------------
FROM ${NODE_IMAGE} AS node

RUN apt-get update \
    && apt-get install -y --no-install-recommends default-jre-headless chromium \
    && rm -rf /var/lib/apt/lists/*

# Karma's Chrome launcher finds the browser through CHROME_BIN.
ENV CHROME_BIN=/usr/bin/chromium

# ---------------------------------------------------------------------------------------------
# arch
# ---------------------------------------------------------------------------------------------
FROM ${ARCH_IMAGE} AS arch
