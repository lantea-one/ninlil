# Merges the dnspython vectors and the BIND capture vectors into tsig-vectors.json.
#
# Usage: python3 -I merge_tsig_vectors.py tsig-vectors.dnspython.json tsig-vectors.bind.json > tsig-vectors.json
# Inputs: two JSON arrays. Output (stdout): one JSON array, dnspython vectors first.

import json
import sys

merged = []
for path in sys.argv[1:]:
    with open(path) as handle:
        merged.extend(json.load(handle))
json.dump(merged, sys.stdout, indent=2)
print()
