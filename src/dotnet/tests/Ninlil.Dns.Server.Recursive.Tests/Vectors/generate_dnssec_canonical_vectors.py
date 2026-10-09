# Generates the dnspython known-answer vectors for the DNSSEC canonical form (RFC 4034 §6,
# RFC 4035 §5.3.2, RFC 6840 §5.1): an NSEC with a mixed-case Next Domain Name, an RRSIG with a
# mixed-case Signer's Name and a wildcard expansion.
#
# Pinned library: dnspython 2.8.0 (signs with dns.dnssec.sign; Ed25519 signatures are
# deterministic, so the output is reproducible; the wall clock is never used).
#
# Command (run from the repository root; needs network once, for pip):
#   docker run --rm -v "$PWD/src/dotnet/tests/Ninlil.Dns.Server.Recursive.Tests/Vectors:/v:Z" -w /v \
#     python:3.13-slim sh -c "pip install --no-cache-dir 'dnspython[dnssec]==2.8.0' >/dev/null 2>&1 && \
#     python -I generate_dnssec_canonical_vectors.py > dnssec-canonical-vectors.json"
#
# Input: none. Output (stdout): a JSON array; see README.md for the fields.

import hashlib
import json

import dns.dnssec
import dns.name
import dns.rdata
import dns.rdataclass
import dns.rdatatype
import dns.rrset
from cryptography.hazmat.primitives.asymmetric import ed25519

assert __import__("dns.version").version.version == "2.8.0", "dnspython 2.8.0 is pinned"

INCEPTION = 1700000000
EXPIRATION = 1800000000
NOW = 1750000000

seed = hashlib.sha256(b"ninlil-dnssec-canonical-vector").digest()
private_key = ed25519.Ed25519PrivateKey.from_private_bytes(seed)
dnskey = dns.dnssec.make_dnskey(
    private_key.public_key(), dns.dnssec.Algorithm.ED25519, flags=256 | 1
)


def wire_hex(rdata):
    return rdata.to_wire().hex()


def vector(vector_id, signed_owner, presented_owner, signer, rtype, rdata_texts):
    rrset = dns.rrset.from_text_list(signed_owner, 3600, "IN", rtype, rdata_texts)
    rrsig = dns.dnssec.sign(
        rrset,
        private_key,
        dns.name.from_text(signer),
        dnskey,
        inception=INCEPTION,
        expiration=EXPIRATION,
    )
    # dnspython must accept its own signature, or the vector is meaningless.
    dns.dnssec.validate_rrsig(rrset, rrsig, {dns.name.from_text(signer): dns.rrset.from_rdata(signer, 3600, dnskey)}, now=NOW)
    return {
        "id": vector_id,
        "source": "dnspython 2.8.0",
        "ownerName": presented_owner,
        "type": rtype,
        "rdataHex": [wire_hex(r) for r in rrset],
        "rrsigHex": wire_hex(rrsig),
        "dnskeyHex": wire_hex(dnskey),
        "now": NOW,
    }


vectors = [
    # C2-7: the Next Domain Name keeps its case in the signed bytes (RFC 6840 §5.1).
    vector("nsec-mixed-case-next", "a.example.", "a.example.", "example.", "NSEC", ["B.Example. A RRSIG NSEC"]),
    # C2-8: the RRSIG carries the signer in mixed case; the signed bytes use lower case.
    vector("signer-mixed-case", "www.example.com.", "www.example.com.", "Example.COM.", "A", ["192.0.2.1"]),
    # (a): signed at the wildcard (Labels 2), presented as the expansion x.y.example.com.
    vector("wildcard-expansion", "*.example.com.", "x.y.example.com.", "example.com.", "A", ["192.0.2.7"]),
]
print(json.dumps(vectors, indent=2))
