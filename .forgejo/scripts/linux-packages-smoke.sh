#!/usr/bin/env bash
# Install-tests the ninlil Deb and RPM packages in disposable containers of the distributions the
# packages support: Debian 12, Ubuntu 22.04 and 24.04 (Deb), Rocky Linux 9 and Fedora 41 (RPM).
# For each one it starts the distribution's image, installs the package from the given directory
# with apt-get or dnf, and runs linux-packages-smoke-container.sh in it: files, user, the
# executable answering /status, upgrade over itself, remove and purge.
#
# The images are pinned by multi-architecture index digest like every other image in the
# repository (bump-pins.sh refreshes them). Debian and Ubuntu come from the public.ecr.aws mirror
# of Docker Hub's official images and Rocky and Fedora from quay.io, because Docker Hub rate
# limits anonymous pulls from shared runners.
#
# Usage: linux-packages-smoke.sh <packages-directory>
#   The directory holds the packages linux-packages.sh built (ninlil_*_<arch>.deb,
#   ninlil-*.<arch>.rpm).
#
# Environment:
#   SMOKE_ARCH          amd64 (default) or arm64: the packages and containers' architecture. A
#                       non-native architecture needs binfmt/QEMU emulation, which the pipeline
#                       does not rely on, so it tests amd64 only.
#   SMOKE_OLD_PACKAGES  optional directory with an older build of the packages; each container
#                       then upgrades from it first.
#   SMOKE_IMAGES        optional space-separated image references that replace the default list,
#                       for a local run when a registry is unreachable.
#
# Output: the container script's per-check lines for every image and a summary. Exit status: 0
# when every image passes, 1 otherwise (a failed image does not stop the others).

set -euo pipefail
shopt -s inherit_errexit

packages="${1:?usage: linux-packages-smoke.sh <packages-directory>}"
packages="$(cd "${packages}" && pwd)"
arch="${SMOKE_ARCH:-amd64}"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

images=(
    public.ecr.aws/docker/library/debian:12@sha256:2c037a04925515fdd6ea85ea14a682d0e79931f5e9f5d07b6dbfc6ba12f9e858
    public.ecr.aws/docker/library/ubuntu:22.04@sha256:5ec03bb3441e8b0bf3b4f9cd4629a1ae763010dc3035bb8da3ae6cf026486401
    public.ecr.aws/docker/library/ubuntu:24.04@sha256:534baea6a22c03a63003dbc8dbe78fe34bc0d7e595d9a9dc9834884ff530eb55
    quay.io/rockylinux/rockylinux:9@sha256:8101994123cf3d0a8fee517bee7f39e555c7d92bd2d9eb3303cc988a0eeed00f
    quay.io/fedora/fedora:41@sha256:893f7eeffce8e3e2bb2bc62786ba5603fb1e0bd6ba07091acb892266b95bafdf
)
if [[ -n "${SMOKE_IMAGES:-}" ]]; then
    read -r -a images <<< "${SMOKE_IMAGES}"
fi

mounts=(
    --volume "${packages}:/packages:ro"
    --volume "${here}/linux-packages-smoke-container.sh:/smoke.sh:ro"
)
if [[ -n "${SMOKE_OLD_PACKAGES:-}" ]]; then
    mounts+=(--volume "$(cd "${SMOKE_OLD_PACKAGES}" && pwd):/old-packages:ro")
fi

failed=()
for image in "${images[@]}"; do
    echo "=== ${image}"
    if ! docker run --rm --platform "linux/${arch}" "${mounts[@]}" "${image}" bash /smoke.sh; then
        failed+=("${image}")
    fi
done

if ((${#failed[@]} > 0)); then
    echo "Package smoke test failed on: ${failed[*]}" >&2
    exit 1
fi
echo "Package smoke test passed on ${#images[@]} image(s)."
