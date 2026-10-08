# TSIG known-answer vectors

`tsig-vectors.json` holds RFC 8945 message and MAC vectors made by other implementations. The tests
(`TsigKnownAnswerTests`) only read it; nothing generates vectors at test time. Never edit a vector
by hand. If dnspython and BIND disagree on a case, follow the RFC text and investigate.

## Files

| File | Role |
|---|---|
| `generate_tsig_vectors.py` | Makes `tsig-vectors.dnspython.json` with dnspython 2.8.0 (`dns.tsig.sign`, fixed times and explicit chaining) |
| `capture_bind_requests.py` | Records the first signed message that one BIND tool run sends (standard library only) |
| `capture_bind_vectors.sh` | Runs `dig` (SOA over UDP, AXFR over TCP) and `nsupdate` (one add) through the capture script and writes `tsig-vectors.bind.json` |
| `merge_tsig_vectors.py` | Merges both JSON files into `tsig-vectors.json` |

## Regenerating

Run from the repository root.

```bash
V=src/dotnet/tests/Ninlil.Dns.Server.Tsig.Tests/Vectors

# 1. dnspython vectors (a throwaway container; network is needed once, for pip).
docker run --rm -v "$PWD/$V:/v:Z" -w /v python:3.13-slim sh -c \
  "pip install --no-cache-dir dnspython==2.8.0 >/dev/null 2>&1 && python -I generate_tsig_vectors.py" \
  > $V/tsig-vectors.dnspython.json

# 2. BIND vectors (needs dig, nsupdate and python3 on the host; the BIND version goes into "source").
$V/capture_bind_vectors.sh

# 3. Merge.
python3 -I $V/merge_tsig_vectors.py $V/tsig-vectors.dnspython.json $V/tsig-vectors.bind.json \
  > $V/tsig-vectors.json
```

The dnspython output is reproducible: secrets come from the vector ids and the times are fixed. The
BIND vectors carry the real clock of the capture run, which the tests set their clock to.

## Vector fields

`id`, `source`, `kind`, `keyName`, `algorithm`, `secretBase64`, `timeSigned`, `fudge`,
`originalId`, `unsignedHex` (the message without its TSIG, header ID equal to the Original ID),
`wireHex` (the signed message as sent), `macHex`, plus per kind:

- `request`: nothing more.
- `first-response`: `requestMacHex`, the MAC of the request it answers.
- `continuation`: `priorMacHex`, `unsignedMessagesHex` (messages without a TSIG sent since the
  prior signed one).
- `badtime-response`: `requestMacHex`, `otherDataHex`, `errorCode`.
- `unsigned`: only `wireHex` (a message in the middle of a multi-message answer).

## Set

The SOA records in the answers use names that share no suffix, so dnspython writes them without
compression, as Ninlil's writer does; the sign tests compare the unsigned bytes of both.

| Ids | Content |
|---|---|
| V1 | SOA query, key `transfer-key.`, Time Signed 1700000000, Fudge 300, ID 0x1234 |
| V2 | V1 with the key `Mixed.Case.Key.` |
| V3 | V1 after a forwarder rewrote the header ID to 0xBEEF (Original ID stays 0x1234) |
| V4 | NOERROR SOA answer to V1 |
| V5-req, V5-m1..m4 | AXFR: request, signed m1 (request MAC), signed m2 (prior m1), unsigned m3, signed m4 (prior m2 plus m3) |
| V6, V6-reply | Dynamic update (one add) and its signed NOERROR reply |
| V7 | BADTIME reply to V1 (Other Data = request time + 600) |
| A1, A1-reply | HMAC-SHA1 query and answer |
| A2, A2-reply | HMAC-SHA384 query and answer |
| A3, A3-reply | HMAC-SHA512 query and answer |
| A4-req, A4-m1..m4 | The V5 chain with HMAC-SHA512 |
| B1, B2, B3 | BIND `dig` SOA, `dig` AXFR over TCP and `nsupdate`, key `Mixed.Case.Key.` |
