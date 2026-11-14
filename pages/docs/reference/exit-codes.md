# Exit codes

Code 78 is the only exit code Ninlil sets itself. It means Ninlil refused to start on purpose.
Any other non-zero code comes from the .NET runtime or the operating system, and the log holds the
cause.

## Exit code 78: the start is refused

Ninlil exits with **78** (`EX_CONFIG`) and logs a critical entry that begins with
`Startup refused:` when it must not run with what it finds. Causes:

- the database was upgraded by a newer Ninlil version;
- a database migration failed;
- the snapshot taken before a migration could not be written;
- storage settings contradict each other;
- the JWT signing key is too short;
- a Data Protection key ring setting is wrong, for example a wrong certificate password.

The entry names the setting or the file to fix. It never prints a password. The README lists every
cause and what to do under
[When the start is refused](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#when-the-start-is-refused-exit-code-78).

### Before you start

You can reach the log of the Ninlil process that refused to start.

### Steps

1. Read the message. Use the command for your installation:

    | Installed as | Command |
    |---|---|
    | Linux package | `sudo journalctl -u ninlil -e` |
    | Docker | `docker logs ninlil` |
    | Binary or `dotnet run` | the terminal output |
    | Windows service | `Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Ninlil.Web'} -MaxEvents 5 \| Format-List TimeCreated, Message` |

2. Find the entry that contains `Startup refused:` and follow what it says.
3. Start Ninlil again. On a systemd host, run `sudo systemctl restart ninlil`.

### Check that it worked

For a short JWT signing key, for example `NINLIL_JWT_SIGNING_KEY=short`, the log holds this line
and the process exits with 78 (the numbers in your message follow your key):

```text
Startup refused: The JWT signing key is 5 bytes long, and HMAC-SHA256 needs at least 16. Set NINLIL_JWT_SIGNING_KEY to a random value of 32 bytes or more. If the key comes from data/jwt-signing-key.txt, delete that file so Ninlil generates a new key (this signs every user out).
```

After you fix the cause, `curl http://localhost/status` answers
`{"status":"Healthy","components":null}`.

## What restarts a refused start

| Supervisor | Behavior |
|---|---|
| systemd, with the units Ninlil ships (the packages and `src/script/systemd/ninlil-dns.service`) | `RestartPreventExitStatus=78` stops systemd from retrying. `systemctl status ninlil` shows `status=78`. If you write your own unit, add the same line. |
| Docker restart policies such as `unless-stopped` | The container restarts and the refusal repeats. It does not change the database. Read the log and fix the cause. |
| Windows service | The service stops before it connects to the Service Control Manager, so `Start-Service` fails with "Cannot start service" and `sc.exe query Ninlil` shows `STATE: STOPPED` with exit codes 0. The code 78 appears only in a console run of the executable. The recovery actions restart the service whatever the code. |

## Related

- [Troubleshooting](../troubleshooting.md)
- [Upgrades](../upgrades.md)
