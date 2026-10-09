# DNSSEC canonical form vectors

`dnssec-canonical-vectors.json` holds RRsets signed by another implementation (dnspython 2.8.0,
Ed25519, fixed times). `RrsetSignatureVerifierCanonicalFormTests` only reads it; nothing generates
vectors at test time. Never edit a vector by hand. If dnspython and the RFC text disagree, follow
the RFC and investigate.

## Regenerating

Run from the repository root. The container needs network once, for pip.

```bash
V=src/dotnet/tests/Ninlil.Dns.Server.Recursive.Tests/Vectors

docker run --rm -v "$PWD/$V:/v:Z" -w /v python:3.13-slim sh -c \
  "pip install --no-cache-dir 'dnspython[dnssec]==2.8.0' >/dev/null 2>&1 && python -I generate_dnssec_canonical_vectors.py" \
  > $V/dnssec-canonical-vectors.json
```

The output is reproducible: the key comes from a fixed seed, Ed25519 is deterministic and the times
are fixed.

## Vector fields

`id`, `source`, `ownerName` (the name the RRset is presented under), `type`, `rdataHex` (one wire
RDATA per record), `rrsigHex` (the RRSIG RDATA), `dnskeyHex` (the DNSKEY RDATA, apex `example.` or
`example.com.` as the signer) and `now` (Unix seconds inside the validity window).

## Set

| Id | Covers |
|---|---|
| `nsec-mixed-case-next` | An NSEC whose Next Domain Name is `B.Example.`: the signed bytes keep its case (RFC 6840 §5.1) |
| `signer-mixed-case` | An RRSIG whose Signer's Name is `Example.COM.`: the signed bytes use lower case (RFC 4034 §3.1.8.1) |
| `wildcard-expansion` | An A RRset signed at `*.example.com.` (Labels 2) and presented as `x.y.example.com.` (RFC 4035 §5.3.2) |
