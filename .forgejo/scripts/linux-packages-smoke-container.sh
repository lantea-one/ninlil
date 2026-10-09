#!/usr/bin/env bash
# Install-tests the ninlil Deb or RPM package inside one disposable distribution container. It is
# run by linux-packages-smoke.sh (mounted into the container and started as root) and can also be
# run by hand in any Debian, Ubuntu, Fedora or RHEL-family container.
#
# Steps, each reported as "ok" or "FAIL":
#   1. Install the package with the distribution's package manager (apt-get or dnf), which also
#      pulls the declared dependencies (libicu, OpenSSL, ...) from the distribution's repositories.
#   2. The files, modes, symbolic link, ninlil user and state directory are in place.
#   3. The executable starts as the ninlil user on free ports (systemd is not PID 1 in a
#      container, so it is started directly, with the unit's ExecStart arguments), answers
#      GET /status with 200 and serves the admin console at /.
#   4. Reinstalling the same package (an upgrade over itself) keeps an edited
#      /etc/ninlil/ninlil.env and the executable still starts. With /old-packages mounted, a
#      real upgrade from that older package is tested first.
#   5. Removing the package removes the executable and the unit, stops nothing else, and keeps
#      /var/lib/ninlil and the ninlil user. A Debian purge also removes the environment file; on
#      RPM the edited file is kept as ninlil.env.rpmsave.
#
# Inputs: the new packages in /packages (read-only mount); optional older packages in
# /old-packages. The architecture is the container's own.
# Output: one line per check on stdout. Exit status: 0 when every check passes, 1 otherwise.

set -uo pipefail

failures=0
server_pid=''
server_log=/tmp/ninlil-smoke.log

ok() { echo "ok   - $1"; }
bad() {
    echo "FAIL - $1"
    failures=$((failures + 1))
}
# Runs a command and reports it. Arguments: description, command...
check() {
    local description="$1"
    shift
    if "$@" > /dev/null 2>&1; then ok "${description}"; else bad "${description}"; fi
}
# Aborts the whole run: later checks make no sense without an installed package.
die() {
    echo "FAIL - $1"
    exit 1
}

# --- package manager --------------------------------------------------------------------------

if command -v apt-get > /dev/null 2>&1; then
    format=deb
    architecture="$(dpkg --print-architecture)"
    pattern="ninlil_*_${architecture}.deb"
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq > /dev/null || die 'apt-get update failed'
    install_package() { apt-get install -y -qq "$@" > /tmp/package-manager.log 2>&1; }
    reinstall_package() { apt-get install -y -qq --reinstall "$@" > /tmp/package-manager.log 2>&1; }
    remove_package() { apt-get remove -y -qq ninlil > /tmp/package-manager.log 2>&1; }
    purge_package() { apt-get purge -y -qq ninlil > /tmp/package-manager.log 2>&1; }
    is_installed() { dpkg-query -W -f='${Status}' ninlil 2> /dev/null | grep -q 'ok installed'; }
elif command -v dnf > /dev/null 2>&1; then
    format=rpm
    architecture="$(uname -m)"
    pattern="ninlil-*.${architecture}.rpm"
    install_package() { dnf install -y -q "$@" > /tmp/package-manager.log 2>&1; }
    reinstall_package() { dnf reinstall -y -q "$@" > /tmp/package-manager.log 2>&1; }
    remove_package() { dnf remove -y -q ninlil > /tmp/package-manager.log 2>&1; }
    purge_package() { :; }
    is_installed() { rpm -q ninlil > /dev/null 2>&1; }
else
    die 'neither apt-get nor dnf is available'
fi

# The newest file matching the pattern in a directory. Arguments: directory.
package_in() {
    local listing
    listing="$(compgen -G "$1/${pattern}")" || return 1
    sort -V <<< "${listing}" | tail -n 1
}

new_package="$(package_in /packages)" || die "no ${pattern} in /packages"
# shellcheck source=/dev/null
distribution="$(. /etc/os-release && echo "${PRETTY_NAME}")"
echo "== ${distribution} (${format}, ${architecture}): ${new_package##*/}"

# --- helpers ----------------------------------------------------------------------------------

# Prints "HTTP-status-line" and body of GET <path> on 127.0.0.1:<port>. Plain bash, because
# minimal images have no curl. Arguments: port, path.
http_get() {
    (
        exec 3<> "/dev/tcp/127.0.0.1/$1" || exit 1
        printf 'GET %s HTTP/1.0\r\nHost: localhost\r\nConnection: close\r\n\r\n' "$2" >&3
        cat <&3
    )
}

# Picks a port nothing listens on. Each container has its own network, so a random port in the
# dynamic range is enough.
free_port() {
    local port
    while true; do
        port=$((20000 + RANDOM % 20000))
        if ! (exec 3<> "/dev/tcp/127.0.0.1/${port}") 2> /dev/null; then
            echo "${port}"
            return
        fi
    done
}

# Starts the executable as the ninlil user the way ninlil.service does, on free ports, and
# waits for /status. Sets server_pid and web_port. Arguments: label for the messages.
start_server() {
    local dns_port https_port deadline
    # Minimal Fedora images lack setpriv (util-linux). It is test tooling, installed only now so
    # that it cannot hide a missing dependency of the package itself.
    if ! command -v setpriv > /dev/null 2>&1; then
        install_package util-linux || die 'cannot install setpriv (util-linux)'
    fi
    dns_port="$(free_port)"
    web_port="$(free_port)"
    https_port="$(free_port)"
    : > "${server_log}"
    chmod 666 "${server_log}"
    setpriv --reuid=ninlil --regid=ninlil --init-groups env \
        HOME=/var/lib/ninlil \
        DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/lib/ninlil/.bundle \
        NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT="${dns_port}" \
        NINLIL_DNS_SERVER_WEB_HTTP_PORT="${web_port}" \
        NINLIL_DNS_SERVER_WEB_HTTPS_PORT="${https_port}" \
        NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Smoke-Test-Passw0rd!' \
        /usr/lib/ninlil/ninlil --contentRoot=/var/lib/ninlil --webroot=/usr/lib/ninlil/wwwroot \
        > "${server_log}" 2>&1 &
    server_pid=$!
    deadline=$((SECONDS + 120))
    while ((SECONDS < deadline)); do
        if ! kill -0 "${server_pid}" 2> /dev/null; then
            bad "$1: the executable exited before /status answered"
            sed 's/^/       /' "${server_log}" | tail -n 30
            return 1
        fi
        if http_get "${web_port}" /status 2> /dev/null | head -n 1 | grep -q ' 200'; then
            ok "$1: /status answers 200"
            return 0
        fi
        sleep 1
    done
    bad "$1: /status did not answer within 120 seconds"
    sed 's/^/       /' "${server_log}" | tail -n 30
    return 1
}

stop_server() {
    if [[ -n "${server_pid}" ]]; then
        kill "${server_pid}" 2> /dev/null
        wait "${server_pid}" 2> /dev/null
        server_pid=''
    fi
}
trap stop_server EXIT

# Runs the start-and-answer checks. Arguments: label.
exercise_server() {
    if start_server "$1"; then
        if http_get "${web_port}" / | grep -q '<app-root'; then
            ok "$1: the admin console is served at /"
        else
            bad "$1: the admin console is served at /"
        fi
    fi
    stop_server
}

# --- 1. install (optionally from an older package first) --------------------------------------

old_package=''
if [[ -d /old-packages ]] && old_package="$(package_in /old-packages)"; then
    if install_package "${old_package}"; then ok "install the older package ${old_package##*/}"; else
        cat /tmp/package-manager.log
        die "install the older package ${old_package##*/}"
    fi
    echo '# edited by the smoke test' >> /etc/ninlil/ninlil.env
    exercise_server 'older package'
fi

if install_package "${new_package}"; then
    ok "install ${new_package##*/}"
else
    cat /tmp/package-manager.log
    die "install ${new_package##*/}"
fi
if [[ -n "${old_package}" ]]; then
    check 'upgrade from the older package keeps the edited environment file' \
        grep -q 'edited by the smoke test' /etc/ninlil/ninlil.env
fi

# --- 2. files, user, state directory ----------------------------------------------------------

check 'the executable is root:root 0755' test "$(stat -c '%U:%G %a' /usr/lib/ninlil/ninlil)" = 'root:root 755'
check 'the data link points at /var/lib/ninlil' test "$(readlink /usr/lib/ninlil/data)" = /var/lib/ninlil
web_root_is_empty() { [[ -d /usr/lib/ninlil/wwwroot && -z "$(ls -A /usr/lib/ninlil/wwwroot)" ]]; }
check 'the empty web root exists and is empty' web_root_is_empty
check 'the systemd unit is installed' test -f /usr/lib/systemd/system/ninlil.service
check 'the unit runs the packaged executable' grep -q '^ExecStart=/usr/lib/ninlil/ninlil ' /usr/lib/systemd/system/ninlil.service
check 'the sysusers entry is installed' test -f /usr/lib/sysusers.d/ninlil.conf
check 'the environment file is root:root 0640' test "$(stat -c '%U:%G %a' /etc/ninlil/ninlil.env)" = 'root:root 640'
check 'the license is installed' test -f /usr/share/licenses/ninlil/LICENSE
check 'the ninlil system user exists' getent passwd ninlil
check 'the ninlil group exists' getent group ninlil
user_has_home_and_no_shell() {
    local entry
    entry="$(getent passwd ninlil)" || return 1
    [[ "$(cut -d: -f6 <<< "${entry}")" == /var/lib/ninlil && "$(cut -d: -f7 <<< "${entry}")" == */nologin ]]
}
check 'the user has /var/lib/ninlil as home and no login shell' user_has_home_and_no_shell
check '/var/lib/ninlil is owned by ninlil with mode 0700' test "$(stat -c '%U:%G %a' /var/lib/ninlil)" = 'ninlil:ninlil 700'
check 'the service is not enabled by the install' test ! -e /etc/systemd/system/multi-user.target.wants/ninlil.service

# --- 3. the executable runs -------------------------------------------------------------------

exercise_server 'installed package'

# --- 4. upgrade over itself -------------------------------------------------------------------

echo '# edited by the smoke test' >> /etc/ninlil/ninlil.env
if reinstall_package "${new_package}"; then
    ok 'reinstall the same package (upgrade over itself)'
else
    cat /tmp/package-manager.log
    bad 'reinstall the same package (upgrade over itself)'
fi
check 'the edited environment file survives the upgrade' grep -q 'edited by the smoke test' /etc/ninlil/ninlil.env
check 'no environment file backup is left beside it' test ! -e /etc/ninlil/ninlil.env.dpkg-new -a ! -e /etc/ninlil/ninlil.env.rpmnew
exercise_server 'after upgrade'

# --- 5. remove and purge ----------------------------------------------------------------------

# The first start created the database and key files in the state directory.
echo data > /var/lib/ninlil/smoke-marker
chown ninlil:ninlil /var/lib/ninlil/smoke-marker

if remove_package; then ok 'remove the package'; else
    cat /tmp/package-manager.log
    bad 'remove the package'
fi
if is_installed; then bad 'the package is no longer installed'; else ok 'the package is no longer installed'; fi
check 'the executable is gone' test ! -e /usr/lib/ninlil/ninlil
check 'the unit is gone' test ! -e /usr/lib/systemd/system/ninlil.service
check 'the state directory and its files stay' test -f /var/lib/ninlil/smoke-marker
check 'the ninlil user stays' getent passwd ninlil
if [[ "${format}" == deb ]]; then
    check 'remove keeps the edited environment file (dpkg conffile)' test -f /etc/ninlil/ninlil.env
    if purge_package; then ok 'purge the package'; else
        cat /tmp/package-manager.log
        bad 'purge the package'
    fi
    check 'purge removes the environment file' test ! -e /etc/ninlil/ninlil.env
else
    check 'remove saves the edited environment file as ninlil.env.rpmsave' test -f /etc/ninlil/ninlil.env.rpmsave
fi
check 'the state directory still stays' test -f /var/lib/ninlil/smoke-marker

if ((failures > 0)); then
    echo "${failures} check(s) failed."
    exit 1
fi
echo 'All checks passed.'
