# Security policy

## Report a vulnerability

Send the report by e-mail to [git+security@lantea.one](mailto:git+security@lantea.one). There is no
PGP key, so the message is not encrypted end to end. Leave secrets such as working credentials
out of it.

Do not report a vulnerability in a public issue, a pull request or a discussion. Those are visible
to everyone before a fix exists.

## Supported versions

Only the latest release receives security fixes. A fix ships in a new release; older releases are
not patched, so upgrade to the latest one.

## What to include

1. The Ninlil version: the release name or the container tag (see
   [Releases and versioning](./README.md#releases-and-versioning)).
2. How you run Ninlil: binary, package or container image, the operating system and the storage
   engine.
3. What the problem is and what an attacker gains (for example reading data, signing in without a
   password, or stopping the service).
4. The steps to reproduce it, with the requests or the configuration involved.
5. Anything you already know about a fix.

## Private handling

Every report is read and answered as soon as possible. There are no promised deadlines for the
answer or the fix. Please keep the details private until a fix is released; the
[README](./README.md#releases-and-versioning) explains how releases are versioned and published.

When the fix is released, its security note in the release notes (`CHANGELOG.md`) names the
reporter, unless the reporter asks not to be named.

Ninlil's accepted risks and security behaviour are described in the README's
[Security](./README.md#security) guide; check it first, because a documented behaviour is not a
vulnerability.

To change Ninlil rather than report a problem, read [`CONTRIBUTING.md`](./CONTRIBUTING.md).
