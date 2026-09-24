#!/usr/bin/env bash
# Expires pre-release builds 24 hours after they were published.
#
# Removed once older than MAX_AGE_HOURS:
#   releases        every pre-release, together with its git tag, and every draft (a draft is a
#                   release whose pipeline never finished)
#   container image every version tagged <yyyy>.<mm>.<dd>.<increment>-<branch>-<short-sha>
#   NuGet packages  every version with a pre-release label (<yyyy>.<m>.<d>.<increment>-<branch>)
#
# Never removed: full releases, their tags, their container tags (the release version and
# latest), and their NuGet packages. Each rule matches only the pre-release shape of its own
# artifact, so a release artifact can never be mistaken for one.
#
# Environment:
#   NINLIL_CI_TOKEN  a Forgejo access token with repository and package write access
#   MAX_AGE_HOURS    optional, default 24
#   DRY_RUN          optional; "true" lists what would be removed without removing it

set -euo pipefail

: "${NINLIL_CI_TOKEN:?NINLIL_CI_TOKEN is required}"

server="${FORGEJO_SERVER_URL:-${GITHUB_SERVER_URL:?no server URL in the environment}}"
repository="${FORGEJO_REPOSITORY:-${GITHUB_REPOSITORY:?no repository in the environment}}"
owner="${repository%%/*}"
api="${server}/api/v1"
max_age_hours="${MAX_AGE_HOURS:-24}"
dry_run="${DRY_RUN:-false}"
cutoff="$(date -u -d "-${max_age_hours} hours" +%s)"

# Pre-release shapes, per artifact (see compute-version.sh).
image_pattern='^[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]+-[a-z0-9-]+-[0-9a-f]{7,}$'
package_pattern='^[0-9]{4}\.[0-9]+\.[0-9]+\.[0-9]+-[a-z0-9-]+(\+[0-9a-f]+)?$'

# Calls the Forgejo API, failing on any HTTP error.
forgejo() {
    local method="$1" path="$2"
    shift 2
    curl --fail-with-body --silent --show-error \
        --request "${method}" \
        --header "Authorization: token ${NINLIL_CI_TOKEN}" \
        --header 'Accept: application/json' \
        "$@" \
        "${api}${path}"
}

# Prints every item of a paginated list endpoint as one JSON object per line.
paginate() {
    local path="$1" separator page=1 items
    separator='?'
    [[ "${path}" == *'?'* ]] && separator='&'
    while :; do
        items="$(forgejo GET "${path}${separator}limit=50&page=${page}")"
        [[ "$(jq 'length' <<< "${items}")" -eq 0 ]] && return
        jq -c '.[]' <<< "${items}"
        page=$((page + 1))
    done
}

# True when an ISO-8601 timestamp is older than the cutoff.
expired() {
    (($(date -u -d "$1" +%s) < cutoff))
}

# Runs a removal, or only reports it in a dry run.
remove() {
    local description="$1"
    shift
    if [[ "${dry_run}" == 'true' ]]; then
        echo "Would remove ${description}"
    else
        echo "Removing ${description}"
        "$@" > /dev/null
    fi
}

# Percent-encodes a path segment (tags contain "+").
encode() {
    jq -rn --arg value "$1" '$value | @uri'
}

echo "Removing pre-release builds published before $(date -u -d "@${cutoff}" '+%F %T UTC')"

# Releases: collected first, then removed, so deleting never shifts the pages being read.
mapfile -t releases < <(paginate "/repos/${repository}/releases?draft=true" \
    | jq -c 'select(.prerelease or .draft) | {id, tag_name, draft, created_at}')
for release in "${releases[@]}"; do
    id="$(jq -r '.id' <<< "${release}")"
    tag="$(jq -r '.tag_name' <<< "${release}")"
    draft="$(jq -r '.draft' <<< "${release}")"
    expired "$(jq -r '.created_at' <<< "${release}")" || continue

    remove "release ${tag}" forgejo DELETE "/repos/${repository}/releases/${id}"
    # A draft never created its tag; a published pre-release did.
    if [[ "${draft}" == 'false' ]]; then
        remove "tag ${tag}" forgejo DELETE "/repos/${repository}/tags/$(encode "${tag}")"
    fi
done

# Packages: the container image's pre-release tags and NuGet pre-release versions.
mapfile -t versions < <(paginate "/packages/${owner}" \
    | jq -c '{type, name, version, created_at}')
for entry in "${versions[@]}"; do
    type="$(jq -r '.type' <<< "${entry}")"
    name="$(jq -r '.name' <<< "${entry}")"
    version="$(jq -r '.version' <<< "${entry}")"
    expired "$(jq -r '.created_at' <<< "${entry}")" || continue

    case "${type}" in
        container)
            [[ "${name}" == 'ninlil' && "${version}" =~ ${image_pattern} ]] || continue
            ;;
        nuget)
            [[ "${name}" == Ninlil.* && "${version}" =~ ${package_pattern} ]] || continue
            ;;
        *)
            continue
            ;;
    esac

    remove "${type} ${name} ${version}" \
        forgejo DELETE "/packages/${owner}/${type}/$(encode "${name}")/$(encode "${version}")"
done
