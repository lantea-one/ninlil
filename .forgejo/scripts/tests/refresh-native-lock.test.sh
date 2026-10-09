#!/usr/bin/env bash
# Tests refresh-native-lock.sh against fixture Packages indexes served through file:// URLs.
#
# Covers: the highest Debian version wins across noble, noble-updates and noble-security, where
# plain string or sort -V order would pick another one (an epoch "1:1.0" beats "2.0", "2.0" beats
# "2.0~rc1", "2.10.0" beats "2.9.0"); a package that only another package's stanza mentions is
# ignored; amd64 reads the archive and arm64 reads the ports mirror; the old and new versions are
# reported; a missing package or index exits non-zero and writes no output file.
#
# Usage: .forgejo/scripts/tests/refresh-native-lock.test.sh
# Needs: bash, curl, gzip, awk and dpkg (Debian or Ubuntu; lint.sh runs it in the lint
# container). Never contacts a real server.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scripts="$(cd "${here}/.." && pwd)"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

if ! command -v dpkg > /dev/null; then
    echo 'refresh-native-lock.test.sh needs dpkg; run it through in-container.sh lint.' >&2
    exit 1
fi

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

hex() { printf '%064d' "$1" | tr '0' "$2"; }

# Writes one Packages.gz. Arguments: repository directory, suite, architecture, then stanzas as
# "package:version" pairs (the file name and sum derive from them).
write_index() {
    local repository="$1" suite="$2" arch="$3" entry package version digit
    shift 3
    mkdir -p "${repository}/dists/${suite}/main/binary-${arch}"
    : > "${repository}/dists/${suite}/main/binary-${arch}/Packages"
    for entry in "$@"; do
        package="${entry%%:*}"
        version="${entry#*:}"
        digit=$(((${#version} + ${#suite}) % 10))
        printf 'Package: %s\nVersion: %s\nArchitecture: %s\nFilename: pool/main/%s_%s_%s.deb\nSHA256: %s\n\n' \
            "${package}" "${version}" "${arch}" "${package}" "${version//:/%3a}" "${arch}" "$(hex "${digit}" a)" \
            >> "${repository}/dists/${suite}/main/binary-${arch}/Packages"
    done
    gzip -f "${repository}/dists/${suite}/main/binary-${arch}/Packages"
}

ubuntu="${work}/ubuntu"
ports="${work}/ports"
microsoft="${work}/microsoft"

# libnuma1 amd64: the epoch version sits in noble-security and must win over 2.0 and 2.0~rc1.
write_index "${ubuntu}" noble amd64 libnuma1:2.0
write_index "${ubuntu}" noble-updates amd64 libnuma1:2.0~rc1 libnuma-dev:9.9
write_index "${ubuntu}" noble-security amd64 libnuma1:1:1.0
# libnuma1 arm64: noble has the pre-release, noble-updates has the release.
write_index "${ports}" noble arm64 libnuma1:2.0~rc1
write_index "${ports}" noble-updates arm64 libnuma1:2.0
write_index "${ports}" noble-security arm64 libnuma-dev:9.9
# libmsquic: 2.10.0 beats 2.9.0 and 2.4.0 (a numeric, not a string, comparison).
write_index "${microsoft}" noble amd64 libmsquic:2.4.0 libmsquic:2.10.0 libmsquic:2.9.0
write_index "${microsoft}" noble arm64 libmsquic:2.10.0

printf '%s\t%s\t%s\n' amd64 libnuma1 0.9 amd64 libmsquic 2.4.0 > "${work}/current.lock"

# Runs the script. Output goes to $output, the exit code to $status.
run_refresh() {
    output="$(env NATIVE_LOCK_UBUNTU_URL="file://${ubuntu}" NATIVE_LOCK_UBUNTU_PORTS_URL="file://${ports}" \
        NATIVE_LOCK_MICROSOFT_URL="file://${microsoft}" NATIVE_LOCK_CURRENT="${work}/current.lock" \
        bash "${scripts}/refresh-native-lock.sh" "${work}/new.lock" 2>&1)"
    status=$?
}

# Prints the version column of the lock entry for an architecture and package.
lock_version() { awk -F '\t' -v arch="$1" -v package="$2" '$1 == arch && $2 == package { print $3 }' "${work}/new.lock"; }

run_refresh
assert_eq 'refresh: exits 0 with complete indexes' 0 "${status}"
assert_eq 'refresh: the epoch version from noble-security wins (amd64 libnuma1)' '1:1.0' "$(lock_version amd64 libnuma1)"
assert_eq 'refresh: 2.0 beats 2.0~rc1 from noble-updates (arm64 libnuma1)' '2.0' "$(lock_version arm64 libnuma1)"
assert_eq 'refresh: 2.10.0 beats 2.9.0 (amd64 libmsquic)' '2.10.0' "$(lock_version amd64 libmsquic)"
assert_eq 'refresh: arm64 libmsquic is present' '2.10.0' "$(lock_version arm64 libmsquic)"
assert_eq 'refresh: four entries and a comment header' 4 "$(grep -vc '^#' "${work}/new.lock")"
assert_contains 'refresh: amd64 libnuma1 comes from the archive' "$(cat "${work}/new.lock")" "libnuma1	1:1.0	file://${ubuntu}	pool/main/libnuma1_1%3a1.0_amd64.deb"
assert_contains 'refresh: arm64 libnuma1 comes from the ports mirror' "$(cat "${work}/new.lock")" "file://${ports}	pool/main/libnuma1_2.0_arm64.deb"
assert_contains 'refresh: reports the old and new version' "${output}" 'libnuma1 amd64: 0.9 -> 1:1.0'
assert_contains 'refresh: reports a package new to the lock' "${output}" 'libnuma1 arm64: none -> 2.0'
assert_eq 'refresh: every entry has six tab-separated columns' 0 "$(grep -v '^#' "${work}/new.lock" | awk -F '\t' 'NF != 6' | wc -l)"
assert_eq 'refresh: every sum is 64 hex digits' 4 "$(grep -cE '	[0-9a-f]{64}$' "${work}/new.lock")"

# A package missing from an index fails and writes nothing.
rm -f "${work}/new.lock"
write_index "${microsoft}" noble arm64 libother:1.0
run_refresh
assert_eq 'refresh: a missing package exits 1' 1 "${status}"
assert_contains 'refresh: the failure names the package' "${output}" 'Package libmsquic not found for arm64'
assert_eq 'refresh: a failure writes no output file' false "$([[ -e "${work}/new.lock" ]] && echo true || echo false)"

# A missing index fails too.
write_index "${microsoft}" noble arm64 libmsquic:2.10.0
rm "${ports}/dists/noble-updates/main/binary-arm64/Packages.gz"
run_refresh
assert_eq 'refresh: a missing index exits non-zero' nonzero "$([[ "${status}" -ne 0 ]] && echo nonzero || echo zero)"
assert_eq 'refresh: a missing index writes no output file' false "$([[ -e "${work}/new.lock" ]] && echo true || echo false)"

finish
