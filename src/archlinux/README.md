# Arch Linux packages

Three packages install Ninlil the same way — the server at `/usr/lib/ninlil/ninlil`, a
`ninlil.service` systemd unit running it as the unprivileged `ninlil` user, all state in
`/var/lib/ninlil`, and its environment file at `/etc/ninlil/ninlil.env` — and differ only in where
the executable comes from:

| Package | Directory | The executable |
| --- | --- | --- |
| `ninlil` | [`ninlil/`](./ninlil/) | The release binary (`ninlil-linux-amd64` / `ninlil-linux-arm64`), attached to every release as a ready-built package |
| `ninlil-git-stable` | [`ninlil-git-stable/`](./ninlil-git-stable/) | Built from source: the newest `v<yyyy>.<mm>.<dd>.<increment>` release branch (AUR) |
| `ninlil-git-next` | [`ninlil-git-next/`](./ninlil-git-next/) | Built from source: the latest `develop` commit (AUR) |

Both AUR packages provide and conflict with `ninlil`, so only one of the three is ever installed.
[`common/`](./common/) holds the systemd unit, the `sysusers.d` entry, and the default environment
file they all install.

## `ninlil`

Every pipeline build attaches ready-built `ninlil` packages to its release, for `x86_64` and
`aarch64`, so installing a release is one command:

```bash
sudo pacman -U https://git.lantea.one/Lantea.One/ninlil/releases/download/<tag>/ninlil-<pkgver>-1-x86_64.pkg.tar.zst
```

`ninlil/PKGBUILD.in` is the template they are built from: `.forgejo/scripts/arch-packages.sh`
fills in the build's version, download URLs, and checksums, then builds both architectures in an
Arch Linux container from the binaries that same pipeline run produced. A release's `pkgver` is its
plain date version (`2026.09.24.1`); a branch build appends the branch and commit
(`2026.09.24.3.develop.a1b2c3d`).

## `ninlil-git-stable` and `ninlil-git-next`

The two AUR packages, each PKGBUILD used as-is. Both clone the repository and build the admin
console and the self-contained single-file executable exactly as the pipeline does, and both
compute their `pkgver` at build time, so rebuilding picks up new commits without any edit:

- `ninlil-git-stable` checks out the newest release branch and takes its `pkgver` from the
  branch's name (`2026.09.24.1`), the same as that release's own `ninlil` package.
- `ninlil-git-next` builds `develop`'s latest commit, versioned as the pipeline versions a
  `develop` build: the commit's UTC date, its position among that day's commits, and its hash
  (`2026.09.24.3.develop.a1b2c3d`).

They need `dotnet-sdk`, `nodejs`, `npm`, and a Java runtime to build. After changing either one,
regenerate its `.SRCINFO` with `makepkg --printsrcinfo > .SRCINFO`.

## After installing any of them

```bash
sudoedit /etc/ninlil/ninlil.env   # optional: storage engine, bootstrap password, ports
sudo systemctl enable --now ninlil
journalctl -u ninlil              # the bootstrap administrator's password is printed once here
```

The unit grants `CAP_NET_BIND_SERVICE`, `CAP_NET_RAW`, and `CAP_NET_ADMIN`, so the unprivileged
user can serve DNS on port 53, the admin console on 80/443, and DHCP/Router Advertisement.
