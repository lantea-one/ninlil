#!/usr/bin/env bash
#
# Installs Ninlil as a systemd service on a systemd-based Linux host: it creates an unprivileged
# system account (ninlil-dns), the directories the service needs and a starter environment file,
# installs the unit file that sits next to this script, and enables (but does not start) it.
#
# It accepts either a release binary (ninlil-linux-amd64 / ninlil-linux-arm64) or the output
# directory of `dotnet publish src/dotnet/src/Ninlil.Web`:
#
#   sudo ./install.sh ./ninlil-linux-amd64
#   sudo ./install.sh /path/to/published/Ninlil.Web
#
# Writes: /opt/ninlil-dns (program, data/ and wwwroot/), /etc/ninlil-dns/ninlil-dns.env and
# /etc/systemd/system/ninlil-dns.service. Ninlil logs to the journal, so no log directory is
# created. Exits non-zero when not run as root, when the argument is missing or does not exist,
# or when a step fails.
#
# Re-running it upgrades the installed program and the unit file, and leaves the data directory
# and the environment file untouched. An upgrade first removes everything in /opt/ninlil-dns
# except data/, so files the new version dropped do not linger, then restarts the service if it
# is running.

set -euo pipefail

usage() {
    echo "Usage: sudo ./install.sh <ninlil-linux-binary | publish-directory>" >&2
    exit 1
}

if [[ "${EUID}" -ne 0 ]]; then
    echo "This script must be run as root." >&2
    usage
fi

SOURCE="${1:-}"
[[ -n "${SOURCE}" && -e "${SOURCE}" ]] || usage

SCRIPT_DIRECTORY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIRECTORY="/opt/ninlil-dns"
CONFIG_DIRECTORY="/etc/ninlil-dns"
SERVICE_USER="ninlil-dns"

# Create the unprivileged service account, if it does not already exist.
if ! id "${SERVICE_USER}" &>/dev/null; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

# Lay down the directories the service needs. The program itself stays root-owned (read-only to
# the service account); only the data directory belongs to it.
mkdir -p "${INSTALL_DIRECTORY}" "${INSTALL_DIRECTORY}/data" "${CONFIG_DIRECTORY}"
# Remove the previous program (assemblies a new version dropped, or a whole publish tree being
# replaced by the single file) but keep the data directory. The guard keeps a changed
# INSTALL_DIRECTORY from ever pointing this delete elsewhere.
if [[ "${INSTALL_DIRECTORY}" == /opt/ninlil-dns ]]; then
    find "${INSTALL_DIRECTORY}" -mindepth 1 -maxdepth 1 ! -name data -exec rm -rf {} +
fi
if [[ -f "${SOURCE}" ]]; then
    # A single-file release binary, installed under the name the unit starts.
    install -m 0755 "${SOURCE}" "${INSTALL_DIRECTORY}/Ninlil.Web"
    # The admin console is built into the executable; the unit still names a web root, which
    # ASP.NET Core would try to create (and fail to, read-only) if it did not already exist.
    mkdir -p "${INSTALL_DIRECTORY}/wwwroot"
else
    cp -r "${SOURCE}/." "${INSTALL_DIRECTORY}/"
    chmod 0755 "${INSTALL_DIRECTORY}/Ninlil.Web"
fi
chown -R "${SERVICE_USER}:${SERVICE_USER}" "${INSTALL_DIRECTORY}/data"
chmod 0700 "${INSTALL_DIRECTORY}/data"

# Create a starter environment file, if one does not already exist, without overwriting any
# configuration an operator has already put in place.
ENVIRONMENT_FILE="${CONFIG_DIRECTORY}/ninlil-dns.env"
if [[ ! -f "${ENVIRONMENT_FILE}" ]]; then
    cat > "${ENVIRONMENT_FILE}" <<'ENVEOF'
# Environment variables for the Ninlil service. Every setting Ninlil reads can be set here using
# its NINLIL_-prefixed name; see ENVIRONMENT.md in the repository for the complete list.
# For example:
#   NINLIL_STORAGE_ENGINE=PostgreSql
#   NINLIL_STORAGE_CONNECTION_STRING=Host=localhost;Database=ninlil;Username=ninlil;Password=change-me
#   NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD=Change-Me-Now1!
NINLIL_ASPNETCORE_ENVIRONMENT=Production
ENVEOF
    chmod 0640 "${ENVIRONMENT_FILE}"
    chgrp "${SERVICE_USER}" "${ENVIRONMENT_FILE}"
fi

# Install and enable the systemd unit.
cp "${SCRIPT_DIRECTORY}/ninlil-dns.service" /etc/systemd/system/ninlil-dns.service
systemctl daemon-reload
systemctl enable ninlil-dns
# Restart only if the service is running, so an upgrade takes effect and a fresh install stays
# stopped.
systemctl try-restart ninlil-dns

echo "Ninlil has been installed. Review ${ENVIRONMENT_FILE}, then start it with:"
echo "  sudo systemctl start ninlil-dns"
echo "(An already running service has just been restarted onto the new version.)"
echo "The bootstrap administrator's password is printed once to: journalctl -u ninlil-dns"
