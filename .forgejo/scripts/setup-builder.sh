#!/usr/bin/env bash
# Makes sure this runner has the pipeline's BuildKit daemon, with explicit DNS servers, and
# selects it for the rest of the job.
#
# On some runners the Docker daemon's default DNS for containers cannot resolve names, and
# `docker buildx build` has no --dns flag. So the pipeline runs its own BuildKit daemon as an
# ordinary container on Docker's bridge network, started with --dns like any other container,
# and builds through it with buildx's remote driver. The daemon's own registry lookups and every
# build step (apt-get, npm ci, dotnet restore) then resolve through those nameservers.
#
# The daemon container persists on the runner, named after its DNS configuration so a changed
# list gets a fresh one, which keeps its layer cache between pipeline runs. Its buildx builder
# entry lives under BUILDX_CONFIG, set here to the runner user's own buildx directory, so a step
# that points DOCKER_CONFIG somewhere temporary (for registry credentials) still finds it.
#
# Environment:
#   NINLIL_BUILD_DNS  optional, space- or comma-separated nameservers; default 8.8.8.8 1.1.1.1
#
# Writes BUILDX_BUILDER, BUILDX_CONFIG, and NINLIL_DNS_ARGS (the matching --dns arguments, for a
# plain `docker run` in a later step) to $GITHUB_ENV.

set -euo pipefail

read -ra servers <<< "$(tr ',' ' ' <<< "${NINLIL_BUILD_DNS:-8.8.8.8 1.1.1.1}")"
dns_args=()
for server in "${servers[@]}"; do
    dns_args+=(--dns "${server}")
done

buildx_config="${BUILDX_CONFIG:-${HOME}/.docker/buildx}"
export BUILDX_CONFIG="${buildx_config}"

image='docker.io/moby/buildkit:buildx-stable-1'
name="ninlil-buildkit-$(printf '%s\n' "${servers[@]}" | sha256sum | cut -c 1-8)"

if [[ "$(docker inspect --format '{{.State.Running}}' "${name}" 2> /dev/null)" != 'true' ]]; then
    echo "Starting the BuildKit daemon ${name} (DNS: ${servers[*]})"
    docker rm --force "${name}" > /dev/null 2>&1 || true
    # BuildKit needs a privileged container to create build-step sandboxes. Another job on this
    # runner may start the same daemon at the same moment; either one winning is fine.
    docker run --detach --name "${name}" --privileged --restart unless-stopped \
        "${dns_args[@]}" \
        --volume "${name}:/var/lib/buildkit" \
        "${image}" > /dev/null \
        || [[ "$(docker inspect --format '{{.State.Running}}' "${name}" 2> /dev/null)" == 'true' ]]
fi

if ! docker buildx inspect "${name}" > /dev/null 2>&1; then
    docker buildx create --name "${name}" --driver remote "docker-container://${name}" > /dev/null \
        || docker buildx inspect "${name}" > /dev/null
fi
docker buildx inspect --bootstrap "${name}" > /dev/null
echo "Using the builder ${name}"

if [[ -n "${GITHUB_ENV:-}" ]]; then
    echo "BUILDX_BUILDER=${name}" >> "${GITHUB_ENV}"
    echo "BUILDX_CONFIG=${buildx_config}" >> "${GITHUB_ENV}"
    echo "NINLIL_DNS_ARGS=${dns_args[*]}" >> "${GITHUB_ENV}"
fi
echo "BUILDX_BUILDER=${name}"
echo "NINLIL_DNS_ARGS=${dns_args[*]}"
