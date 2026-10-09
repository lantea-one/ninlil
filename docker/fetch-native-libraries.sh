#!/bin/sh
# Fetches the native shared libraries the Ninlil container image needs on top of its chiseled
# .NET runtime-deps base image, for any target architecture, without running anything on that
# architecture.
#
# The runtime image has no shell and no package manager, so these libraries cannot be installed
# into it with apt. Instead, this script runs in a build stage on the build machine's
# architecture, looks the exact package up in docker/native-libraries.lock, downloads that one
# file, checks it against the SHA-256 committed in the lock, and extracts only the shared objects
# into an output directory the runtime stage copies from. This lets one amd64 machine build both
# the amd64 and arm64 images with no CPU emulation.
#
# The lock is the trust anchor: its versions and sums are reviewed in git (and refreshed by
# .forgejo/scripts/refresh-native-lock.sh through bump-pins.sh), so a changed or tampered file on
# a mirror fails the build instead of reaching the image. That is why no GPG check is needed. The
# index of the repository is not read at all.
#
# Libraries fetched:
#   libmsquic  -- the QUIC implementation System.Net.Quic loads for DNS-over-QUIC. Without it
#                 the DoQ transport reports QuicListener.IsSupported == false and cannot start.
#   libnuma1   -- a runtime dependency of libmsquic that the chiseled base image does not ship.
#                 (libmsquic's other dependency, OpenSSL 3, is already part of the base image.)
#
# Usage: fetch-native-libraries.sh <amd64|arm64> <output-directory>
#
# Environment: NATIVE_LIBRARIES_LOCK is the lock file (default: native-libraries.lock next to
# this script; the self-test points it at a fixture).
#
# Output: the libraries under <output-directory>/usr/lib/<multiarch triplet>/. Exits non-zero for
# an unknown architecture, a package missing from the lock for that architecture, a failed
# download or a checksum mismatch; nothing is extracted in the last two cases. Needs curl,
# awk, sha256sum and dpkg-deb (buildpack-deps has them).

set -eu

target_arch="$1"
output_directory="$2"
lock_file="${NATIVE_LIBRARIES_LOCK:-$(dirname "$0")/native-libraries.lock}"

case "${target_arch}" in
    amd64) library_directory='usr/lib/x86_64-linux-gnu' ;;
    arm64) library_directory='usr/lib/aarch64-linux-gnu' ;;
    *)
        echo "Unsupported target architecture: ${target_arch}" >&2
        exit 1
        ;;
esac

work_directory="$(mktemp -d)"
trap 'rm -rf "${work_directory}"' EXIT
mkdir -p "${output_directory}/${library_directory}"

# fetch_package <package-name>
#
# Finds the package's line for the target architecture in the lock, downloads the file, checks
# its SHA-256 against the lock, and extracts every shared object it contains into the output
# directory under the architecture's multiarch library path.
fetch_package() {
    package="$1"

    # Lock columns (tab-separated): arch, package, version, repository URL, file name, sha256.
    selected="$(awk -F '\t' -v arch="${target_arch}" -v wanted="${package}" '
        $1 == arch && $2 == wanted { print $3 "\t" $4 "\t" $5 "\t" $6 }
    ' "${lock_file}")"

    if [ -z "${selected}" ]; then
        echo "Package ${package} has no ${target_arch} entry in ${lock_file}" >&2
        exit 1
    fi

    version="$(echo "${selected}" | cut -f 1)"
    repository="$(echo "${selected}" | cut -f 2)"
    filename="$(echo "${selected}" | cut -f 3)"
    expected_sha="$(echo "${selected}" | cut -f 4)"
    echo "Fetching ${package} ${version} (${target_arch})"

    curl --fail --silent --show-error --location \
        "${repository}/${filename}" -o "${work_directory}/${package}.deb"
    echo "${expected_sha}  ${work_directory}/${package}.deb" | sha256sum -c -

    rm -rf "${work_directory}/extract"
    dpkg-deb -x "${work_directory}/${package}.deb" "${work_directory}/extract"
    # -a keeps the soname symlinks (libmsquic.so.2 -> libmsquic.so.2.x.y) intact. The LTTng
    # tracing build of libmsquic is skipped: nothing loads it, and it would need LTTng itself.
    for library in "${work_directory}/extract/${library_directory}/"*.so*; do
        case "${library}" in
            *lttng*) continue ;;
        esac
        cp -a "${library}" "${output_directory}/${library_directory}/"
    done
}

fetch_package 'libmsquic'
fetch_package 'libnuma1'

ls -l "${output_directory}/${library_directory}"
