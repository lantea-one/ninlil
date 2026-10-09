#!/usr/bin/env bash
# Tests arch-packages.sh with a stub makepkg first on PATH.
#
# Covers: a missing binary stops the script with a non-zero exit before makepkg is ever called
# (a failed checksum must not leave an empty sum in the generated PKGBUILD).
#
# Usage: .forgejo/scripts/tests/arch-packages.test.sh
# Needs: bash. Writes only to a temporary directory it removes on exit.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$(cd "${here}/.." && pwd)/arch-packages.sh"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

work_root="$(mktemp -d)"
trap 'rm -rf "${work_root}"' EXIT
mkdir -p "${work_root}/bin" "${work_root}/binaries"
marker="${work_root}/makepkg-called"
printf '#!/usr/bin/env bash\ntouch "%s"\n' "${marker}" > "${work_root}/bin/makepkg"
chmod +x "${work_root}/bin/makepkg"
printf 'binary' > "${work_root}/binaries/ninlil-linux-amd64"

output="$(PATH="${work_root}/bin:${PATH}" RELEASE_TAG=v2026.01.01.1 ARCH_VERSION=2026.01.01.1 \
    bash "${script}" "${work_root}/binaries" 2>&1)"
status=$?
if [[ "${status}" -ne 0 ]]; then pass 'arch-packages: a missing arm64 binary exits non-zero'; else fail 'arch-packages: a missing arm64 binary exits non-zero' "${output}"; fi
if [[ -e "${marker}" ]]; then
    fail 'arch-packages: makepkg is not called' 'the stub was run'
else
    pass 'arch-packages: makepkg is not called'
fi

finish
