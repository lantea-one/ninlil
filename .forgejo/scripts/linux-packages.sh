#!/usr/bin/env bash
# Builds the ninlil Deb and RPM packages for one pipeline run, for amd64/x86_64 and
# arm64/aarch64, into <binaries-directory> so the pipeline attaches them to the release.
#
# The packages only copy files (the Linux executable, the systemd unit, the sysusers entry and the
# environment file shared with the Arch packages, and small install scripts), so nFPM builds all
# four from one amd64 run with no emulation. src/linux-packages/nfpm.yaml describes the contents.
# The pipeline runs this script in the packages toolchain container
# (in-container.sh packages ...).
#
# Usage: linux-packages.sh <binaries-directory>
#   The directory must hold ninlil-linux-amd64 and ninlil-linux-arm64.
#
# Environment (compute-version.sh outputs):
#   DEB_VERSION  the Debian version (deb-version), e.g. 2026.10.09.1 or 2026.10.09.3~develop.a1b2c3d
#   RPM_VERSION  the RPM version (rpm-version), e.g. 2026.10.09.1
#   RPM_RELEASE  the RPM release (rpm-release), e.g. 1 or 0.develop.a1b2c3d
#
# Output, in <binaries-directory>:
#   ninlil_<DEB_VERSION>_amd64.deb         ninlil_<DEB_VERSION>_arm64.deb
#   ninlil-<RPM_VERSION>-<RPM_RELEASE>.x86_64.rpm   ninlil-<RPM_VERSION>-<RPM_RELEASE>.aarch64.rpm
# Exits non-zero when an input is missing or nFPM fails.

set -euo pipefail
shopt -s inherit_errexit

binaries="${1:?usage: linux-packages.sh <binaries-directory>}"
: "${DEB_VERSION:?DEB_VERSION is required}"
: "${RPM_VERSION:?RPM_VERSION is required}"
: "${RPM_RELEASE:?RPM_RELEASE is required}"

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
binaries="$(cd "${binaries}" && pwd)"

for binary in ninlil-linux-amd64 ninlil-linux-arm64; do
    if [[ ! -f "${binaries}/${binary}" ]]; then
        echo "Missing ${binaries}/${binary}." >&2
        exit 1
    fi
done

export NINLIL_COMMON="${repository_root}/src/archlinux/common"
export NINLIL_LICENSE="${repository_root}/LICENSE"
config="src/linux-packages/nfpm.yaml"
cd "${repository_root}"

# Builds one package. Arguments: nFPM architecture, binary, format, version, release, target file.
build() {
    local arch="$1" binary="$2" format="$3" version="$4" release="$5" target="$6"
    NFPM_ARCH="${arch}" NINLIL_BINARY="${binaries}/${binary}" PKG_VERSION="${version}" PKG_RELEASE="${release}" \
        nfpm package --config "${config}" --packager "${format}" --target "${target}"
}

build amd64 ninlil-linux-amd64 deb "${DEB_VERSION}" '' "${binaries}/ninlil_${DEB_VERSION}_amd64.deb"
build arm64 ninlil-linux-arm64 deb "${DEB_VERSION}" '' "${binaries}/ninlil_${DEB_VERSION}_arm64.deb"
build amd64 ninlil-linux-amd64 rpm "${RPM_VERSION}" "${RPM_RELEASE}" "${binaries}/ninlil-${RPM_VERSION}-${RPM_RELEASE}.x86_64.rpm"
build arm64 ninlil-linux-arm64 rpm "${RPM_VERSION}" "${RPM_RELEASE}" "${binaries}/ninlil-${RPM_VERSION}-${RPM_RELEASE}.aarch64.rpm"

ls -l "${binaries}"/ninlil_*.deb "${binaries}"/ninlil-*.rpm
