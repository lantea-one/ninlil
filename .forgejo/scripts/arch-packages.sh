#!/usr/bin/env bash
# Builds the ninlil-bin Arch Linux packages for one build, for x86_64 and aarch64, and writes
# them into <binaries-directory> next to the binaries, so the pipeline attaches them to the
# release: ninlil-bin-<pkgver>-1-<arch>.pkg.tar.zst, installable directly with `pacman -U <url>`.
#
# The PKGBUILD is generated from src/archlinux/ninlil-bin/PKGBUILD.in with this build's version,
# download URLs, and SHA256 sums filled in -- the same PKGBUILD an AUR ninlil-bin package would
# carry. The packages are built from the binaries this pipeline run just produced (the release
# they would download from is published only after this step), and the checksums are still
# verified against them. The package only copies files, so both architectures are built here,
# on whatever architecture the runner has, in a stock Arch Linux container.
#
# Usage: arch-packages.sh <binaries-directory>
#
# Environment:
#   RELEASE_TAG   the release's tag (the computed version)
#   ARCH_VERSION  the package's pkgver (compute-version.sh's arch-version)

set -euo pipefail

: "${RELEASE_TAG:?RELEASE_TAG is required}"
binaries="$1"

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
template="${repository_root}/src/archlinux/ninlil-bin/PKGBUILD.in"
common="${repository_root}/src/archlinux/common"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
package="${work}/ninlil-bin"
mkdir -p "${package}"

# The pkgver, computed alongside every other form of the version (compute-version.sh).
pkgver="${ARCH_VERSION:?ARCH_VERSION is required}"

sha() { sha256sum "$1" | cut -d ' ' -f 1; }

cp "${common}/ninlil.service" "${common}/ninlil.sysusers" "${common}/ninlil.env" "${package}/"
cp "${repository_root}/LICENSE" "${package}/"

# The tag goes into a download URL path, where "+" must be percent-encoded.
tag_path="${RELEASE_TAG//+/%2B}"

sed \
    -e "s|@VERSION@|${RELEASE_TAG}|g" \
    -e "s|@PKGVER@|${pkgver}|g" \
    -e "s|@TAG_PATH@|${tag_path}|g" \
    -e "s|@SHA256_SERVICE@|$(sha "${package}/ninlil.service")|g" \
    -e "s|@SHA256_SYSUSERS@|$(sha "${package}/ninlil.sysusers")|g" \
    -e "s|@SHA256_ENV@|$(sha "${package}/ninlil.env")|g" \
    -e "s|@SHA256_LICENSE@|$(sha "${package}/LICENSE")|g" \
    -e "s|@SHA256_X86_64@|$(sha "${binaries}/ninlil-linux-amd64")|g" \
    -e "s|@SHA256_AARCH64@|$(sha "${binaries}/ninlil-linux-arm64")|g" \
    "${template}" > "${package}/PKGBUILD"

if grep -n '@[A-Z0-9_]*@' "${package}/PKGBUILD"; then
    echo "Unfilled placeholders remain in the generated PKGBUILD." >&2
    exit 1
fi

# The package's download names for the local binaries, so makepkg finds them instead of fetching.
cp "${binaries}/ninlil-linux-amd64" "${package}/ninlil-${pkgver}-x86_64"
cp "${binaries}/ninlil-linux-arm64" "${package}/ninlil-${pkgver}-aarch64"

# makepkg refuses to run as root, so it runs as an unprivileged user in the container, building
# from a writable copy of the package directory into a world-writable output directory. -d skips
# the (runtime-only) dependency check, and a per-architecture makepkg.conf sets CARCH.
output="${work}/output"
mkdir -p "${output}"
chmod 0755 "${work}" "${package}"
chmod 0644 "${package}"/*
chmod 0777 "${output}"
docker run --rm --user 65534:65534 --env HOME=/tmp \
    --volume "${package}:/source:ro" \
    --volume "${output}:/output" \
    docker.io/library/archlinux:base-devel \
    sh -euc '
        cp -r /source /tmp/package
        cd /tmp/package
        makepkg --printsrcinfo > /output/.SRCINFO
        for arch in x86_64 aarch64; do
            sed "s/^CARCH=.*/CARCH=\"${arch}\"/" /etc/makepkg.conf > "/tmp/makepkg-${arch}.conf"
            PKGDEST=/output makepkg --config "/tmp/makepkg-${arch}.conf" \
                --nodeps --force --noconfirm
        done
    '

cp "${output}"/ninlil-bin-*.pkg.tar.zst "${binaries}/"
ls -l "${binaries}"/ninlil-bin-*.pkg.tar.zst
