# Arch Linux

Every release carries installable `ninlil` packages for `x86_64` and `aarch64`. The package installs
a `ninlil` systemd service that runs as an unprivileged user, with its state in `/var/lib/ninlil`.

## Before you start

1. An Arch Linux host with `sudo` and `pacman`.
2. Port 53 free ([Installation](index.md#before-every-installation)).

## Steps

1. Install the package and start the service. Replace `<tag>` with the release's tag and `<pkgver>`
   with its date version ([Find the release tag](index.md#find-the-release-tag)):

    ```bash
    sudo pacman -U https://git.lantea.one/lantea-one/ninlil/releases/download/<tag>/ninlil-<pkgver>-1-x86_64.pkg.tar.zst
    sudo systemctl enable --now ninlil
    ```

2. Read the bootstrap administrator's one-time password:

    ```bash
    sudo journalctl -u ninlil
    ```

## Check that it worked

```bash
curl http://localhost/status
```

```text
{"status":"Healthy","components":null}
```

## Behaviour worth knowing

- The package lists `libmsquic` (the `msquic` package) as an optional dependency and does not
  install it. DNS-over-QUIC needs version 2.4.20 or later.
- [`src/archlinux/`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/src/archlinux) also
  holds the AUR PKGBUILDs that build from source: `ninlil-git-stable` (the newest release branch)
  and `ninlil-git-next` (the latest `develop` commit).
