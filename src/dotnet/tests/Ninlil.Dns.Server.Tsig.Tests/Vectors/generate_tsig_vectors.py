# Generates the dnspython half of the TSIG known-answer vectors.
#
# Pinned library: dnspython 2.8.0 (the TSIG digest is computed by dns.tsig.sign, with the time and
# the chaining fixed explicitly; the wall clock is never used).
#
# Command (run from the repository root; needs network once, for pip):
#   docker run --rm -v "$PWD/src/dotnet/tests/Ninlil.Dns.Server.Tsig.Tests/Vectors:/v:Z" -w /v \
#     python:3.13-slim sh -c "pip install --no-cache-dir dnspython==2.8.0 && \
#     python -I generate_tsig_vectors.py > tsig-vectors.dnspython.json"
#
# Input: none. Secrets are derived from the vector ids, so the output is reproducible.
# Output (stdout): a JSON array of vectors, see README.md for the fields.

import base64
import hashlib
import io
import json
import struct
import sys

import dns.flags
import dns.message
import dns.name
import dns.rdataclass
import dns.rdatatype
import dns.rdtypes.ANY.TSIG
import dns.rrset
import dns.tsig
import dns.rcode
import dns.update

assert __import__("dns.version").version.version == "2.8.0", "dnspython 2.8.0 is pinned"

TIME = 1700000000
FUDGE = 300
ALGORITHMS = {
    "hmac-sha1": dns.tsig.HMAC_SHA1,
    "hmac-sha256": dns.tsig.HMAC_SHA256,
    "hmac-sha384": dns.tsig.HMAC_SHA384,
    "hmac-sha512": dns.tsig.HMAC_SHA512,
}
vectors = []


def secret_for(seed, algorithm):
    size = {"hmac-sha1": 20, "hmac-sha256": 32, "hmac-sha384": 48, "hmac-sha512": 64}[algorithm]
    return hashlib.sha512(b"ninlil-tsig-vector-" + seed.encode()).digest()[:size]


class Signer:
    """Signs messages for one key with the dnspython low-level API."""

    def __init__(self, key_name, algorithm, secret):
        self.key_name_text = key_name
        self.algorithm = algorithm
        self.secret = secret
        self.key = dns.tsig.Key(dns.name.from_text(key_name), secret, ALGORITHMS[algorithm])

    def rdata(self, original_id, time_signed, error=0, other=b""):
        return dns.rdtypes.ANY.TSIG.TSIG(
            dns.rdataclass.ANY,
            dns.rdatatype.TSIG,
            ALGORITHMS[self.algorithm],
            time_signed,
            FUDGE,
            b"",
            original_id,
            error,
            other,
        )

    def sign(self, wire, original_id, time_signed, request_mac=None, ctx=None, multi=False,
             error=0, other=b""):
        """Returns (signed_wire, mac, next_ctx). wire has no TSIG and carries original_id."""
        rdata = self.rdata(original_id, time_signed, error, other)
        signed_rdata, next_ctx = dns.tsig.sign(
            wire, self.key, rdata, time_signed, request_mac, ctx, multi
        )
        rdata_io = io.BytesIO()
        signed_rdata.to_wire(rdata_io)
        rdata_bytes = rdata_io.getvalue()
        record = (
            self.key.name.to_wire()
            + struct.pack("!HHIH", dns.rdatatype.TSIG, dns.rdataclass.ANY, 0, len(rdata_bytes))
            + rdata_bytes
        )
        arcount = struct.unpack("!H", wire[10:12])[0]
        signed = wire[:10] + struct.pack("!H", arcount + 1) + wire[12:] + record
        return signed, signed_rdata.mac, next_ctx


def add(vector_id, kind, signer, time_signed, signed, mac, unsigned, **extra):
    vector = {
        "id": vector_id,
        "source": "dnspython 2.8.0 dns.tsig.sign",
        "kind": kind,
        "keyName": signer.key_name_text,
        "algorithm": signer.algorithm,
        "secretBase64": base64.b64encode(signer.secret).decode(),
        "timeSigned": time_signed,
        "fudge": FUDGE,
        "unsignedHex": unsigned.hex(),
        "wireHex": signed.hex(),
        "macHex": mac.hex(),
    }
    vector.update(extra)
    vectors.append(vector)


def soa_query(msg_id):
    query = dns.message.make_query("example.", "SOA", use_edns=False)
    query.id = msg_id
    query.flags = 0
    return query


# The SOA names share no suffix with each other or with the zone, so dnspython does not compress
# them. Ninlil's writer never compresses RDATA names, and the sign tests compare unsigned bytes.
SOA_RDATA = "ns.example.net. hostmaster.example.org. 1 3600 600 86400 300"


def soa_answer(query):
    response = dns.message.make_response(query)
    response.flags |= dns.flags.AA
    response.answer.append(
        dns.rrset.from_text(
            "example.", 3600, "IN", "SOA", SOA_RDATA,
        )
    )
    return response


def request_vector(vector_id, key_name, algorithm, msg_id, time_signed, build=soa_query):
    signer = Signer(key_name, algorithm, secret_for(vector_id, algorithm))
    unsigned = build(msg_id).to_wire()
    signed, mac, _ = signer.sign(unsigned, msg_id, time_signed)
    add(vector_id, "request", signer, time_signed, signed, mac, unsigned, originalId=msg_id)
    return signer, signed, unsigned, mac, build(msg_id)


def response_vector(vector_id, signer, query, request_mac, time_signed, request_id, **kw):
    response = soa_answer(query)
    unsigned = response.to_wire()
    signed, mac, _ = signer.sign(unsigned, request_id, time_signed, request_mac)
    add(vector_id, "first-response", signer, time_signed, signed, mac, unsigned,
        requestMacHex=request_mac.hex(), originalId=request_id, **kw)
    return signed, mac


# V1, V2: SOA query, plain and mixed-case key names.
s1, v1_signed, v1_unsigned, v1_mac, v1_query = request_vector(
    "V1", "transfer-key.", "hmac-sha256", 0x1234, TIME)
s2, _, _, _, _ = request_vector("V2", "Mixed.Case.Key.", "hmac-sha256", 0x1234, TIME)

# V3: V1 after a forwarder rewrote the header ID; the Original ID stays 0x1234.
v3 = bytearray(v1_signed)
v3[0:2] = struct.pack("!H", 0xBEEF)
add("V3", "request", s1, TIME, bytes(v3), v1_mac, v1_unsigned, originalId=0x1234)

# V4: NOERROR SOA answer to V1.
response_vector("V4", s1, v1_query, v1_mac, TIME, 0x1234)

# V7: BADTIME reply to V1, signed as a first response; Time Signed is the request's, Other Data
# is the server time (request time + 600).
badtime = dns.message.make_response(v1_query)
badtime.set_rcode(dns.rcode.NOTAUTH)
unsigned = badtime.to_wire()
other = struct.pack("!HI", 0, TIME + 600)
signed, mac, _ = s1.sign(unsigned, 0x1234, TIME, v1_mac, error=18, other=other)
add("V7", "badtime-response", s1, TIME, signed, mac, unsigned,
    requestMacHex=v1_mac.hex(), originalId=0x1234, otherDataHex=other.hex(), errorCode=18)


def axfr(prefix, key_name, algorithm, msg_id):
    """Request, m1 (request MAC), m2 (prior m1), m3 unsigned, m4 (prior m2 + m3)."""

    def build(i):
        q = dns.message.make_query("example.", "AXFR", use_edns=False)
        q.id = i
        q.flags = 0
        return q

    signer, _, _, req_mac, query = request_vector(prefix + "-req", key_name, algorithm, msg_id, TIME,
                                                  build=build)
    soa = SOA_RDATA

    def message(rrs, with_question):
        m = dns.message.Message(msg_id)
        m.flags = dns.flags.QR | dns.flags.AA
        if with_question:
            m.question.append(dns.rrset.from_text("example.", 0, "IN", "AXFR"))
        for owner, rtype, rdata in rrs:
            m.answer.append(dns.rrset.from_text(owner, 3600, "IN", rtype, rdata))
        return m.to_wire()

    w1 = message([("example.", "SOA", soa), ("a.example.", "A", "192.0.2.1")], True)
    s_m1, mac1, ctx = signer.sign(w1, msg_id, TIME, req_mac, multi=True)
    add(prefix + "-m1", "first-response", signer, TIME, s_m1, mac1, w1,
        requestMacHex=req_mac.hex(), originalId=msg_id)

    w2 = message([("b.example.", "A", "192.0.2.2")], False)
    s_m2, mac2, ctx = signer.sign(w2, msg_id, TIME + 1, ctx=ctx, multi=True)
    add(prefix + "-m2", "continuation", signer, TIME + 1, s_m2, mac2, w2,
        priorMacHex=mac1.hex(), unsignedMessagesHex=[], originalId=msg_id)

    # m3 is sent without a TSIG; its full bytes join the next digest.
    w3 = message([("c.example.", "A", "192.0.2.3")], False)
    ctx.update(w3)
    vectors.append({"id": prefix + "-m3", "source": "dnspython 2.8.0 dns.message", "kind": "unsigned",
                    "wireHex": w3.hex()})

    w4 = message([("example.", "SOA", soa)], False)
    s_m4, mac4, ctx = signer.sign(w4, msg_id, TIME + 2, ctx=ctx, multi=True)
    add(prefix + "-m4", "continuation", signer, TIME + 2, s_m4, mac4, w4,
        priorMacHex=mac2.hex(), unsignedMessagesHex=[w3.hex()], originalId=msg_id)


axfr("V5", "transfer-key.", "hmac-sha256", 0x2345)

# V6: dynamic update (one add) and its NOERROR reply.
def build_update(i):
    u = dns.update.UpdateMessage("example.")
    u.id = i
    u.add("www.example.", 300, "A", "192.0.2.1")
    return u


s6, _, _, mac6, upd = request_vector("V6", "transfer-key.", "hmac-sha256", 0x3456, TIME,
                                     build=build_update)
reply = dns.message.make_response(upd)
unsigned = reply.to_wire()
signed, mac, _ = s6.sign(unsigned, 0x3456, TIME, mac6)
add("V6-reply", "first-response", s6, TIME, signed, mac, unsigned,
    requestMacHex=mac6.hex(), originalId=0x3456)

# Algorithm coverage (RFC 8945 §6): SHA1 (mandatory), SHA384, SHA512.
for name, algorithm in (("A1", "hmac-sha1"), ("A2", "hmac-sha384"), ("A3", "hmac-sha512")):
    key_name = algorithm.replace("hmac-", "") + "-key."
    signer, _, _, mac_a, query = request_vector(name, key_name, algorithm, 0x4567, TIME + 100)
    response_vector(name + "-reply", signer, query, mac_a, TIME + 100, 0x4567)

axfr("A4", "sha512-axfr-key.", "hmac-sha512", 0x5678)

json.dump(vectors, sys.stdout, indent=2)
print()
