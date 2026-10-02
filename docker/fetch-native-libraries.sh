#!/bin/sh
# Fetches the native shared libraries the Ninlil container image needs on top of its chiseled
# .NET runtime-deps base image, for any target architecture, without running anything on that
# architecture.
#
# The runtime image has no shell and no package manager, so these libraries cannot be installed
# into it with apt. Instead, this script runs in a build stage on the build machine's
# architecture, reads each package's Debian repository index (Packages.gz, over HTTPS) for the
# *target* architecture, downloads the newest package, checks it against the SHA256 recorded in
# that index, and extracts only the shared objects into an output directory the runtime stage
# copies from. This lets one amd64 machine build both the amd64 and arm64 images with no CPU
# emulation.
#
# Libraries fetched:
#   libmsquic  -- the QUIC implementation System.Net.Quic loads for DNS-over-QUIC. Without it
#                 the DoQ transport reports QuicListener.IsSupported == false and cannot start.
#   libnuma1   -- a runtime dependency of libmsquic that the chiseled base image does not ship.
#                 (libmsquic's other dependency, OpenSSL 3, is already part of the base image.)
#
# Usage: fetch-native-libraries.sh <amd64|arm64> <output-directory>
#
# Output: the libraries under <output-directory>/usr/lib/<multiarch triplet>/. Exits non-zero for
# an unknown architecture, a package missing from its index, a failed download or a checksum
# mismatch. Needs curl, gzip, awk, sort -V, sha256sum and dpkg-deb (buildpack-deps has them).

set -eu

target_arch="$1"
output_directory="$2"

case "${target_arch}" in
    amd64)
        ubuntu_repository='https://archive.ubuntu.com/ubuntu'
        library_directory='usr/lib/x86_64-linux-gnu'
        ;;
    arm64)
        ubuntu_repository='https://ports.ubuntu.com/ubuntu-ports'
        library_directory='usr/lib/aarch64-linux-gnu'
        ;;
    *)
        echo "Unsupported target architecture: ${target_arch}" >&2
        exit 1
        ;;
esac

microsoft_repository='https://packages.microsoft.com/ubuntu/24.04/prod'
work_directory="$(mktemp -d)"
trap 'rm -rf "${work_directory}"' EXIT
mkdir -p "${output_directory}/${library_directory}"

# fetch_package <repository-base-url> <suite> <package-name>
#
# Downloads the Packages index for the suite/architecture, picks the stanza with the highest
# version of the named package (an index can list several), downloads that .deb, checks its
# SHA256 against the index, and extracts every shared object it contains into the output
# directory under the architecture's multiarch library path.
fetch_package() {
    repository="$1"
    suite="$2"
    package="$3"

    curl --fail --silent --show-error --location \
        "${repository}/dists/${suite}/main/binary-${target_arch}/Packages.gz" \
        | gzip -d > "${work_directory}/Packages"

    # Emit "<version> <filename> <sha256>" for each stanza of this package, then keep the
    # highest version.
    selected="$(awk -v wanted="${package}" '
        /^Package: / { name = $2 }
        /^Version: / { version = $2 }
        /^Filename: / { filename = $2 }
        /^SHA256: / { sha = $2 }
        /^$/ {
            if (name == wanted) print version, filename, sha
            name = ""; version = ""; filename = ""; sha = ""
        }
        END { if (name == wanted) print version, filename, sha }
    ' "${work_directory}/Packages" | sort -V | tail -n 1)"

    if [ -z "${selected}" ]; then
        echo "Package ${package} not found for ${target_arch} in ${repository}" >&2
        exit 1
    fi

    version="$(echo "${selected}" | cut -d ' ' -f 1)"
    filename="$(echo "${selected}" | cut -d ' ' -f 2)"
    expected_sha="$(echo "${selected}" | cut -d ' ' -f 3)"
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

fetch_package "${microsoft_repository}" 'noble' 'libmsquic'
fetch_package "${ubuntu_repository}" 'noble' 'libnuma1'

ls -l "${output_directory}/${library_directory}"
