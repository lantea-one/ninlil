#!/usr/bin/env bash
# Computes a fresh docker/native-libraries.lock: the exact libmsquic and libnuma1 packages the
# container image (Dockerfile, docker/fetch-native-libraries.sh) installs, per architecture.
#
# For every architecture and package it reads the Packages.gz index of each source below, takes
# every stanza of that package, and keeps the one with the highest Debian version. Versions are
# compared with `dpkg --compare-versions`, which knows epochs ("1:1.0" beats "2.0") and the tilde
# ("2.0~rc1" sorts before "2.0"); sort -V gets both wrong. The sources are:
#   libmsquic  Microsoft's Ubuntu 24.04 repository, suite noble (both architectures)
#   libnuma1   Ubuntu noble, noble-updates and noble-security (archive.ubuntu.com for amd64,
#              ports.ubuntu.com for arm64), so a security fix is picked up
# The index is only used to find the file name and its SHA-256. The lock then pins that sum in
# git, so the image build trusts the reviewed lock, not whatever the server answers later.
#
# The script only writes the file named by its argument; bump-pins.sh decides whether to replace
# the committed lock with it. It needs dpkg, so bump-pins.sh runs it in the lint toolchain:
#   .forgejo/scripts/in-container.sh lint .forgejo/scripts/refresh-native-lock.sh <output>
#
# Usage: refresh-native-lock.sh <output-file>   (relative paths are relative to the repository root)
#
# Environment (all optional; the self-test points them at fixture directories):
#   NATIVE_LOCK_UBUNTU_URL        Ubuntu archive for amd64 (default https://archive.ubuntu.com/ubuntu)
#   NATIVE_LOCK_UBUNTU_PORTS_URL  Ubuntu ports for arm64 (default https://ports.ubuntu.com/ubuntu-ports)
#   NATIVE_LOCK_MICROSOFT_URL     Microsoft repository (default https://packages.microsoft.com/ubuntu/24.04/prod)
#   NATIVE_LOCK_CURRENT           the lock to compare with, for the report (default docker/native-libraries.lock)
#
# Output: the new lock in <output-file> (tab-separated: arch, package, version, repository URL,
# file name, sha256) and one "lock  <package> <arch>: <old version> -> <new version>" line per
# entry on stdout. Exits non-zero, writing nothing, when a package is missing or a download fails.

set -euo pipefail
shopt -s inherit_errexit

output="${1:?usage: refresh-native-lock.sh <output-file>}"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repository_root}"

ubuntu_url="${NATIVE_LOCK_UBUNTU_URL:-https://archive.ubuntu.com/ubuntu}"
ports_url="${NATIVE_LOCK_UBUNTU_PORTS_URL:-https://ports.ubuntu.com/ubuntu-ports}"
microsoft_url="${NATIVE_LOCK_MICROSOFT_URL:-https://packages.microsoft.com/ubuntu/24.04/prod}"
current="${NATIVE_LOCK_CURRENT:-docker/native-libraries.lock}"

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

# Prints "<version>\t<file name>\t<sha256>" for each stanza of a package in one index.
# Arguments: repository URL, suite, architecture, package.
list_stanzas() {
    local url="$1" suite="$2" arch="$3" package="$4"
    curl --fail --silent --show-error --location \
        "${url}/dists/${suite}/main/binary-${arch}/Packages.gz" | gzip -d | awk -v wanted="${package}" '
        /^Package: / { name = $2 }
        /^Version: / { version = $2 }
        /^Filename: / { filename = $2 }
        /^SHA256: / { sha = $2 }
        /^$/ {
            if (name == wanted) printf "%s\t%s\t%s\n", version, filename, sha
            name = ""; version = ""; filename = ""; sha = ""
        }
        END { if (name == wanted) printf "%s\t%s\t%s\n", version, filename, sha }
    '
}

# Prints the lock line for the highest version of a package across the given suites.
# Arguments: repository URL, architecture, package, suite...
best_entry() {
    local url="$1" arch="$2" package="$3" suite stanza version filename sha
    local best_version='' best_filename='' best_sha=''
    shift 3
    for suite in "$@"; do
        # Not a process substitution: its failure would not stop the script.
        stanza="$(list_stanzas "${url}" "${suite}" "${arch}" "${package}")"
        while IFS=$'\t' read -r version filename sha; do
            [[ -n "${version}" ]] || continue
            if [[ -z "${best_version}" ]] || dpkg --compare-versions "${version}" gt "${best_version}"; then
                best_version="${version}"
                best_filename="${filename}"
                best_sha="${sha}"
            fi
        done <<< "${stanza}"
    done
    if [[ -z "${best_version}" ]]; then
        echo "Package ${package} not found for ${arch} in ${url} (suites: $*)." >&2
        return 1
    fi
    if [[ ! "${best_sha}" =~ ^[0-9a-f]{64}$ ]]; then
        echo "Package ${package} ${best_version} (${arch}) has no usable SHA256 in the index." >&2
        return 1
    fi
    printf '%s\t%s\t%s\t%s\t%s\t%s\n' "${arch}" "${package}" "${best_version}" "${url}" "${best_filename}" "${best_sha}"
}

# Prints the version a package had in the current lock, or "none".
old_version() {
    local found=''
    if [[ -f "${current}" ]]; then
        found="$(awk -F '\t' -v arch="$1" -v package="$2" '$1 == arch && $2 == package { print $3 }' "${current}")"
    fi
    printf '%s' "${found:-none}"
}

{
    cat <<'HEADER'
# Native libraries the container image installs, pinned by version and SHA-256.
# Read by docker/fetch-native-libraries.sh; rewritten by .forgejo/scripts/refresh-native-lock.sh
# (run weekly by bump-pins.sh). Do not edit by hand. Columns, separated by one tab:
# architecture, package, version, repository URL, file name below it, SHA-256 of the file.
HEADER
} > "${work}/lock"

for arch in amd64 arm64; do
    if [[ "${arch}" == 'amd64' ]]; then
        ubuntu="${ubuntu_url}"
    else
        ubuntu="${ports_url}"
    fi
    for entry in "libmsquic|${microsoft_url}|noble" "libnuma1|${ubuntu}|noble noble-updates noble-security"; do
        IFS='|' read -r package url suites <<< "${entry}"
        # shellcheck disable=SC2086  # the suite list is split on purpose
        line="$(best_entry "${url}" "${arch}" "${package}" ${suites})"
        printf '%s\n' "${line}" >> "${work}/lock"
        new_version="$(cut -f 3 <<< "${line}")"
        echo "lock      ${package} ${arch}: $(old_version "${arch}" "${package}") -> ${new_version}"
    done
done

cp "${work}/lock" "${output}"
