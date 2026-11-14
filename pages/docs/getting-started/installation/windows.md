# Windows

The Windows executable is a console program that also runs as a Windows service. It is unsigned, so
Windows SmartScreen warns the first time you start it. The DNS server, REST API and admin console
work. DHCP, Router Advertisement and passive device presence need Linux kernel interfaces and are
unavailable.

## Before you start

1. A Windows x86-64 machine and an administrator PowerShell.
2. Free ports. The defaults use 53 (UDP and TCP), 80 and 443. To use others, see
   [Ports](../../reference/ports.md).
3. Download `ninlil-windows-amd64.zip` and `SHA256SUMS` from the
   [releases page](https://git.lantea.one/lantea-one/ninlil/releases). The zip holds the executable
   and `msquic.dll`, the library DNS-over-QUIC needs. Both must stay in one folder.
4. Verify the zip before you accept the SmartScreen warning. In PowerShell,
   `(Get-FileHash .\ninlil-windows-amd64.zip).Hash` must equal the `ninlil-windows-amd64.zip` line
   in `SHA256SUMS`, ignoring case.

## Steps: try it from a console

1. Remove the download mark, then unzip into a new folder:

    ```powershell
    Unblock-File .\ninlil-windows-amd64.zip
    Expand-Archive .\ninlil-windows-amd64.zip -DestinationPath C:\Ninlil
    ```

2. Check that `Get-ChildItem C:\Ninlil` lists `ninlil-windows-amd64.exe` and `msquic.dll`.
3. Run `C:\Ninlil\ninlil-windows-amd64.exe`, wait for the log lines and open
   `http://localhost/`. Press Ctrl+C to stop it. It created `C:\Ninlil\data\`.

## Steps: run it as a service

Run these in an administrator PowerShell.

1. Create the data folder. If you already ran the executable from a console, keep that folder: it
   holds your database and keys.

    ```powershell
    New-Item -ItemType Directory -Force C:\Ninlil\data
    ```

2. Create the service. The space after each `=` is required by `sc.exe`:

    ```powershell
    sc.exe create Ninlil binPath= "C:\Ninlil\ninlil-windows-amd64.exe" start= auto DisplayName= "Ninlil DNS server"
    ```

3. Run it as a virtual service account instead of LocalSystem. It needs no password:

    ```powershell
    sc.exe config Ninlil obj= "NT SERVICE\Ninlil"
    icacls C:\Ninlil\data /grant "NT SERVICE\Ninlil:(OI)(CI)M"
    ```

4. Register the event source for the log, let Windows restart the service after a crash, open the
   ports you use and start it:

    ```powershell
    New-EventLog -LogName Application -Source Ninlil.Web
    sc.exe failure Ninlil reset= 86400 actions= restart/5000/restart/5000/restart/60000
    New-NetFirewallRule -DisplayName "Ninlil DNS" -Direction Inbound -Protocol UDP -LocalPort 53 -Action Allow
    New-NetFirewallRule -DisplayName "Ninlil DNS (TCP)" -Direction Inbound -Protocol TCP -LocalPort 53 -Action Allow
    New-NetFirewallRule -DisplayName "Ninlil web" -Direction Inbound -Protocol TCP -LocalPort 80,443 -Action Allow
    Start-Service Ninlil
    ```

## Check that it worked

```powershell
Get-Service Ninlil
Invoke-WebRequest http://localhost/status
```

`Get-Service` shows `Running` a few seconds after `Start-Service`, and the status call answers
`{"status":"Healthy",...}`.

## Behaviour worth knowing

- Keep the program folder outside any user profile (such as `C:\Ninlil` or
  `C:\ProgramData\Ninlil`): a service account cannot read the profile of the user who unzipped it.
- Service settings go in the service's environment. One `NAME=value` line per setting, as listed in
  the [environment variables](../../reference/environment-variables.md), then `Restart-Service Ninlil`.
- Ninlil writes warnings and errors to the Application event log under the source `Ninlil.Web`.
  When a start is refused, the newest entry whose message contains `Startup refused:` names the
  setting. See [Exit codes](../../reference/exit-codes.md).
- Updating means stopping the service, replacing the `.exe` and `msquic.dll` with the files from the
  new zip, and starting it again.

The README holds the rest: removing the service, the service account's key storage and how Ninlil
claims its ports. See
[Windows](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#windows) and
[Ports](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#ports).
