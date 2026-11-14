# Download a binary

Each release carries a self-contained, single-file executable per platform. No .NET installation is
needed: the runtime, every native library and the admin console are inside the one file.

| Platform | File |
|---|---|
| Linux x86-64 | `ninlil-linux-amd64` |
| Linux ARM64 | `ninlil-linux-arm64` |
| macOS Intel | `ninlil-macos-amd64` |
| macOS Apple Silicon | `ninlil-macos-arm64` |
| Windows x86-64 | `ninlil-windows-amd64.zip` (see [Windows](windows.md)) |

## Before you start

1. Choose a folder the user that runs Ninlil can write to, for example `/opt/ninlil`. Ninlil keeps
   all of its state in a `data/` directory next to the executable.
2. Check the ports as described in [Installation](index.md#before-every-installation).

## Steps

1. Download the executable and the checksum list. Replace `<version>` with the release's version:

    ```bash
    curl -LO https://git.lantea.one/lantea-one/ninlil/releases/download/<version>/ninlil-linux-amd64
    curl -LO https://git.lantea.one/lantea-one/ninlil/releases/download/<version>/SHA256SUMS
    ```

2. Check the download. The command must print `OK` for your file:

    ```bash
    sha256sum --check --ignore-missing SHA256SUMS
    ```

    ```text
    ninlil-linux-amd64: OK
    ```

3. Make the file executable:

    ```bash
    chmod +x ninlil-linux-amd64
    ```

4. Start it. `sudo` allows the privileged default ports. For a trial without root, set other ports
   as shown in the [quick start](../quick-start.md):

    ```bash
    sudo ./ninlil-linux-amd64
    ```

## Check that it worked

In a second terminal (use your own HTTP port if you changed it):

```bash
curl http://localhost/status
```

```text
{"status":"Healthy","components":null}
```

## Behaviour worth knowing

- On the first start, the native libraries inside the file are extracted once to `~/.net/`. Set
  `DOTNET_BUNDLE_EXTRACT_BASE_DIR` to use another folder.
- A systemd unit to start from lives in
  [`src/script/systemd/`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/src/script/systemd).
- On macOS, the binaries are ad-hoc signed and not notarized: remove the download quarantine flag
  once with `xattr -d com.apple.quarantine ninlil-macos-arm64` before the first run. The DNS server,
  REST API and admin console work. DHCP, Router Advertisement and passive device presence need
  Linux kernel interfaces and are unavailable. The README lists the DNS-over-QUIC requirements
  under
  [Behaviour worth knowing](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#download-a-binary).
