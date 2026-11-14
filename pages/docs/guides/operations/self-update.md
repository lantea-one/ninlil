# Self-update

This page lets you switch on the update notifier, which tells you on the console's dashboard when a
newer Ninlil release exists, and shows how to apply an update. Ninlil never installs anything: you
replace the program yourself, the way you installed it. The README holds the complete reference:
[Self-update](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#self-update).

The check reads a small JSON document, the **release feed**, from an address you set. Ninlil
publishes no feed and has no built-in address, so the check does nothing until you set
[`NINLIL_DNS_SERVER_SELF_UPDATE_RELEASE_FEED_URL`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#webhooks-backup-self-update--clustering).
Host a feed yourself, on any web server, when you want the notice.

## Before you start

1. A web server that serves a JSON file over HTTP or HTTPS and that the Ninlil host reaches.
2. An account with `selfupdatesettings:write` to set the address, `selfupdate:manage` to start a check
   and `selfupdate:read` to read the result (the bootstrap administrator has them), and its access
   token, called `<token>` below. The commands use the HTTPS port `18443` from the
   [quick start](../../getting-started/quick-start.md).
3. The version your Ninlil runs. A release build reports a date-based version such as `2026.9.24.1`;
   a build from source reports `0.0.0.0`. Read it:

    ```bash
    curl -k https://127.0.0.1:18443/api/self-update -H "Authorization: Bearer <token>"
    ```

    ```text
    {"configured":false,"currentVersion":"0.0.0.0","latestKnownVersion":null,"updateAvailable":false,"releaseUrl":null,"releaseNotes":null,"lastCheckedAtUtc":"...","lastCheckError":null,"updateInstructions":"No release feed is configured yet -- set one in Self-Update Settings to enable checking for a newer release."}
    ```

## Set up the notifier

1. Write the feed file. Only `version` is required. Write the version in the same numeric form as
   `currentVersion` (`yyyy.m.d.increment`), not the release's `v...R...` name:

    ```json
    {
      "version": "2026.9.24.1",
      "releaseUrl": "https://example.com/releases/2026.9.24.1",
      "publishedAtUtc": "2026-09-24T00:00:00Z",
      "notes": "Free text shown with the notice."
    }
    ```

    Serve it at an address such as `https://example.com/ninlil-feed.json`. For a test, save it as
    `feed.json` and run this in that folder, then use `http://127.0.0.1:18098/feed.json` as the address.
    Press Ctrl+C to stop the test server:

    ```bash
    python3 -m http.server 18098 --bind 127.0.0.1
    ```

2. Set the address (`<feed-url>`). Ninlil checks that it is an absolute HTTP or HTTPS address and does
   not fetch it yet. The change reaches the running server within about a second:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/self-update-settings -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"releaseFeedUrl":"<feed-url>"}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read live, so it takes effect immediately -- no restart required."}
    ```

    The console page is **Settings**, **Self-Update** (`/settings/self-update`). To set it from the
    environment, use the variable above and restart.
3. Run a check at once instead of waiting for the next one:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/self-update/check -H "Authorization: Bearer <token>"
    ```

## Check that it worked

The check call returns the result. With a feed that names a version above the running one:

```text
{"configured":true,"currentVersion":"0.0.0.0","latestKnownVersion":"2026.9.24.1","updateAvailable":true,"releaseUrl":"https://example.com/releases/2026.9.24.1","releaseNotes":"Free text shown with the notice.","lastCheckedAtUtc":"2026-11-14T02:55:18.4033862+00:00","lastCheckError":null,"updateInstructions":"Version 2026.9.24.1 is available (currently running 0.0.0.0). Download the new release from https://example.com/releases/2026.9.24.1, replace this server's own binary, and restart it -- this project has no automatic in-place updater, so applying an update is always a manual step."}
```

(your values differ). The dashboard shows the same notice.

## Apply an update

When the notice says a newer version is available, install it the way you installed Ninlil. Ninlil
has no action that applies an update. [Upgrades](../../upgrades.md) has the steps for each kind of
installation, the snapshot Ninlil takes before it migrates the database and the way back.

## Behaviour worth knowing

- **When checks run.** One check runs at startup, then one every
  [`NINLIL_DNS_SERVER_SELF_UPDATE_CHECK_INTERVAL`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#webhooks-backup-self-update--clustering)
  (24 hours by default). The checker wakes every 15 minutes, so the wait rounds up to the next
  15-minute tick. With
  [`NINLIL_DNS_SERVER_SELF_UPDATE_ENABLE_AUTOMATIC_CHECK`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#webhooks-backup-self-update--clustering)
  off, only a manual check runs.
- **A failed check is retried after 15 minutes.** A failure keeps the last good result visible and sets
  `lastCheckError`. An `updateAvailable` that a check found stays `true` through an outage.
- **The result is held in memory.** After a restart, the status is empty until the startup check
  finishes.
- **Only `version` decides.** The check reports an update when the feed's version is strictly newer than
  the running one. Ninlil never opens `releaseUrl`.
- **No feed address, no request.** The status reads `configured: false` and no failure is logged.
- **The address is not limited.** The feed address can name any host the server can reach, so give
  `selfupdatesettings:write` only to people you trust.

## When something goes wrong

- *The settings call answers `400` with `'<feed-url>' is not a valid absolute HTTP or HTTPS URL.`* Write
  the address with `http://` or `https://`.
- *`lastCheckError` reads `Response status code does not indicate success: 404 (File not found).`* The
  address is wrong or the file is missing. Open the address in a browser from the Ninlil host.
- *`lastCheckError` reads `The release feed document is missing a 'version' field.`* Add the field to the
  feed.
- *`lastCheckError` reads `'<version>' is not a valid dotted version string.`* Write the version as
  numbers separated by dots, for example `2026.9.24.1`.
- *The status stays `configured: false`.* The address is empty. Run step 2.

## Next steps

- [Upgrades](../../upgrades.md)
- [Backup and restore](backup-and-restore.md): take a backup before you update.
- [Security](../security/security.md)
