#!/bin/sh
# Post-install script of the ninlil Deb and RPM packages (declared in ../nfpm.yaml). It runs after
# the files are unpacked, on a first install and on an upgrade.
#
# It creates the ninlil system user (systemd-sysusers reads the packaged
# /usr/lib/sysusers.d/ninlil.conf; useradd is the fallback where systemd is absent, such as a
# container), creates the state directory the user owns, and reloads systemd so it sees the unit.
# It never enables or starts the service: the administrator does that with
# "systemctl enable --now ninlil".
#
# Arguments: the package manager's own (configure/1/2); unused.
# Exits non-zero only when the user cannot be created.

set -e

if command -v systemd-sysusers > /dev/null 2>&1; then
    systemd-sysusers /usr/lib/sysusers.d/ninlil.conf
elif ! getent passwd ninlil > /dev/null 2>&1; then
    useradd --system --user-group --home-dir /var/lib/ninlil --no-create-home \
        --shell /usr/sbin/nologin --comment "Ninlil DNS/DHCP server" ninlil
fi

# systemd creates the directory when the unit starts (StateDirectory=); creating it here also lets
# the executable be started by hand. It is never removed.
install -d -o ninlil -g ninlil -m 0700 /var/lib/ninlil

if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi
