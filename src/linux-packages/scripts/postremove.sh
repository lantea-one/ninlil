#!/bin/sh
# Post-remove script of the ninlil Deb and RPM packages (declared in ../nfpm.yaml). It makes
# systemd forget the removed unit.
#
# Nothing else is removed: /var/lib/ninlil (database, keys, certificates) and the ninlil user are
# the administrator's data and stay after remove and purge alike. dpkg itself deletes the
# /etc/ninlil/ninlil.env conffile on purge.

set -e

if [ -d /run/systemd/system ]; then
    systemctl daemon-reload || true
fi
