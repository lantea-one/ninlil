#!/usr/bin/env bash
# Tests docker/fetch-native-libraries.sh against a fixture lock and fake .deb packages served
# through file:// URLs (the script uses curl, which treats them like any repository).
#
# Covers: with matching sums the shared objects are extracted under the right multiarch path, the
# soname symlink is kept and the LTTng build is skipped; a wrong sum exits non-zero, names the
# failing file and extracts nothing; a package missing from the lock for the target architecture,
# an unknown architecture and an unreachable file also exit non-zero; the other architecture's
# lock entries are never used.
#
# Usage: .forgejo/scripts/tests/fetch-native-libraries.test.sh
# Needs: bash, curl, awk, sha256sum and dpkg-deb (Debian or Ubuntu; lint.sh runs it in the lint
# container). Never contacts a real server.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd "${here}/../../.." && pwd)"
fetch="${repository_root}/docker/fetch-native-libraries.sh"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

if ! command -v dpkg-deb > /dev/null; then
    echo 'fetch-native-libraries.test.sh needs dpkg-deb; run it through in-container.sh lint.' >&2
    exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
repository="${work}/repository"
mkdir -p "${repository}/pool"

# Builds a fake package. Arguments: name, multiarch directory, then library file names; a name
# ending in ".2" becomes a symlink to the same name plus ".6.2".
build_deb() {
    local name="$1" directory="$2" root file
    shift 2
    root="${work}/build-${name}-${directory//\//_}"
    mkdir -p "${root}/${directory}" "${root}/DEBIAN"
    printf 'Package: %s\nVersion: 1.0\nArchitecture: all\nMaintainer: test <t@t>\nDescription: fixture\n' "${name}" > "${root}/DEBIAN/control"
    for file in "$@"; do
        if [[ "${file}" == *.2 ]]; then
            ln -s "${file}.6.2" "${root}/${directory}/${file}"
        else
            printf 'library %s' "${file}" > "${root}/${directory}/${file}"
        fi
    done
    dpkg-deb --root-owner-group --build "${root}" "${repository}/pool/${name}_$(basename "${directory}").deb" > /dev/null
}

build_deb libmsquic usr/lib/x86_64-linux-gnu libmsquic.so.2.6.2 libmsquic.so.2 libmsquic-lttng.so.2.6.2
build_deb libnuma1 usr/lib/x86_64-linux-gnu libnuma.so.1.0.0
build_deb libmsquic usr/lib/aarch64-linux-gnu libmsquic.so.2.6.2 libmsquic.so.2
build_deb libnuma1 usr/lib/aarch64-linux-gnu libnuma.so.1.0.0

sum_of() { local s; s="$(sha256sum "${repository}/pool/$1")"; printf '%s' "${s%% *}"; }

# Writes the lock. Arguments: output file, then optionally a package name whose amd64 sum is wrong.
write_lock() {
    local lock="$1" wrong="${2:-}" package sum
    {
        echo '# fixture lock'
        for arch in amd64 arm64; do
            for package in libmsquic libnuma1; do
                if [[ "${arch}" == 'amd64' ]]; then directory='x86_64-linux-gnu'; else directory='aarch64-linux-gnu'; fi
                sum="$(sum_of "${package}_${directory}.deb")"
                if [[ "${package}" == "${wrong}" && "${arch}" == 'amd64' ]]; then sum="$(printf '%064d' 0)"; fi
                printf '%s\t%s\t1.0\tfile://%s\tpool/%s_%s.deb\t%s\n' "${arch}" "${package}" "${repository}" "${package}" "${directory}" "${sum}"
            done
        done
    } > "${lock}"
}

# Runs the fetch script. Arguments: lock, architecture, output directory. Output goes to $output,
# the exit code to $status.
run_fetch() {
    output="$(env NATIVE_LIBRARIES_LOCK="$1" sh "${fetch}" "$2" "$3" 2>&1)"
    status=$?
}

listing() { (cd "$1" && find . -type f -o -type l | sort | tr '\n' ' '); }

# --- Matching sums -----------------------------------------------------------------------------
write_lock "${work}/good.lock"
run_fetch "${work}/good.lock" amd64 "${work}/out-amd64"
assert_eq 'fetch: matching sums exit 0' 0 "${status}"
assert_eq 'fetch: amd64 extracts the libraries, the symlink and no LTTng build' \
    './usr/lib/x86_64-linux-gnu/libmsquic.so.2 ./usr/lib/x86_64-linux-gnu/libmsquic.so.2.6.2 ./usr/lib/x86_64-linux-gnu/libnuma.so.1.0.0 ' \
    "$(listing "${work}/out-amd64")"
assert_eq 'fetch: the soname symlink points at the versioned file' 'libmsquic.so.2.6.2' "$(readlink "${work}/out-amd64/usr/lib/x86_64-linux-gnu/libmsquic.so.2")"
run_fetch "${work}/good.lock" arm64 "${work}/out-arm64"
assert_eq 'fetch: arm64 uses its own entries and multiarch path' 0 "${status}"
assert_eq 'fetch: arm64 output holds only aarch64 libraries' \
    './usr/lib/aarch64-linux-gnu/libmsquic.so.2 ./usr/lib/aarch64-linux-gnu/libmsquic.so.2.6.2 ./usr/lib/aarch64-linux-gnu/libnuma.so.1.0.0 ' \
    "$(listing "${work}/out-arm64")"

# --- A wrong sum -------------------------------------------------------------------------------
write_lock "${work}/bad-msquic.lock" libmsquic
run_fetch "${work}/bad-msquic.lock" amd64 "${work}/out-bad"
assert_eq 'fetch: a wrong libmsquic sum exits non-zero' nonzero "$([[ "${status}" -ne 0 ]] && echo nonzero || echo zero)"
assert_contains 'fetch: the failure names the file that did not match' "${output}" 'FAILED'
assert_eq 'fetch: a wrong sum extracts nothing' '' "$(listing "${work}/out-bad")"
write_lock "${work}/bad-numa.lock" libnuma1
run_fetch "${work}/bad-numa.lock" amd64 "${work}/out-bad2"
assert_eq 'fetch: a wrong libnuma1 sum exits non-zero' nonzero "$([[ "${status}" -ne 0 ]] && echo nonzero || echo zero)"
assert_eq 'fetch: the arm64 build ignores a wrong amd64 sum' 0 "$(run_fetch "${work}/bad-numa.lock" arm64 "${work}/out-ok" && echo "${status}")"

# --- A package missing from the lock -----------------------------------------------------------
grep -v $'^amd64\tlibnuma1\t' "${work}/good.lock" > "${work}/missing.lock"
run_fetch "${work}/missing.lock" amd64 "${work}/out-missing"
assert_eq 'fetch: a package missing for the architecture exits non-zero' nonzero "$([[ "${status}" -ne 0 ]] && echo nonzero || echo zero)"
assert_contains 'fetch: the failure names the package and architecture' "${output}" 'Package libnuma1 has no amd64 entry'

# --- Other failures ----------------------------------------------------------------------------
run_fetch "${work}/good.lock" riscv64 "${work}/out-riscv"
assert_eq 'fetch: an unknown architecture exits 1' 1 "${status}"
assert_contains 'fetch: the unknown architecture is named' "${output}" 'Unsupported target architecture: riscv64'
sed "s#pool/libmsquic_x86_64-linux-gnu.deb#pool/gone.deb#" "${work}/good.lock" > "${work}/gone.lock"
run_fetch "${work}/gone.lock" amd64 "${work}/out-gone"
assert_eq 'fetch: an unreachable file exits non-zero' nonzero "$([[ "${status}" -ne 0 ]] && echo nonzero || echo zero)"

finish
