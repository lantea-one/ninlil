#!/usr/bin/env bash
# Makes sure this runner has a BuildKit builder with explicit DNS servers, and selects it for the
# rest of the job.
#
# The runners are real network appliances: some run their own DNS server, which the host itself
# resolves through, but which build steps inside BuildKit's default network cannot reach -- every
# `apt-get`, `npm ci`, and `dotnet restore` then times out resolving names. A `docker-container`
# builder created with a buildkitd configuration naming its own nameservers resolves the same way
# on every runner, whatever that host's own resolver setup is.
#
# The builder persists on the runner (named after its configuration, so a changed DNS list gets a
# fresh builder), which keeps its layer cache between pipeline runs. Its state lives under
# BUILDX_CONFIG, set here to the runner user's own buildx directory, so a step that points
# DOCKER_CONFIG somewhere temporary (for registry credentials) still finds it.
#
# Environment:
#   NINLIL_BUILD_DNS  optional, comma-separated nameservers; default 8.8.8.8,1.1.1.1
#
# Writes BUILDX_BUILDER and BUILDX_CONFIG to $GITHUB_ENV for the following steps.

set -euo pipefail

dns="${NINLIL_BUILD_DNS:-8.8.8.8,1.1.1.1}"
buildx_config="${BUILDX_CONFIG:-${HOME}/.docker/buildx}"
export BUILDX_CONFIG="${buildx_config}"

# buildkitd.toml: the nameservers every build step's resolv.conf is given.
config="$(mktemp)"
trap 'rm -f "${config}"' EXIT
{
    echo '[dns]'
    printf '  nameservers = ['
    first=1
    IFS=',' read -ra servers <<< "${dns}"
    for server in "${servers[@]}"; do
        server="${server// /}"
        [[ -z "${server}" ]] && continue
        ((first)) || printf ', '
        printf '"%s"' "${server}"
        first=0
    done
    echo ']'
} > "${config}"
cat "${config}"

name="ninlil-$(sha256sum "${config}" | cut -c 1-8)"
if docker buildx inspect "${name}" > /dev/null 2>&1; then
    echo "Using the existing builder ${name}"
else
    echo "Creating the builder ${name}"
    # Another job on this runner may create the same builder at the same moment; either one
    # winning is fine.
    docker buildx create --name "${name}" --driver docker-container \
        --buildkitd-config "${config}" > /dev/null \
        || docker buildx inspect "${name}" > /dev/null
fi
docker buildx inspect --bootstrap "${name}" > /dev/null

if [[ -n "${GITHUB_ENV:-}" ]]; then
    echo "BUILDX_BUILDER=${name}" >> "${GITHUB_ENV}"
    echo "BUILDX_CONFIG=${buildx_config}" >> "${GITHUB_ENV}"
fi
echo "BUILDX_BUILDER=${name}"
