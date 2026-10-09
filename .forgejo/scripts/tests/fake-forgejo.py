#!/usr/bin/env python3
"""A tiny fake Forgejo API and container registry for the pipeline script tests.

The tests (cleanup.test.sh, release.test.sh) start it on a free port on 127.0.0.1 and point
cleanup.sh and release.sh at it, so the real curl, config files and jq are exercised without
touching a real server.

Usage: fake-forgejo.py <routes.json> <request-log> <port-file>
  routes.json   maps "METHOD /path" or "METHOD /path#<page>" to [status, body] (a string body is sent as is, anything else as JSON); a page-specific
                key wins over the plain one. Unknown GET list paths answer 200 with "[]" and any
                other unknown request answers 204 with no body. The file is re-read per request.
  request-log   one line per request: METHOD, path with query, and the Authorization header
                the server received, separated by tabs.
  port-file     written with the chosen port once the server listens.
"""

import http.server
import json
import sys
import urllib.parse

routes_file, log_file, port_file = sys.argv[1:4]


class Handler(http.server.BaseHTTPRequestHandler):
    """Answers every request from the routes file and records it in the request log."""

    def handle_request(self):
        """Looks up the route for this request, logs it and sends the canned response."""
        length = int(self.headers.get("Content-Length") or 0)
        if length:
            self.rfile.read(length)
        parsed = urllib.parse.urlsplit(self.path)
        query = urllib.parse.parse_qs(parsed.query)
        path = urllib.parse.unquote(parsed.path)
        with open(log_file, "a") as log:
            log.write(f"{self.command}\t{self.path}\t{self.headers.get('Authorization', '')}\n")
        with open(routes_file) as handle:
            routes = json.load(handle)
        page = query.get("page", [None])[0]
        key = f"{self.command} {path}"
        route = routes.get(f"{key}#{page}") or routes.get(key)
        if route is None:
            route = [200, []] if self.command == "GET" and page else [204, None]
        status, body = route
        if body is None:
            payload = b""
        elif isinstance(body, str):
            payload = body.encode()
        else:
            payload = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    do_GET = do_POST = do_PUT = do_PATCH = do_DELETE = handle_request

    def log_message(self, *args):
        """Silences the default stderr request log."""


server = http.server.HTTPServer(("127.0.0.1", 0), Handler)
with open(port_file, "w") as handle:
    handle.write(str(server.server_port))
server.serve_forever()
