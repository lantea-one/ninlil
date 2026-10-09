#!/usr/bin/env bash
# Tests linux-packages.sh.
#
# Covers with a fake nfpm first on PATH (it logs the packager, target, version and release it was
# given): four packages are requested with the right names, formats, architectures and versions;
# a missing arm64 binary or a missing version variable stops the script before nfpm runs.
# When a real nfpm and dpkg-deb are on PATH (the packages toolchain has both), it also builds the
# amd64 Deb from a stub binary and checks its name, version, dependencies, conffile, script
# presence and file list. The lint toolchain has no nfpm, so there that part is reported as
# skipped.
#
# Usage: .forgejo/scripts/tests/linux-packages.test.sh
# Needs: bash. Writes only to a temporary directory it removes on exit.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$(cd "${here}/.." && pwd)/linux-packages.sh"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

work_root="$(mktemp -d)"
trap 'rm -rf "${work_root}"' EXIT
mkdir -p "${work_root}/bin" "${work_root}/binaries"
log="${work_root}/nfpm.log"
cat > "${work_root}/bin/nfpm" << 'FAKE'
#!/usr/bin/env bash
# Records one line per call and creates the target file, like the real nfpm would.
target=''
packager=''
while (($# > 0)); do
    case "$1" in
        --target) target="$2"; shift ;;
        --packager) packager="$2"; shift ;;
    esac
    shift
done
echo "${packager} ${NFPM_ARCH} ${PKG_VERSION} [${PKG_RELEASE}] $(basename "${target}") binary=$(basename "${NINLIL_BINARY}")" >> "${NFPM_LOG}"
: > "${target}"
FAKE
chmod +x "${work_root}/bin/nfpm"
printf 'amd64' > "${work_root}/binaries/ninlil-linux-amd64"
printf 'arm64' > "${work_root}/binaries/ninlil-linux-arm64"

versions=(DEB_VERSION='2026.10.09.3~develop.a1b2c3d' RPM_VERSION=2026.10.09.3 RPM_RELEASE=0.develop.a1b2c3d)

output="$(PATH="${work_root}/bin:${PATH}" NFPM_LOG="${log}" env "${versions[@]}" bash "${script}" "${work_root}/binaries" 2>&1)"
assert_eq 'linux-packages: builds with the fake nfpm: exit code' 0 "$?"
assert_contains 'linux-packages: amd64 Deb' "$(cat "${log}")" 'deb amd64 2026.10.09.3~develop.a1b2c3d [] ninlil_2026.10.09.3~develop.a1b2c3d_amd64.deb binary=ninlil-linux-amd64'
assert_contains 'linux-packages: arm64 Deb' "$(cat "${log}")" 'deb arm64 2026.10.09.3~develop.a1b2c3d [] ninlil_2026.10.09.3~develop.a1b2c3d_arm64.deb binary=ninlil-linux-arm64'
assert_contains 'linux-packages: x86_64 RPM' "$(cat "${log}")" 'rpm amd64 2026.10.09.3 [0.develop.a1b2c3d] ninlil-2026.10.09.3-0.develop.a1b2c3d.x86_64.rpm binary=ninlil-linux-amd64'
assert_contains 'linux-packages: aarch64 RPM' "$(cat "${log}")" 'rpm arm64 2026.10.09.3 [0.develop.a1b2c3d] ninlil-2026.10.09.3-0.develop.a1b2c3d.aarch64.rpm binary=ninlil-linux-arm64'
assert_eq 'linux-packages: four packages land in the directory' 4 "$(find "${work_root}/binaries" -name 'ninlil[-_]*' ! -name 'ninlil-linux-*' | wc -l)"

# A missing arm64 binary must stop the script before any package is built.
rm -f "${work_root}/binaries"/ninlil_* "${work_root}/binaries"/ninlil-*.rpm "${work_root}/binaries/ninlil-linux-arm64" "${log}"
output="$(PATH="${work_root}/bin:${PATH}" NFPM_LOG="${log}" env "${versions[@]}" bash "${script}" "${work_root}/binaries" 2>&1)"
status=$?
if [[ "${status}" -ne 0 ]]; then pass 'linux-packages: a missing arm64 binary exits non-zero'; else fail 'linux-packages: a missing arm64 binary exits non-zero' "${output}"; fi
if [[ -e "${log}" ]]; then fail 'linux-packages: nfpm is not called without both binaries' "$(cat "${log}")"; else pass 'linux-packages: nfpm is not called without both binaries'; fi
printf 'arm64' > "${work_root}/binaries/ninlil-linux-arm64"

output="$(PATH="${work_root}/bin:${PATH}" NFPM_LOG="${log}" DEB_VERSION=1 RPM_VERSION=1 bash "${script}" "${work_root}/binaries" 2>&1)"
status=$?
if [[ "${status}" -ne 0 ]]; then pass 'linux-packages: a missing RPM_RELEASE exits non-zero'; else fail 'linux-packages: a missing RPM_RELEASE exits non-zero' "${output}"; fi

# The real nfpm, when present, proves nfpm.yaml itself is valid.
if command -v nfpm > /dev/null 2>&1 && command -v dpkg-deb > /dev/null 2>&1; then
    real="${work_root}/real"
    mkdir -p "${real}"
    printf 'amd64' > "${real}/ninlil-linux-amd64"
    printf 'arm64' > "${real}/ninlil-linux-arm64"
    output="$(env "${versions[@]}" bash "${script}" "${real}" 2>&1)"
    assert_eq 'linux-packages: real nfpm builds all four: exit code' 0 "$?"
    deb="${real}/ninlil_2026.10.09.3~develop.a1b2c3d_amd64.deb"
    info="$(dpkg-deb -I "${deb}" 2>&1)"
    assert_contains 'linux-packages: real Deb has the version' "${info}" 'Version: 2026.10.09.3~develop.a1b2c3d'
    assert_contains 'linux-packages: real Deb depends on ICU and OpenSSL alternatives' "${info}" 'libssl3 | libssl3t64, libicu76 | libicu74 | libicu72 | libicu70'
    assert_contains 'linux-packages: real Deb lists the environment file as a conffile' "$(dpkg-deb -I "${deb}" conffiles 2>&1)" '/etc/ninlil/ninlil.env'
    contents="$(dpkg-deb -c "${deb}" 2>&1)"
    assert_contains 'linux-packages: real Deb installs the executable' "${contents}" './usr/lib/ninlil/ninlil'
    assert_contains 'linux-packages: real Deb links the data directory' "${contents}" './usr/lib/ninlil/data -> /var/lib/ninlil'
    assert_contains 'linux-packages: real Deb installs the unit' "${contents}" './usr/lib/systemd/system/ninlil.service'
    assert_contains 'linux-packages: real Deb installs the sysusers entry' "${contents}" './usr/lib/sysusers.d/ninlil.conf'
    assert_eq 'linux-packages: real Deb carries postinst, prerm and postrm' 3 "$(dpkg-deb -e "${deb}" "${real}/control" && find "${real}/control" -name 'postinst' -o -name 'prerm' -o -name 'postrm' | wc -l)"
else
    echo 'skip - linux-packages: real nfpm and dpkg-deb are not installed here'
fi

finish
