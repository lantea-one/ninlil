# Ninlil

Ninlil is a DNS, DHCPv4, DHCPv6 and Router Advertisement server in one program, with a REST API and
a web admin console. It is one option among the alternatives in these categories. The
[README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md) is the complete
reference; these pages carry the tasks and link to it for the detail.

## What do you want to do?

| You want to | Go to |
|---|---|
| Learn what Ninlil is and does | [What Ninlil is](getting-started/what-is-ninlil.md) |
| Try it in ten minutes, on one machine, without changing anything outside one folder | [Quick start](getting-started/quick-start.md) |
| Install it as a service or from a package | [Installation](getting-started/installation/index.md) |
| Know what happens the first time it starts, and where its files go | [First run](getting-started/first-run.md) |
| Run it in a container | [Docker](docker.md) |
| Change a setting, or find the name of an environment variable | [Configuration](configuration.md) and the [environment variables](reference/environment-variables.md) |
| Serve zones, filter queries, or turn on an encrypted transport | [Guides](guides/index.md) (DNS) |
| Hand out addresses and boot files | [Guides](guides/index.md) (DHCP) |
| Harden an installation or sign in with single sign-on | [Guides](guides/index.md) (Security) |
| Back up, cluster, move databases or update | [Guides](guides/index.md) (Operations) and [Upgrades](upgrades.md) |
| Look up a port or an exit code | [Ports](reference/ports.md) and [Exit codes](reference/exit-codes.md) |
| Find out why it does not start | [Troubleshooting](troubleshooting.md) |
| Change the code | [CONTRIBUTING.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/CONTRIBUTING.md) |

## How these pages are written

- Each page lists what it assumes, then the commands, then the expected result.
- A command is ready to copy. A word in `<angle-brackets>` is a placeholder the page explains.
- The [README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md) holds the
  complete text of every feature; a page links to it where the detail is long.
- Ninlil is MIT-licensed. The [clean-room statement](https://git.lantea.one/lantea-one/ninlil/src/branch/main/NOTICE.md)
  says how it was built.
