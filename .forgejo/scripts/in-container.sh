#!/usr/bin/env bash
# Runs a command in a throwaway container of one of the pipeline's toolchains (docker/ci.Dockerfile),
# with the repository mounted at /src, so nothing is built or tested on the host.
#
# The command runs as the calling user, so everything it writes into the checkout stays owned by
# that user, with HOME set to the container's /tmp. The container is on Docker's bridge network,
# so a test listener never collides with a service the host runs. The only other thing shared
# with the host is a package cache (NuGet and npm) under ~/.cache/ninlil-ci, which saves every job
# downloading the same packages again. NuGet's lock files (NUGET_SCRATCH) are shared too: they
# keep two jobs on one host from extracting the same package into that cache at once, and would
# otherwise sit in each container's /tmp, where no other container sees them.
#
# Every call builds the toolchain image from the checkout's docker/ci.Dockerfile (cached when the
# file is unchanged), tags it ninlil-builder-<toolchain>:<first 12 hex digits of the file's
# SHA-256> with the label ninlil.builder=<toolchain>, and runs the container from that tag. It never
# removes an image; the hourly cleanup workflow removes builders of other Dockerfile versions.
#
# Usage: in-container.sh [--docker] <dotnet|node|arch|lint|packages> <command> [argument...]
#   --docker  Also hands the container the host's Docker socket, for the tests that start
#             database containers (Testcontainers), and routes those containers' published ports
#             back through the host gateway.
#
# BUILD_VERSION, BUILD_PACKAGE_VERSION, BUILD_ASSEMBLY_VERSION, RELEASE_TAG, ARCH_VERSION,
# DEB_VERSION, RPM_VERSION, RPM_RELEASE, NPM_CI_TIMEOUT and NPM_CI_KILL_AFTER (see npm-ci.sh) are passed through when they are set. Exits with the
# command's status, or 1 when the toolchain image fails to build (its build log is printed then).

set -euo pipefail

docker_socket=false
if [[ "${1:-}" == '--docker' ]]; then
    docker_socket=true
    shift
fi
toolchain="${1:?usage: in-container.sh [--docker] <dotnet|node|arch|lint|packages> <command> [argument...]}"
shift

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# The tag names the Dockerfile's content, so jobs on branches with different Dockerfiles never
# share a tag. That matters because Docker's containerd image store deletes an image as soon as
# its last tag moves elsewhere, which would pull the image from under a job about to start it.
dockerfile="${repository_root}/docker/ci.Dockerfile"
dockerfile_hash="$(sha256sum < "${dockerfile}" | cut -c1-12)"
image="ninlil-builder-${toolchain}:${dockerfile_hash}"
cache="${XDG_CACHE_HOME:-${HOME}/.cache}/ninlil-ci"
mkdir -p "${cache}/nuget" "${cache}/nuget-scratch" "${cache}/npm"

# Arch Linux publishes amd64 images only, so an arm64 runner needs amd64 emulation (binfmt) here.
platform=()
if [[ "${toolchain}" == 'arch' ]]; then
    platform=(--platform linux/amd64)
fi

# The image is built from the Dockerfile alone, with no build context, on every call, so it always
# matches the checkout's docker/ci.Dockerfile; an unchanged file costs nothing because every layer
# is cached. The build output is shown only if it fails. Builds of one toolchain on this host run
# one at a time (the lock file in the cache folder): two cold builds at once produce different
# image IDs for the same tag, and the second would delete the first one's image, while a build
# that waits finds every layer cached and produces the same image.
build_log="$(mktemp)"
trap 'rm -f "${build_log}"' EXIT
if ! (
    flock 9
    docker build --progress=plain "${platform[@]}" --target "${toolchain}" --tag "${image}" \
        --label "ninlil.builder=${toolchain}" - < "${dockerfile}"
) 9> "${cache}/build-${toolchain}.lock" > "${build_log}" 2>&1; then
    cat "${build_log}" >&2
    exit 1
fi
rm -f "${build_log}"


arguments=(
    --rm
    "${platform[@]}"
    --user "$(id -u):$(id -g)"
    --volume "${repository_root}:/src"
    --workdir /src
    --volume "${cache}/nuget:/cache/nuget"
    --volume "${cache}/nuget-scratch:/cache/nuget-scratch"
    --volume "${cache}/npm:/cache/npm"
    --env HOME=/tmp
    --env DOTNET_CLI_HOME=/tmp
    --env NUGET_PACKAGES=/cache/nuget
    --env NUGET_SCRATCH=/cache/nuget-scratch
    --env npm_config_cache=/cache/npm
    --env DOTNET_CLI_TELEMETRY_OPTOUT=1
    --env DOTNET_NOLOGO=1
    --env BUILD_VERSION
    --env BUILD_PACKAGE_VERSION
    --env BUILD_ASSEMBLY_VERSION
    --env RELEASE_TAG
    --env ARCH_VERSION
    --env DEB_VERSION
    --env RPM_VERSION
    --env RPM_RELEASE
    --env NPM_CI_TIMEOUT
    --env NPM_CI_KILL_AFTER
)
if [[ "${docker_socket}" == 'true' ]]; then
    arguments+=(
        --volume /var/run/docker.sock:/var/run/docker.sock
        --group-add "$(stat -c %g /var/run/docker.sock)"
        --add-host host.docker.internal:host-gateway
        --env TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal
    )
fi

exec docker run "${arguments[@]}" "${image}" "$@"
