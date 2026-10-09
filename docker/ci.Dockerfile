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
#   lint    shellcheck and actionlint at pinned versions, plus bash, git, jq, curl and python3,
#           which the script self-tests in .forgejo/scripts/tests need. Runs
#           .forgejo/scripts/lint.sh.
#
# The base images are build arguments (SDK_IMAGE, NODE_IMAGE, ARCH_IMAGE, LINT_IMAGE), pinned by
# tag only.

ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-noble
ARG NODE_IMAGE=node:24-bookworm-slim
ARG ARCH_IMAGE=archlinux:base-devel
ARG LINT_IMAGE=debian:bookworm-slim

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

# ---------------------------------------------------------------------------------------------
# lint
# ---------------------------------------------------------------------------------------------
FROM ${LINT_IMAGE} AS lint

# The release binaries are pinned by version and checked against the SHA-256 sums below, one
# pair per CPU architecture (the image builds natively on amd64 and arm64 runners). To bump a
# tool, change the version and take the new sums from the release page: actionlint publishes a
# checksums file, ShellCheck does not, so its sums are computed from the downloaded archives.
ARG SHELLCHECK_VERSION=0.11.0
ARG SHELLCHECK_SHA256_AMD64=8c3be12b05d5c177a04c29e3c78ce89ac86f1595681cab149b65b97c4e227198
ARG SHELLCHECK_SHA256_ARM64=12b331c1d2db6b9eb13cfca64306b1b157a86eb69db83023e261eaa7e7c14588
ARG ACTIONLINT_VERSION=1.7.12
ARG ACTIONLINT_SHA256_AMD64=8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8
ARG ACTIONLINT_SHA256_ARM64=325e971b6ba9bfa504672e29be93c24981eeb1c07576d730e9f7c8805afff0c6

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends bash ca-certificates curl git jq python3 xz-utils; \
    case "$(dpkg --print-architecture)" in \
        amd64) shellcheck_arch=x86_64; actionlint_arch=amd64; \
               shellcheck_sha="${SHELLCHECK_SHA256_AMD64}"; actionlint_sha="${ACTIONLINT_SHA256_AMD64}" ;; \
        arm64) shellcheck_arch=aarch64; actionlint_arch=arm64; \
               shellcheck_sha="${SHELLCHECK_SHA256_ARM64}"; actionlint_sha="${ACTIONLINT_SHA256_ARM64}" ;; \
        *) echo "Unsupported architecture" >&2; exit 1 ;; \
    esac; \
    cd /tmp; \
    curl -fsSLO "https://github.com/koalaman/shellcheck/releases/download/v${SHELLCHECK_VERSION}/shellcheck-v${SHELLCHECK_VERSION}.linux.${shellcheck_arch}.tar.xz"; \
    echo "${shellcheck_sha}  shellcheck-v${SHELLCHECK_VERSION}.linux.${shellcheck_arch}.tar.xz" | sha256sum -c -; \
    tar -xJf "shellcheck-v${SHELLCHECK_VERSION}.linux.${shellcheck_arch}.tar.xz"; \
    install -m 0755 "shellcheck-v${SHELLCHECK_VERSION}/shellcheck" /usr/local/bin/shellcheck; \
    curl -fsSLO "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_linux_${actionlint_arch}.tar.gz"; \
    echo "${actionlint_sha}  actionlint_${ACTIONLINT_VERSION}_linux_${actionlint_arch}.tar.gz" | sha256sum -c -; \
    tar -xzf "actionlint_${ACTIONLINT_VERSION}_linux_${actionlint_arch}.tar.gz" actionlint; \
    install -m 0755 actionlint /usr/local/bin/actionlint; \
    rm -rf /tmp/* /var/lib/apt/lists/*; \
    shellcheck --version; \
    actionlint -version
