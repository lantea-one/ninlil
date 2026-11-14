# Using the admin console

The admin console is the web application that Ninlil serves on its web ports. This page shows you
how to sign in, find an area in the sidebar, read the dashboard, protect your own account with
two-factor sign-in and understand why a page or a button is missing for an account.

The console is built into the program, so there is nothing to install. It calls the same
[REST API](rest-api.md) as any other client. Everything it shows or changes is checked against the
permissions of the signed-in account.

## Before you start

1. A running Ninlil ([Quick start](getting-started/quick-start.md) or an
   [installation](getting-started/installation/index.md)) and a browser.
2. The web address. The console answers on the web HTTPS port (443 by default; the Quick start uses
   `18443`) and on the plain-HTTP port. Check that it answers:

    ```bash
    curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/
    ```

    ```text
    200
    ```

    `404` means this build has no console. A build from source without the Angular build has none
    ([Build and run from source](getting-started/installation/from-source.md)).
3. A sign-in name and password. On a fresh installation, use the bootstrap administrator
   ([First run](getting-started/first-run.md)).

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](getting-started/quick-start.md#self-signed-certificate).

## Steps

### Sign in

1. Open `https://<host>:<port>/` in the browser. `<host>` is the machine that runs Ninlil and
   `<port>` is the web HTTPS port. The self-signed certificate that Ninlil makes at the first start
   triggers a browser warning. Accept it for a first look, or install a trusted certificate
   ([Security](guides/security/security.md)).
2. On the **Admin sign in** card, enter the user name and the password.
3. Choose **Remember me** only on your own computer. Checked, the session stays signed in after the
   browser closes. Unchecked, it ends with the browser and after 12 hours without activity.
   [`NINLIL_JWT_BROWSER_SESSION_REFRESH_TOKEN_LIFETIME`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso)
   sets the 12 hours.
4. Choose **Sign in**. If the account has two-factor sign-in, the card changes to **Enter your
   verification code**. Pick **Authenticator app**, **Emailed code** (choose **Send code** first) or
   **Recovery code**, enter the code and choose **Verify**.
5. If an administrator configured single sign-on, a second button under **or** carries the identity
   provider's name. It leaves the console for the provider and returns. If that fails, the page
   says "Single sign-on could not complete. Try again, or sign in with your username and password
   below."
6. The console opens the **Dashboard**. A page address you opened before you signed in, such as
   `/zones`, opens after the sign-in.

The sign-in page shows one of these messages when a sign-in fails:

| Message | Cause |
|---|---|
| Incorrect username or password. | The server answered `401`. The account may also be disabled or not exist. |
| This account is locked. Try again later, or ask an administrator to unlock it. | The server answered `423`. This happens only for an account with a stored lockout end date in the future, which Ninlil never sets. |
| Too many sign-in attempts. Try again in `<n>` minutes. | The server answered `429`: the address reached the sign-in limit. |
| Could not reach the server. Check your connection and try again. | The browser got no answer. |

[Troubleshooting](troubleshooting.md) explains each case.

### Find your way around

The console has three parts.

- **The sidebar** on the left lists the areas. The button at its foot collapses it to icons. On a
  narrow screen the menu button in the top bar opens it.
- **The top bar** holds the **Documentation** link (it opens the API reference at `/documentation`
  in a new tab), the colour-scheme switcher and the account menu.
- **The page** fills the rest of the window.

The account menu (your user name, top right) holds **Profile**, **Security** and **Log out**.

The sidebar has one entry for each area. An entry shows only when the account may read it. The
read permission is in the second column; [what a permission hides](#what-a-permission-hides)
explains the rest.

| Sidebar entry | What it holds | Read permission |
|---|---|---|
| Dashboard | Health, counts and the live feed | any signed-in account |
| Zones | Zones, their records and DNSSEC keys | `zones:read` |
| DNS Rewrites | Name-to-address and CNAME overrides | `dnsrewrites:read` |
| TSIG Keys | Keys for transfers and dynamic updates | `tsigkeys:read` |
| Cluster | Cluster nodes | `clusternodes:read` |
| Cloud Credentials | Credentials for cloud DNS sync | `cloudprovidercredentials:read` |
| Filter Lists | Block lists and their entries | `filterlists:read` |
| Clients | Known devices, their schedules and activity | `clients:read` |
| Static Address Mappings | Fixed addresses for clients | `clients:read` |
| Client Groups | Groups of clients and networks | `clientgroups:read` |
| DHCP Services | The DHCPv4, DHCPv6, TFTP and Router Advertisement services | `dhcpservices:read` |
| DHCP Scopes | Scopes and reservations | `dhcpscopes:read` |
| DHCP Tag Rules | Rules that tag requests | `dhcptagrules:read` |
| DHCP Leases | Current leases | `dhcpleases:read` |
| Forwarders | Upstream resolvers | `forwarders:read` |
| Access Control Rules | Which networks may recurse or skip rate limits | `accesscontrol:read` |
| Query Log | Past queries | `querylog:read` |
| Webhooks | Event subscriptions and their deliveries | `webhooks:read` |
| Presets & Settings | Every settings page | the read permission of at least one settings page |
| Users | Accounts | `users:read` |
| Roles | Roles and their permissions | `roles:read` |
| API Tokens | Your own API tokens | any signed-in account |
| Backup & Restore | Backups and restores | `backup:read` |
| Services | Start and stop of the transport listeners and background loops of the running process | `services:read` |

The guides explain each area: [Zones and records](guides/dns/zones-and-records.md),
[Filtering](guides/dns/filtering.md), [DHCP](guides/dhcp/dhcp.md),
[Clustering, events and webhooks](guides/operations/clustering.md) and
[Backup and restore](guides/operations/backup-and-restore.md).

#### Lists and forms

Every area follows the same pattern. A list page shows a table, 25 rows to a page, with a **New
...** button at the top right and, in each row, icons for the row's actions (edit, delete and so
on). A form page has **Save** or **Create** and **Cancel**. A list with no rows shows a short
explanation and the same **New ...** button.

### Read the dashboard

1. Choose **Dashboard** in the sidebar. The page has four blocks and a feed.
2. **Getting Started** lists up to four steps: add your first zone, create a non-admin user, enable
   a DNS transport beyond Plain DNS and configure SMTP for password-reset e-mails. A step that is
   done is marked complete. A step appears only when the account may read what it checks. A dismiss button hides the list.
3. **System Health** has an **Overall** tile and one tile for each component. The badge is
   `Healthy`, `Degraded`, `Unhealthy` or `Unknown`. It is the same answer as
   [`GET /status`](rest-api.md#check-that-ninlil-is-healthy). Without the `status:read` permission,
   only the **Overall** tile shows, with the hint "Sign in with the `status:read` permission to see
   the full per-component breakdown."
4. **At a Glance** counts zones, forwarders, filter lists and webhooks. A dash instead of a number
   means the account may not read that list.
5. **Live Activity** shows the newest 50 events, newest first, with the time and a one-line
   summary. The badge in its header shows the connection: `connecting`, `connected`,
   `reconnecting` or `disconnected`. The feed starts empty on every page load, and the server keeps
   no history. Use the **Query Log** for history.

The feed comes from the [SignalR event hub](rest-api.md#the-event-hub). It carries four event
types: query received, query answered, zone changed and cache evicted. An account receives an
event type only when it holds the permission that reads the same data through the API:
`querylog:read` for the two query types, `zones:read` for zone changes and `recursivesettings:read`
for cache evictions. The server reads the permissions when the connection opens, so a change takes
effect after the next reconnect.

### Change a setting

1. Open **Presets & Settings**. The list on the left groups the settings pages: DNS Resolution &
   Protocol, Filtering & Privacy, Zone Management, Identity & Access, Integrations & Delivery,
   System and DHCP. It lists only the pages the account may read.
2. Choose a page, edit the fields and choose **Save**.
3. Read the note under the form. The server writes it, and it says whether the change applies at
   once ("takes effect immediately -- no restart required") or needs a restart ("a restart is
   required before this change takes effect"). The value is saved in `appsettings.json` and
   survives the restart.
4. If the note asks for a restart, restart Ninlil when it suits you.

A page that shows "You can view these settings but not change them" needs the write permission it
names, such as `ssosettings:write`. A setting that an environment variable fixes stays at the
variable's value; remove the variable to use the saved one. The full list of settings pages and
what each does is in the [README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#using-the-admin-console).

### Protect your own account

1. Open the account menu and choose **Profile**. The page has four cards. **Avatar** shows your
   [Gravatar](https://gravatar.com) when your e-mail address has one, and your initials otherwise.
   **Profile details** changes the display name and phone number. **Change e-mail** asks for the
   new address and your current password. **Change password** asks for the current and the new
   password.
2. Open the account menu and choose **Security**. The page has three cards.
3. **Authenticator app**: choose **Set up authenticator**. Scan the shown URI with an authenticator
   app, or type the shared key into it. Enter a generated code and choose **Confirm & enable**.
   The account then requires two-factor sign-in. **Disable** turns it off again and asks for
   a current code.
4. **Recovery codes**: after you enable two-factor sign-in, choose **Generate recovery codes**.
   Copy the codes from the dialog and keep them somewhere safe. Each works once. Generating a new
   batch invalidates the old one.
5. **E-mailed verification code**: choose **Send code to my e-mail** to get a one-time code at your
   confirmed address. It works only when an administrator configured SMTP
   (**Presets & Settings**, **SMTP**).

A secret opens in a dialog once: a new API token, a TSIG key, a cloud credential, a webhook secret,
a cluster invite, a password reset and your recovery codes. Copy it before you close the dialog;
Ninlil cannot show it again.

A password change, a new authenticator, turning two-factor sign-in off and new recovery codes end
every session of the account. The console signs you out and the sign-in page says what changed.
Sign in again with the new password or a code.

### Forgot the password

1. On the sign-in page, choose **Forgot your password?**.
2. Enter the account's e-mail address. Ninlil sends a link when SMTP is configured.
3. Open the link and choose a new password.

Without SMTP, another administrator resets the password with `POST /api/users/<id>/reset-password`
([Troubleshooting](troubleshooting.md#i-lost-the-password-of-the-administrator)).

## What a permission hides

A permission has the form `<resource>:<action>`, such as `zones:read` or `zones:write`. A role
bundles permissions and a user holds roles ([Security](guides/security/security.md)). The
console reflects them in three ways.

- **The sidebar hides an area** when the account lacks its read permission.
- **A typed address or a bookmark to a hidden page** opens a page titled **Access Denied** that says
  "Your account does not hold the permission required to view this page." with a **Back to
  Dashboard** button. The API call behind the page answers `403`.
- **A page hides or disables its actions** when the account lacks the write permission: no
  **New ...** button, no edit or delete icon, and a disabled toggle where a row can be switched on
  and off.

Two areas have no permission: the **Dashboard** and **API Tokens** open for every signed-in
account. The account's tokens carry the account's permissions.

## Check that it worked

1. Sign in as an account with fewer permissions than the administrator. Its sidebar shows fewer
   entries.
2. Open a page address the account may not read, such as `/users`. The page says **Access Denied**.
3. Confirm that the API refuses the same call without a browser. `<token>` is the access token
   from [REST API](rest-api.md#sign-in) for that account:

    ```bash
    curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/users -H 'Authorization: Bearer <token>'
    ```

    ```text
    403
    ```

## Common mistakes

- **`/` answers `404`.** The program has no console. Use the published binary, the packages or the
  container image.
- **The browser warns about the certificate.** The self-signed certificate is the cause. Accept it,
  or install a trusted certificate.
- **Save is disabled.** Eight settings pages send only what you changed. Edit a field first.
- **A page is missing from the sidebar.** The account lacks the read permission. Ask an
  administrator for the role.
- **The sign-in page says "Too many sign-in attempts."** The address reached the sign-in limit.
  Wait for the time it names.

## Next steps

- [REST API](rest-api.md): do the same things with `curl`.
- [Security](guides/security/security.md): roles, permissions and two-factor sign-in in depth.
- [Troubleshooting](troubleshooting.md): a sign-in that fails or a listener that is down.
- The [README section](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#using-the-admin-console)
  for behaviour worth knowing, such as which settings pages send only changed fields.
