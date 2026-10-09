#!/usr/bin/env bash
# Shared setup for the Forgejo API script tests (cleanup.test.sh, release.test.sh).
#
# Source it after assert.sh. It starts fake-forgejo.py on a free local port, puts a `curl`
# wrapper first on PATH, and provides helpers to write canned routes and run a script under
# test. The wrapper logs every curl command line (argv) to $argv_log and, for each --config
# file, the file's mode at call time, then runs the real curl. Tests use both logs to prove
# that no token is ever on a command line.
#
# Inputs: none. Writes only to a temporary directory it removes on exit (it also stops the
# server). Needs bash, python3, curl, jq.

# The variables and functions below are used by the test scripts that source this file.
# shellcheck disable=SC2034,SC2154
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scripts="$(cd "${here}/.." && pwd)"
work_root="$(mktemp -d)"
bin_dir="${work_root}/bin"
routes="${work_root}/routes.json"
request_log="${work_root}/requests.log"
argv_log="${work_root}/argv.log"
real_curl="$(command -v curl)"
mkdir -p "${bin_dir}"

cat > "${bin_dir}/curl" <<WRAPPER
#!/usr/bin/env bash
printf '%s\n' "\$*" >> "${argv_log}"
previous=''
for argument in "\$@"; do
    if [[ "\${previous}" == '--config' ]]; then
        printf 'CONFIG %s %s\n' "\${argument}" "\$(stat -c %a "\${argument}")" >> "${argv_log}"
    fi
    previous="\${argument}"
done
exec "${real_curl}" "\$@"
WRAPPER
chmod +x "${bin_dir}/curl"

python3 -I "${here}/fake-forgejo.py" "${routes}" "${request_log}" "${work_root}/port" &
server_pid=$!
trap 'kill "${server_pid}" 2>/dev/null; rm -rf "${work_root}"' EXIT
for _ in $(seq 1 50); do
    [[ -s "${work_root}/port" ]] && break
    sleep 0.1
done
server_url="http://127.0.0.1:$(cat "${work_root}/port")"

# Clears both logs and writes the routes for the next case. Argument: routes JSON.
set_routes() {
    : > "${request_log}"
    : > "${argv_log}"
    printf '%s' "$1" > "${routes}"
}

# Runs a script under test with the fake server, the curl wrapper and a throwaway runner temp
# directory. Output goes to $output, the exit code to $status. Extra environment comes first,
# as VAR=value arguments, followed by `--` and the command.
run_script() {
    local env_args=()
    while [[ "$1" != '--' ]]; do
        env_args+=("$1")
        shift
    done
    shift
    run_temp="$(mktemp -d -p "${work_root}")"
    output="$(env PATH="${bin_dir}:${PATH}" RUNNER_TEMP="${run_temp}" \
        FORGEJO_SERVER_URL="${server_url}" FORGEJO_REPOSITORY=acme/ninlil \
        NINLIL_CI_TOKEN="${token}" NINLIL_CI_USER=ci \
        "${env_args[@]}" "$@" 2>&1)"
    status=$?
}
