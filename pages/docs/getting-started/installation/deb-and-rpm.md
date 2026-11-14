# Debian, Ubuntu, Fedora, RHEL and Rocky Linux

Every release carries a `.deb` (Debian, Ubuntu) and an `.rpm` (Fedora, RHEL, Rocky Linux,
AlmaLinux) for `amd64`/`x86_64` and `arm64`/`aarch64`. They install the server in `/usr/lib/ninlil/`,
a `ninlil` systemd service that runs as an unprivileged `ninlil` user, all state in
`/var/lib/ninlil` and the settings in `/etc/ninlil/ninlil.env`. Installing never starts the service.

## Before you start

1. A host with `sudo`, `curl` and `sha256sum`. `uname -m` prints `x86_64` or `aarch64`.
2. Port 53 free ([Installation](index.md#before-every-installation)).

## Steps

1. Open the [releases page](https://git.lantea.one/lantea-one/ninlil/releases), pick a release and
   copy the link of the package for your distribution and CPU. The file names are:

    | Distribution | CPU | File |
    |---|---|---|
    | Debian, Ubuntu | x86-64 | `ninlil-<version>-amd64.deb` |
    | Debian, Ubuntu | ARM64 | `ninlil-<version>-arm64.deb` |
    | Fedora, RHEL, Rocky Linux, AlmaLinux | x86-64 | `ninlil-<version>-1.x86_64.rpm` |
    | Fedora, RHEL, Rocky Linux, AlmaLinux | ARM64 | `ninlil-<version>-1.aarch64.rpm` |

    `<version>` is the date version, such as `2026.10.09.1`.
2. Download the package and the checksum list, then check the download. Replace `<tag>` with the
   release's tag and the file name with yours:

    ```bash
    curl -LO https://git.lantea.one/lantea-one/ninlil/releases/download/<tag>/ninlil-<version>-amd64.deb
    curl -LO https://git.lantea.one/lantea-one/ninlil/releases/download/<tag>/SHA256SUMS
    sha256sum --check --ignore-missing SHA256SUMS
    ```

    The last command must print `OK` for your file. The packages are not signed: the checksums are
    your check that the file is the one the pipeline built.
3. Install it. The `./` in front of the file name is required: it tells the package manager to use
   the local file.

    ```bash
    sudo apt install ./ninlil-<version>-amd64.deb        # Debian, Ubuntu
    sudo dnf install ./ninlil-<version>-1.x86_64.rpm     # Fedora, RHEL, Rocky Linux, AlmaLinux
    ```

4. Start it now and at every boot, then read the log for the one-time administrator password:

    ```bash
    sudo systemctl enable --now ninlil
    sudo journalctl -u ninlil
    ```

    Open `http://<this host>/` and sign in as `admin`. To choose the password yourself, set
    `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD` in `/etc/ninlil/ninlil.env` before the first
    start. That file takes every `NINLIL_*` setting in the
    [environment variables](../../reference/environment-variables.md). Run
    `sudo systemctl restart ninlil` after you edit it.

## Check that it worked

```bash
sudo systemctl is-active ninlil
curl http://localhost/status
```

```text
active
{"status":"Healthy","components":null}
```

## Day to day

- **Upgrade:** install the newer package the same way, then `sudo systemctl restart ninlil`. Your
  edits to `/etc/ninlil/ninlil.env` and everything in `/var/lib/ninlil` stay.
- **Remove:** `sudo apt remove ninlil` or `sudo dnf remove ninlil` stops the service and removes
  the program. It keeps `/var/lib/ninlil` and the `ninlil` user. Your edited settings stay as
  `/etc/ninlil/ninlil.env` on Debian and Ubuntu, and are saved as `/etc/ninlil/ninlil.env.rpmsave`
  on Fedora and RHEL.
- **Remove everything:** after `sudo apt purge ninlil` or `dnf remove`, delete the data with
  `sudo rm -r /var/lib/ninlil` and the user with `sudo userdel ninlil`. The package never deletes
  your data.

DNS-over-QUIC needs `libmsquic` 2.4.20 or later. The package suggests it and does not install it.
The README explains why under
[Debian, Ubuntu, Fedora, RHEL and Rocky Linux](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#debian-ubuntu-fedora-rhel-and-rocky-linux).
