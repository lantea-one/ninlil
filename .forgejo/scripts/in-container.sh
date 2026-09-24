#!/usr/bin/env bash
# Runs a command in a throwaway container of one of the pipeline's toolchains (docker/ci.Dockerfile),
# with the repository mounted at /src, so nothing is built or tested on the host itself.
#
# The command runs as the calling user, so everything it writes into the checkout stays owned by
# that user, and with HOME in the container's own /tmp. The container is on Docker's own network,
# so a test listener can never collide with a service the host runs. The only other thing shared
# with the host is a package cache (NuGet and npm) under ~/.cache/ninlil-ci, which saves every job
# downloading the same packages again.
#
# Usage: in-container.sh [--docker] <dotnet|node|arch> <command> [argument...]
#   --docker  Also hands the container the host's Docker socket, for the tests that start their
#             own database containers (Testcontainers), and routes those containers' published
#             ports back through the host gateway.
#
# BUILD_VERSION, BUILD_PACKAGE_VERSION, BUILD_ASSEMBLY_VERSION, RELEASE_TAG, and ARCH_VERSION are
# passed through when they are set.

set -euo pipefail

docker_socket=false
if [[ "${1:-}" == '--docker' ]]; then
    docker_socket=true
    shift
fi
toolchain="${1:?usage: in-container.sh [--docker] <dotnet|node|arch> <command> [argument...]}"
shift

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
image="ninlil-ci-${toolchain}"
cache="${XDG_CACHE_HOME:-${HOME}/.cache}/ninlil-ci"
mkdir -p "${cache}/nuget" "${cache}/npm"

platform=()
if [[ "${toolchain}" == 'arch' ]]; then
    platform=(--platform linux/amd64)
fi

# Built from the Dockerfile alone, with no build context; after the first build on a host every
# layer is cached. The build's own output is shown only if it fails.
build_log="$(mktemp)"
trap 'rm -f "${build_log}"' EXIT
if ! docker build --progress=plain "${platform[@]}" --target "${toolchain}" --tag "${image}" \
    - < "${repository_root}/docker/ci.Dockerfile" > "${build_log}" 2>&1; then
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
    --volume "${cache}/npm:/cache/npm"
    --env HOME=/tmp
    --env DOTNET_CLI_HOME=/tmp
    --env NUGET_PACKAGES=/cache/nuget
    --env npm_config_cache=/cache/npm
    --env DOTNET_CLI_TELEMETRY_OPTOUT=1
    --env DOTNET_NOLOGO=1
    --env BUILD_VERSION
    --env BUILD_PACKAGE_VERSION
    --env BUILD_ASSEMBLY_VERSION
    --env RELEASE_TAG
    --env ARCH_VERSION
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
