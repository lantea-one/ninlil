# Docker

The image `git.lantea.one/lantea-one/ninlil` runs the same single program as the other
installations: the DNS and DHCP engines, the REST API and the admin console in one process.

## What the image is

- **Small attack surface.** The base image is the chiseled .NET runtime-deps image: no shell and no
  package manager. It holds the libraries a self-contained .NET program needs, plus `libmsquic`
  for DNS-over-QUIC and its dependency `libnuma1`.
- **Non-root.** The server runs as user ID `1654`. The application files are read-only to it. Only
  `/app/data` is writable.
- **One volume.** `/app/data` holds the database (with the default SQLite engine), the Data
  Protection key ring, the JWT signing key, the certificates and the `appsettings.json` that the
  console's settings pages write to.
- **Platforms.** `linux/amd64` and `linux/arm64`.

| Tag | What it is |
|---|---|
| `latest` | The newest release |
| `<yyyy>.<mm>.<dd>.<increment>` | A specific release, for example `2026.09.24.1` |
| `<yyyy>.<mm>.<dd>.<increment>-<branch>-<short-sha>` | A pre-release build, removed after 24 hours |

## Run it

### Before you start

1. Docker installed. Check it:

    ```bash
    docker --version
    ```

2. Port 53 free on the host. On a Linux desktop `systemd-resolved` usually holds it for its stub
   listener, so publishing `53:53` fails with `address already in use`. Publish another host port
   (`-p 5353:53/udp -p 5353:53/tcp`) or turn the stub listener off.

### Steps

1. Start the container. Always mount a volume at `/app/data`: without it, every encrypted secret
   becomes unreadable when you replace the container.

    ```bash
    docker run -d --name ninlil \
      -p 80:80 -p 443:443 -p 53:53/udp -p 53:53/tcp \
      -v ninlil-data:/app/data \
      git.lantea.one/lantea-one/ninlil:latest
    ```

2. Read the one-time bootstrap administrator password:

    ```bash
    docker logs ninlil
    ```

3. Open `https://<host>/` and sign in as `admin`. The browser warns about the self-signed
   certificate that Ninlil generated on the first start.

Only plain DNS (53) and the web port (80 and 443) are published above. Every other transport and
both DHCP protocols are off by default. Publish their ports after you turn them on (see
[Ports](reference/ports.md)). To set a variable, add `-e NAME=value`. Variable names are in the
[environment variables](reference/environment-variables.md). A .NET runtime setting uses the same
scheme: `-e NINLIL_DOTNET_GC_SERVER=1` selects server garbage collection.

### Check that it worked

```bash
docker logs ninlil
curl http://localhost/status
```

The log ends with `Now listening on: http://[::]:80`, `Now listening on: https://[::]:443` and
`Application started.`. The status call prints:

```text
{"status":"Healthy","components":null}
```

To remove the test container and its data, run `docker rm -f ninlil` and
`docker volume rm ninlil-data`.

## DHCP and Router Advertisement

A DHCP and Router Advertisement server has to see raw broadcast and multicast traffic from the LAN,
which Docker's bridge network does not forward. Run the container on the host's network, as root:

```bash
docker run -d --name ninlil --network host \
  --user 0:0 \
  -e NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true \
  -v ninlil-data:/app/data \
  git.lantea.one/lantea-one/ninlil:latest
```

DHCP and Router Advertisement need two capabilities: `NET_RAW` (the Router Advertisement socket)
and `NET_BIND_SERVICE` (ports 67 and 547). Docker gives both to a container by default, so the
command adds none. If you start the container with `--cap-drop ALL`, add
`--cap-add NET_RAW --cap-add NET_BIND_SERVICE` back. Without `NET_RAW` the Router Advertisement
service logs `could not bind its socket: Permission denied` and the other services keep running.

## A fuller example: Compose

[`docker-compose.yml`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/docker-compose.yml)
at the repository root runs Ninlil against PostgreSQL instead of SQLite, with named volumes. Its
`ninlil-host-network` service, behind the `host-network` profile, shows the DHCP setup. Every
password in it is a placeholder: change them before you use it for anything but a local trial.

```bash
docker compose up --build
docker compose --profile host-network up --build ninlil-host-network postgres
```

The first command starts DNS, the API and the console. The second adds DHCP and Router
Advertisement.

## Build the image yourself

The image only copies prebuilt `ninlil-linux-<arch>` executables, so build those first. The README
lists the steps and the Docker Engine and buildx versions it needs under
[Building the image yourself](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#building-the-image-yourself).

## Next

- [First run](getting-started/first-run.md)
- [Configuration](configuration.md)
