#!/usr/bin/env bash
# Makes sure this runner has the pipeline's BuildKit builder, and selects it for the rest of the
# job.
#
# The runners are real network appliances, several of them running their own DNS server, and a
# container on Docker's bridge network there cannot resolve names at all -- not through the
# host's resolver, and not through a public one either. So the builder runs entirely on the
# runner's own network: the BuildKit daemon container joins the host network, and its worker
# runs every build step in that same network (--oci-worker-net=host). Image pulls and every
# `apt-get`, `npm ci`, and `dotnet restore` then resolve exactly as the runner host itself does.
# Build steps only make outbound connections; nothing in a build binds a port.
#
# NINLIL_BUILD_DNS, when set, gives build steps those nameservers instead of the host's own.
#
# The builder persists on the runner (named after its configuration, so a changed configuration
# gets a fresh builder), which keeps its layer cache between pipeline runs. Its state lives under
# BUILDX_CONFIG, set here to the runner user's own buildx directory, so a step that points
# DOCKER_CONFIG somewhere temporary (for registry credentials) still finds it.
#
# Environment:
#   NINLIL_BUILD_DNS  optional, comma-separated nameservers for build steps
#
# Writes BUILDX_BUILDER and BUILDX_CONFIG to $GITHUB_ENV for the following steps.

set -euo pipefail

buildx_config="${BUILDX_CONFIG:-${HOME}/.docker/buildx}"
export BUILDX_CONFIG="${buildx_config}"

# buildkitd.toml: empty unless explicit nameservers were asked for.
config="$(mktemp)"
trap 'rm -f "${config}"' EXIT
if [[ -n "${NINLIL_BUILD_DNS:-}" ]]; then
    {
        echo '[dns]'
        printf '  nameservers = ['
        first=1
        IFS=',' read -ra servers <<< "${NINLIL_BUILD_DNS}"
        for server in "${servers[@]}"; do
            server="${server// /}"
            [[ -z "${server}" ]] && continue
            ((first)) || printf ', '
            printf '"%s"' "${server}"
            first=0
        done
        echo ']'
    } > "${config}"
fi
flags='--oci-worker-net=host'

name="ninlil-$( (cat "${config}"; echo "${flags}") | sha256sum | cut -c 1-8)"
if docker buildx inspect "${name}" > /dev/null 2>&1; then
    echo "Using the existing builder ${name}"
else
    echo "Creating the builder ${name}"
    # Another job on this runner may create the same builder at the same moment; either one
    # winning is fine.
    docker buildx create --name "${name}" --driver docker-container \
        --driver-opt network=host \
        --buildkitd-flags "${flags}" \
        --buildkitd-config "${config}" > /dev/null \
        || docker buildx inspect "${name}" > /dev/null
fi
docker buildx inspect --bootstrap "${name}" > /dev/null

if [[ -n "${GITHUB_ENV:-}" ]]; then
    echo "BUILDX_BUILDER=${name}" >> "${GITHUB_ENV}"
    echo "BUILDX_CONFIG=${buildx_config}" >> "${GITHUB_ENV}"
fi
echo "BUILDX_BUILDER=${name}"
