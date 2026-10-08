# Captures the first TSIG-signed message that BIND's dig or nsupdate sends, as a known-answer
# vector. Python standard library only; no dnspython.
#
# Usage: python3 -I capture_bind_requests.py <id> <keyName> <secretBase64> <command...>
#   <command...> is run with the placeholder {port} replaced by the port this script listens on
#   (UDP and TCP, 127.0.0.1, chosen by the OS). Example (see README.md for the full list):
#     python3 -I capture_bind_requests.py B1 Mixed.Case.Key. "$SECRET" \
#       dig -y hmac-sha256:Mixed.Case.Key:$SECRET -p {port} @127.0.0.1 example. SOA +norec +tries=1
#
# The script records the first datagram, or the first TCP message (the 2-octet length stripped),
# kills the command and prints one JSON vector on stdout. The tool version comes from `dig -v` or `nsupdate -V`.
# The BIND tools sign with the real clock, so the vector carries the Time Signed they chose.

import base64
import json
import select
import socket
import struct
import subprocess
import sys


def read_name(data, offset):
    """Returns the offset after the name at offset (compression pointers end a name)."""
    while True:
        length = data[offset]
        if length == 0:
            return offset + 1
        if length & 0xC0 == 0xC0:
            return offset + 2
        offset += 1 + length


def parse_tsig(wire):
    """Returns (tsig_start, algorithm_name_text, time_signed, fudge, mac, original_id)."""
    qd, an, ns, ar = struct.unpack("!4H", wire[4:12])
    offset = 12
    for _ in range(qd):
        offset = read_name(wire, offset) + 4
    tsig_start = None
    for index in range(an + ns + ar):
        start = offset
        offset = read_name(wire, offset)
        rtype, _, _, rdlength = struct.unpack("!HHIH", wire[offset : offset + 10])
        offset += 10
        if index == an + ns + ar - 1:
            assert rtype == 250, "the last record is not a TSIG"
            tsig_start = start
            rdata = wire[offset : offset + rdlength]
        offset += rdlength
    algo_end = read_name(rdata, 0)
    labels = []
    position = 0
    while rdata[position]:
        labels.append(rdata[position + 1 : position + 1 + rdata[position]].decode())
        position += 1 + rdata[position]
    time_high, time_low, fudge, mac_size = struct.unpack("!HIHH", rdata[algo_end : algo_end + 10])
    mac_start = algo_end + 10
    mac = rdata[mac_start : mac_start + mac_size]
    (original_id,) = struct.unpack("!H", rdata[mac_start + mac_size : mac_start + mac_size + 2])
    return tsig_start, ".".join(labels), (time_high << 32) | time_low, fudge, mac, original_id


def main():
    vector_id, key_name, secret_b64 = sys.argv[1:4]
    command = sys.argv[4:]
    udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp.bind(("127.0.0.1", 0))
    port = udp.getsockname()[1]
    tcp = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    tcp.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    tcp.bind(("127.0.0.1", port))
    tcp.listen(1)

    tool = command[0]
    process = subprocess.Popen(
        [part.replace("{port}", str(port)) for part in command],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        stdin=subprocess.PIPE if tool == "nsupdate" else None,
        text=tool == "nsupdate",
    )
    if tool == "nsupdate":
        process.stdin.write(
            f"server 127.0.0.1 {port}\nzone example.\n"
            "update add www.example. 300 A 192.0.2.1\nsend\n"
        )
        process.stdin.flush()

    ready, _, _ = select.select([udp, tcp], [], [], 10)
    assert ready, "the BIND tool sent nothing within 10 seconds"
    if ready[0] is udp:
        wire, _ = udp.recvfrom(65535)
    else:
        connection, _ = tcp.accept()
        header = connection.recv(2, socket.MSG_WAITALL)
        (length,) = struct.unpack("!H", header)
        wire = connection.recv(length, socket.MSG_WAITALL)
    process.kill()

    tsig_start, algorithm, time_signed, fudge, mac, original_id = parse_tsig(wire)
    version = subprocess.run(
        [tool, "-V" if tool == "nsupdate" else "-v"], capture_output=True, text=True, check=True
    )
    version = (version.stdout or version.stderr).strip()
    unsigned = bytearray(wire[:tsig_start])
    ar_count = struct.unpack("!H", unsigned[10:12])[0]
    unsigned[10:12] = struct.pack("!H", ar_count - 1)
    print(
        json.dumps(
            {
                "id": vector_id,
                "source": version,
                "kind": "request",
                "keyName": key_name,
                "algorithm": algorithm,
                "secretBase64": secret_b64,
                "timeSigned": time_signed,
                "fudge": fudge,
                "unsignedHex": unsigned.hex(),
                "wireHex": wire.hex(),
                "macHex": mac.hex(),
                "originalId": original_id,
            },
            indent=2,
        )
    )


main()
