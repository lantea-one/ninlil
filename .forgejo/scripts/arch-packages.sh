#!/usr/bin/env bash
# Builds the ninlil Arch Linux packages for one pipeline run, for x86_64 and aarch64, and copies
# them into <binaries-directory> so the pipeline attaches them to the release. Each package
# installs directly with `pacman -U <url>`.
#
# The PKGBUILD is generated from src/archlinux/ninlil/PKGBUILD.in with this build's version,
# download URLs and SHA256 sums filled in. The release those URLs point at is published only
# after this step, so the packages are built from the binaries this run just produced, and
# makepkg still checks them against the sums. The package only copies files, so one x86_64
# makepkg builds both architectures. The pipeline runs this script in the arch toolchain
# container (in-container.sh arch ...).
#
# Usage: arch-packages.sh <binaries-directory>
#   The directory must hold ninlil-linux-amd64 and ninlil-linux-arm64.
#
# Environment:
#   RELEASE_TAG   the release's tag (the computed version)
#   ARCH_VERSION  the package's pkgver (compute-version.sh's arch-version)
#
# Output: ninlil-<pkgver>-1-x86_64.pkg.tar.zst and ninlil-<pkgver>-1-aarch64.pkg.tar.zst in
# <binaries-directory>. Exits non-zero when a placeholder is left unfilled or makepkg fails.

set -euo pipefail
shopt -s inherit_errexit

: "${RELEASE_TAG:?RELEASE_TAG is required}"
binaries="$1"

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
template="${repository_root}/src/archlinux/ninlil/PKGBUILD.in"
common="${repository_root}/src/archlinux/common"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
package="${work}/ninlil"
mkdir -p "${package}"

# The pkgver comes from compute-version.sh, with every other form of the version.
pkgver="${ARCH_VERSION:?ARCH_VERSION is required}"

sha() { sha256sum "$1" | cut -d ' ' -f 1; }

cp "${common}/ninlil.service" "${common}/ninlil.sysusers" "${common}/ninlil.env" "${package}/"
cp "${repository_root}/LICENSE" "${package}/"

# The tag goes into a download URL path, where "+" must be percent-encoded.
tag_path="${RELEASE_TAG//+/%2B}"

# Each sum is computed on its own line: a failing sha256sum inside a sed argument would not stop
# the script and would leave an empty sum behind.
sha_service="$(sha "${package}/ninlil.service")"
sha_sysusers="$(sha "${package}/ninlil.sysusers")"
sha_env="$(sha "${package}/ninlil.env")"
sha_license="$(sha "${package}/LICENSE")"
sha_x86_64="$(sha "${binaries}/ninlil-linux-amd64")"
sha_aarch64="$(sha "${binaries}/ninlil-linux-arm64")"

sed \
    -e "s|@VERSION@|${RELEASE_TAG}|g" \
    -e "s|@PKGVER@|${pkgver}|g" \
    -e "s|@TAG_PATH@|${tag_path}|g" \
    -e "s|@SHA256_SERVICE@|${sha_service}|g" \
    -e "s|@SHA256_SYSUSERS@|${sha_sysusers}|g" \
    -e "s|@SHA256_ENV@|${sha_env}|g" \
    -e "s|@SHA256_LICENSE@|${sha_license}|g" \
    -e "s|@SHA256_X86_64@|${sha_x86_64}|g" \
    -e "s|@SHA256_AARCH64@|${sha_aarch64}|g" \
    "${template}" > "${package}/PKGBUILD"

if grep -n '@[A-Z0-9_]*@' "${package}/PKGBUILD"; then
    echo "Unfilled placeholders remain in the generated PKGBUILD." >&2
    exit 1
fi

# Copy the binaries under the names the PKGBUILD downloads them as, so makepkg finds them in
# SRCDEST and does not fetch them.
cp "${binaries}/ninlil-linux-amd64" "${package}/ninlil-${pkgver}-x86_64"
cp "${binaries}/ninlil-linux-arm64" "${package}/ninlil-${pkgver}-aarch64"

# --nodeps skips the dependency check (the package has runtime dependencies only), and a
# per-architecture copy of makepkg.conf sets CARCH, so an x86_64 makepkg builds the aarch64
# package too.
output="${work}/output"
mkdir -p "${output}"
(
    cd "${package}"
    makepkg --printsrcinfo > "${output}/.SRCINFO"
    for arch in x86_64 aarch64; do
        sed "s/^CARCH=.*/CARCH=\"${arch}\"/" /etc/makepkg.conf > "${work}/makepkg-${arch}.conf"
        PKGDEST="${output}" BUILDDIR="${work}/build" SRCDEST="${package}" \
            makepkg --config "${work}/makepkg-${arch}.conf" --nodeps --force --noconfirm
    done
)

cp "${output}"/ninlil-*.pkg.tar.zst "${binaries}/"
ls -l "${binaries}"/ninlil-*.pkg.tar.zst
