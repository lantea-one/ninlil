# Installation

Every release, and every pre-release build, is published from the project's Forgejo pipeline on the
[releases page](https://git.lantea.one/lantea-one/ninlil/releases). Pick the row that fits.

| You want | Use |
|---|---|
| To try Ninlil on Linux or macOS in minutes | [Download a binary](binary.md) |
| Ninlil as a Windows service | [Windows](windows.md) |
| An Arch Linux package | [Arch Linux](arch-linux.md) |
| A service on Debian, Ubuntu, Fedora, RHEL, Rocky Linux or AlmaLinux | [Debian, Ubuntu, Fedora, RHEL and Rocky Linux](deb-and-rpm.md) |
| A container | [Docker](../../docker.md) |
| To change the code | [Build and run from source](from-source.md) |

## Find the release tag

Every download address contains the release's tag, written `<tag>` on these pages. Open the
[releases page](https://git.lantea.one/lantea-one/ninlil/releases): each release is named by its
tag. A release tag has the form `v<yyyy>.<mm>.<dd>.<increment>R<short-sha>`. The tag includes the
leading `v` and the `R<short-sha>` part, and a release address without them answers 404. A complete
address for the tag `v2026.09.24.1Rab12cd3` is:

```text
https://git.lantea.one/lantea-one/ninlil/releases/download/v2026.09.24.1Rab12cd3/ninlil-linux-amd64
```

A package file name holds the plain date version instead, written `<version>`: the tag without the
`v` and without `R<short-sha>`, such as `2026.09.24.1`.

## Before every installation

1. Check that port 53 is free. On Linux, this prints only a header line when it is:

    ```bash
    ss -lntu 'sport = :53'
    ```

    If it lists a listener, another program uses port 53 and Ninlil cannot bind it. Stop that
    program or choose another Ninlil port (see [Ports](../../reference/ports.md)). On a Linux
    desktop the program is usually the `systemd-resolved` stub listener.
2. Remember that ports 53, 80 and 443 are privileged on Linux and macOS. Run Ninlil as root, or
   choose other ports.

## After every installation

Read [First run](../first-run.md): it shows the log lines, the one-time administrator password and
the files Ninlil creates.
