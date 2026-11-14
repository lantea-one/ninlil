# TFTP and PXE

This page lets a computer boot from the network. Ninlil serves the boot file over TFTP (the Trivial
File Transfer Protocol, RFC 1350), and a DHCP scope tells the computer which file to fetch and
where to fetch it from. The
[README section](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#tftp-and-pxe)
is the complete reference.

PXE (Preboot Execution Environment) works in two parts. The computer asks DHCP for an address and
for the name and place of a boot file, then fetches the file over TFTP. The TFTP server is
read-only. It serves the files below one root directory, in binary (`octet`) mode, in 512-byte
blocks. It is off by default and listens on UDP port `69`.

## Before you start

1. A running Ninlil, a signed-in API token and a DHCPv4 scope for the network of the computers that
   boot. [DHCPv4 scopes, reservations and leases](dhcp.md) shows both. `<token>` and `<scope-id>`
   are the values from that page; the commands use the Quick start's HTTPS port `18443`.
2. A user that holds `dhcpsettings:write` and `dhcpscopes:write`. The bootstrap administrator holds
   every permission.
3. A directory with the boot files, readable by the user that runs Ninlil. This page uses
   `/srv/tftp` and a file `boot.bin` in it. Check them:

    ```bash
    ls -l /srv/tftp
    ```

    ```text
    -rw-r--r-- 1 root root 1300 Oct 30 19:24 boot.bin
    ```

    (Your owner, size and date differ.)
4. Port `69` is below `1024`: run Ninlil as root or grant `CAP_NET_BIND_SERVICE` (see
   [Ports](../../reference/ports.md)). To try the server without privileges, set
   `NINLIL_DHCP_SERVER_TFTP_PORT` to a high port. A PXE computer always asks port `69`, so use a
   high port for a test only.
5. `curl` with TFTP support, to test the server. Check the protocol list for `tftp`:

    ```bash
    curl --version | grep -o tftp
    ```

    ```text
    tftp
    ```

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Serve the boot files

1. Set the root directory and turn the server on. The settings take effect at the next start:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/tftp -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true,"rootDirectory":"/srv/tftp"}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read once, at startup, so a restart is required before this change takes effect."}
    ```

    The variables are `NINLIL_DHCP_SERVER_TFTP_ENABLE` and
    `NINLIL_DHCP_SERVER_TFTP_ROOT_DIRECTORY` (see
    [ENVIRONMENT.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp)).
    In the console: **Presets & Settings**, **DHCP**, the **TFTP Server** card. Restart Ninlil.
    The **DHCP Services** page starts and stops the server without a restart, once a root
    directory is saved.
2. Read the log. It shows the port the server bound:

    ```text
    The TFTP server started and is now bound to UDP port 69.
    ```

## Point a scope at the boot file

1. Tell the computers which file to fetch. `bootFileName` is the name the computer requests, as the
   TFTP server sees it (relative to the root directory). `nextServerAddress` is the address of the
   TFTP server. Without it, Ninlil sends its Server Identifier address:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-scopes/<scope-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"bootFileName":"boot.bin","nextServerAddress":"192.168.50.2"}'
    ```

    The answer is `204` with no body. In the console: **DHCP Scopes**, edit the scope, **PXE boot
    file name** and **Next server (TFTP) address**.
2. Optional: send a different boot file to computers of one kind. A tag rule can set option `67`
   (the boot file name, text) for a vendor class. This rule matches vendor classes that start with
   `PXEClient:Arch:00007` (read what your computers send in a packet capture), and
   `697078652E656669` is `ipxe.efi`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-tag-rules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"uefi pxe","vendorClassIdentifierMatch":"PXEClient:Arch:00007","vendorClassIdentifierMatchMode":"Prefix","priority":10}'
    curl -k https://127.0.0.1:18443/api/dhcp-tag-rules/<rule-id>/options -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"optionTag":67,"valueHex":"697078652E656669"}'
    ```

    The first answer holds the rule's `id`, which the second command calls `<rule-id>`. In the
    console: **DHCP Tag Rules**.

## Check that it worked

1. Fetch the file from another machine, with the address of the TFTP server:

    ```bash
    curl -s tftp://192.168.50.2/boot.bin -o boot.bin -w '%{size_download}\n'
    ```

    ```text
    1300
    ```

    The number is the size of the file in bytes.
2. Ask for a file that does not exist. `curl` exits with code `68` (TFTP: file not found):

    ```bash
    curl -s tftp://192.168.50.2/missing.bin -o /dev/null -w 'curl exit %{exitcode}\n'
    ```

    ```text
    curl exit 68
    ```

3. Read the scope back. A PXE computer receives these two values in the DHCP reply:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id> -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"id":"<scope-id>","name":"lan", ... ,"bootFileName":"boot.bin","nextServerAddress":"192.168.50.2","kind":"AddressAssignment","delegatedPrefixLength":null}
    ```

4. Check that the server runs. The `Tftp` entry shows `"isRunning":true`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-services -H 'Authorization: Bearer <token>'
    ```

    ```text
    [ ... ,{"kind":"Tftp","enabled":true,"isRunning":true}]
    ```

## How the server behaves

- **It serves one directory.** A file name may use `/` or `\` between directories (Windows PXE
  clients send `\`), and a leading `/` or `\` means the root directory. A name that leaves the root
  with `..`, and a symbolic link whose target or any step on the way lies outside the root, get an
  Access Violation error. The root directory itself may be a link.
- **Read requests, binary mode.** A write request gets the error `This TFTP server is read-only.`
  A `netascii` or `mail` request gets `Only octet (binary) transfer mode is supported.` TFTP
  options such as `blksize` (RFC 2347) are ignored, so every transfer uses 512-byte blocks.
- **Limits.** At most `NINLIL_DHCP_SERVER_TFTP_MAX_CONCURRENT_TRANSFERS` transfers run at once
  (50), and at most four of them belong to one client address. A request over a limit is dropped
  without a reply, because an error packet sent to a forged address would be reflected traffic.
  Ninlil logs one Warning a minute that counts such drops. An unacknowledged block is sent again
  after `NINLIL_DHCP_SERVER_TFTP_RETRANSMIT_TIMEOUT` (2 seconds), up to
  `NINLIL_DHCP_SERVER_TFTP_MAX_RETRANSMISSIONS` times (5).
- **Ninlil does not check the boot file name.** The scope sends `bootFileName` whether or not the
  file exists in the root directory.
- **DHCP carries the boot file in two places.** The scope's boot file name goes into the `file`
  field of every reply, and into option `67` when the device lists option `67` in its Parameter
  Request List. A tag rule that sets option `67` changes the option, not the `file` field. The next
  server address goes into `siaddr`, and into option `66` as text when the device lists it. A
  custom option or tag-rule option `66` must be text; use option `150` for a binary address.
- **A scope allows 128 bytes of boot file name.** This is the size of the `file` field in RFC 2131.

## When something goes wrong

- *`curl` exits with `68`.* The file is missing, or the name does not match. The server's error
  text is `'<name>' does not exist.`
- *The server answers `The requested path is outside this server's configured root directory.`*
  The name uses `..`, or a link leads out of the root. Put the file below the root.
- *Nothing answers.* The log must show `The TFTP server started and is now bound to UDP port 69.`
  If it shows `The TFTP server is enabled but no DhcpServer:Tftp:RootDirectory is configured; it
  will not bind a socket.` or `... root directory '<path>' does not exist; it will not bind a
  socket.`, fix the root directory and restart. Ninlil keeps running.
- *The log has `The TFTP server could not bind its socket: Permission denied` or `Address already
  in use`.* The process may not open port `69`, or another program holds it. Ninlil logs one Error,
  reports `TFTP` with the reason in the `dhcpServices` check of `GET /status`, and keeps running.
  Fix the cause, then start the server again with `POST /api/dhcp-services/Tftp/start`.

## Next steps

- [DHCPv4 scopes, reservations and leases](dhcp.md): scopes, reservations and options.
- [Ports](../../reference/ports.md): the ports Ninlil opens.
- [README: TFTP and PXE](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#tftp-and-pxe):
  the complete reference.
