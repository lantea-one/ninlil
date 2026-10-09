#!/usr/bin/env bash
# Helpers for calling the Forgejo API and registry without putting a token on a command line.
#
# Source it (do not execute it) from cleanup.sh, release.sh and the pipeline's NuGet push step.
# A command line is readable by every local user through /proc/<pid>/cmdline, so every secret
# goes into a curl config file instead and curl reads it with `--config`. All config files live
# in one private directory (mode 0700, files 0600) that an EXIT trap removes; a trap the sourcing
# script already set still runs.
#
# Inputs (environment):
#   NINLIL_CI_TOKEN  the Forgejo access token (needed by forgejo_api and forgejo_download)
#   FORGEJO_API      the API base URL, e.g. https://git.example/api/v1 (needed by forgejo_api)
#   RUNNER_TEMP / TMPDIR  where the private directory is created (/tmp when unset)
#
# Provides:
#   forgejo_work                      variable: the private directory; callers may keep scratch
#                                     files there, they are removed on exit
#   forgejo_write_config VAR KEY VAL  writes a curl config file with one `KEY = "VAL"` line and
#                                     stores its path in the variable named VAR
#   forgejo_api METHOD PATH [curl...] calls ${FORGEJO_API}PATH, fails on any HTTP error, prints
#                                     the response body
#   forgejo_download URL OUTPUT       downloads a file with the token, fails on any HTTP error
#   forgejo_paginate PATH OUTPUT      writes every item of a paginated list endpoint to OUTPUT,
#                                     one JSON object per line; fails if any page fails or is not
#                                     a JSON array, so a caller never works on a partial list
#
# Callers must run forgejo_paginate as a plain command (not inside `if`, `||` or a command
# substitution) so that `set -e` sees its failure. They should also `shopt -s inherit_errexit`,
# so a failing call inside `$(...)` stops the script.

# Escapes a value for a double-quoted curl config string: backslash and double quote.
forgejo_quote() {
    local value="$1"
    value="${value//\\/\\\\}"
    value="${value//\"/\\\"}"
    printf '%s' "${value}"
}

# Removes the private directory, then runs the EXIT trap the sourcing script had set before.
forgejo_cleanup() {
    rm -rf "${forgejo_work}"
    if [[ -n "${forgejo_previous_trap}" ]]; then
        eval "${forgejo_previous_trap}"
    fi
}

# Creates the private directory and installs the EXIT trap, chaining any existing one.
forgejo_init() {
    local previous
    local -a words
    forgejo_work="$(umask 077 && mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/forgejo-api.XXXXXX")"
    forgejo_previous_trap=''
    previous="$(trap -p EXIT)"
    if [[ -n "${previous}" ]]; then
        # `trap -p` prints: trap -- 'command' EXIT
        eval "words=(${previous})"
        forgejo_previous_trap="${words[2]}"
    fi
    trap forgejo_cleanup EXIT
}
forgejo_init

# Writes a curl config file holding one setting and stores its path in the variable named $1.
# Arguments: variable name, curl option (header, user), option value (never logged).
forgejo_write_config() {
    local -n target="$1"
    local file quoted
    if [[ "$3" == *$'\n'* ]]; then
        echo "A credential contains a line break; refusing to write a curl config." >&2
        return 1
    fi
    file="$(umask 077 && mktemp "${forgejo_work}/curl.XXXXXX")"
    quoted="$(forgejo_quote "$3")"
    printf '%s = "%s"\n' "$2" "${quoted}" > "${file}"
    # shellcheck disable=SC2034 # the nameref assigns the caller's variable
    target="${file}"
}

# Returns the path of the config file carrying the CI token as an Authorization header.
forgejo_token_config() {
    : "${NINLIL_CI_TOKEN:?NINLIL_CI_TOKEN is required}"
    local file="${forgejo_work}/token.conf" quoted
    if [[ ! -f "${file}" ]]; then
        quoted="$(forgejo_quote "${NINLIL_CI_TOKEN}")"
        (umask 077 && printf 'header = "Authorization: token %s"\n' "${quoted}" > "${file}")
    fi
    printf '%s' "${file}"
}

# Calls the Forgejo API. Arguments: HTTP method, path below FORGEJO_API, extra curl arguments.
forgejo_api() {
    local method="$1" path="$2" config
    shift 2
    config="$(forgejo_token_config)"
    curl --config "${config}" --fail-with-body --silent --show-error \
        --request "${method}" \
        --header 'Accept: application/json' \
        "$@" \
        "${FORGEJO_API:?FORGEJO_API is required}${path}"
}

# Downloads a URL with the token. Arguments: URL, output file.
forgejo_download() {
    local config
    config="$(forgejo_token_config)"
    curl --config "${config}" --fail --silent --show-error --location --output "$2" "$1"
}

# Writes every item of a paginated list endpoint to a file, one JSON object per line.
# Arguments: path (with or without a query), output file.
forgejo_paginate() {
    local path="$1" output="$2" separator='?' page=1 count body="${forgejo_work}/page.json"
    [[ "${path}" == *'?'* ]] && separator='&'
    : > "${output}"
    while :; do
        forgejo_api GET "${path}${separator}limit=50&page=${page}" > "${body}"
        # An error body or an HTML page must not read as an empty list.
        if ! jq -e 'type == "array"' "${body}" > /dev/null; then
            echo "Page ${page} of ${path} is not a JSON array." >&2
            return 1
        fi
        count="$(jq 'length' "${body}")"
        [[ "${count}" -eq 0 ]] && return 0
        jq -c '.[]' "${body}" >> "${output}"
        page=$((page + 1))
    done
}
