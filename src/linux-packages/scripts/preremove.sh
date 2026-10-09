#!/bin/sh
# Pre-remove script of the ninlil Deb and RPM packages (declared in ../nfpm.yaml). It stops the
# service when the package is being removed, and not when it is being upgraded.
#
# Argument 1 tells the two apart: dpkg passes "remove" (or "upgrade"), rpm passes the number of
# copies that remain installed ("0" on a removal, "1" on an upgrade). Debian's "deconfigure" and
# the other maintainer-script words do nothing here.
#
# The service is stopped but never disabled or deleted; the state in /var/lib/ninlil stays.

set -e

case "$1" in
    remove | 0)
        if [ -d /run/systemd/system ]; then
            systemctl stop ninlil.service || true
        fi
        ;;
esac
