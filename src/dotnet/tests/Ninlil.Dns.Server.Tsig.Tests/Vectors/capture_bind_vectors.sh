#!/usr/bin/env bash
# Captures the three BIND vectors (B1 dig SOA, B2 dig AXFR over TCP, B3 nsupdate add) and writes
# them as a JSON array to tsig-vectors.bind.json in this folder.
#
# Inputs: dig and nsupdate (BIND 9) and python3 on PATH. No arguments.
# Output: tsig-vectors.bind.json. The key is the mixed-case "Mixed.Case.Key" with a fixed secret.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
secret="$(printf 'ninlil-bind-capture-secret!' | base64)"
key="Mixed.Case.Key"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

capture() {
  python3 -I "$here/capture_bind_requests.py" "$@"
}

capture B1 "$key." "$secret" \
  dig -y "hmac-sha256:$key:$secret" -p '{port}' @127.0.0.1 example. SOA +norec +tries=1 >"$tmp/b1.json"
capture B2 "$key." "$secret" \
  dig -y "hmac-sha256:$key:$secret" -p '{port}' @127.0.0.1 example. AXFR +tcp >"$tmp/b2.json"
capture B3 "$key." "$secret" \
  nsupdate -y "hmac-sha256:$key:$secret" >"$tmp/b3.json"

python3 -I - "$tmp/b1.json" "$tmp/b2.json" "$tmp/b3.json" >"$here/tsig-vectors.bind.json" <<'PY'
import json, sys
json.dump([json.load(open(p)) for p in sys.argv[1:]], sys.stdout, indent=2)
print()
PY
