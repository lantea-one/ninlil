#!/usr/bin/env python3
"""Checks the links and anchors in every git-tracked Markdown file.

This is a step of .forgejo/scripts/lint.sh (the "lint" job). A renamed heading or a moved file
breaks links without any build failing, so this script finds them.

For each tracked *.md file (git ls-files, minus anything under node_modules) it checks:
  * relative links `[text](path)`, `[text](path#anchor)`, reference definitions `[id]: path`
    and `<a href>` / `<img src>` attributes: the target file or directory must exist. A path
    is relative to the file that holds the link; a leading `/` is relative to the repository
    root.
  * `#anchor` fragments, in the same file and in other Markdown files, against the target's
    heading slugs (GitHub and Forgejo rules: lowercase, punctuation dropped except `-` and `_`,
    spaces become `-`, a repeated slug gets `-1`, `-2`, ...) and against explicit
    `<a id="x">` / `<a name="x">` anchors. Anchors on non-Markdown targets are not checked.
Fenced code blocks (``` and ~~~), inline code spans and HTML comments are ignored when finding
links. Indented code blocks are not: nested list content uses the same indentation, and telling
the two apart needs a full CommonMark parser, so a link-like text there is checked like a link.
Links with a scheme (https:, mailto:, ...) are skipped unless external checking is on.

Usage: python3 -I .forgejo/scripts/check-markdown-links.py [--external]
Inputs: run from inside the git repository (it works on the repository root). `--external`, or
  NINLIL_LINK_CHECK_EXTERNAL=true, also fetches each distinct http(s) URL with curl (20 second
  limit, redirects followed) and reports the ones that fail. This needs network access and can
  fail because of a remote outage, so the lint job leaves it off.
Outputs: one line `file:line: message` per problem, then a summary line with the counts.
Exit status: 0 when no problem was found, 1 otherwise, 2 when git cannot list the files.
"""

import os
import re
import subprocess
import sys
import urllib.parse

SCHEME = re.compile(r"^[a-zA-Z][a-zA-Z0-9+.-]*:")
FENCE = re.compile(r"^\s*(`{3,}|~{3,})")
ATX_HEADING = re.compile(r"^ {0,3}(#{1,6})[ \t]+(.*?)(?:[ \t]+#+)?[ \t]*$")
SETEXT_UNDERLINE = re.compile(r"^ {0,3}(=+|-+)[ \t]*$")
CODE_SPAN = re.compile(r"(`+)(.+?)\1")
# A destination is either <anything but angle brackets> or text without spaces in which
# parentheses may nest one level, as CommonMark allows.
INLINE_LINK = re.compile(r"\]\(\s*(?:<([^<>\n]*)>|((?:[^()\s]|\([^()\s]*\))*))")
HTML_COMMENT = re.compile(r"<!--.*?-->")
REFERENCE_DEFINITION = re.compile(r"^ {0,3}\[[^\]]+\]:\s*<?(\S+?)>?(?:\s|$)")
HTML_LINK = re.compile(r"""\b(?:href|src)\s*=\s*["']([^"']+)["']""")
HTML_ANCHOR = re.compile(r"""<a\s[^>]*?\b(?:id|name)\s*=\s*["']([^"']+)["']""")


def slugify(heading):
    """Returns the GitHub-style anchor slug of a heading's Markdown text."""
    text = re.sub(r"!?\[([^\]]*)\]\([^)]*\)", r"\1", heading)  # links keep their text
    text = CODE_SPAN.sub(r"\2", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = text.replace("*", "")
    text = re.sub(r"(?<!\w)__(.+?)__(?!\w)", r"\1", text)
    # Underscores around a word are emphasis, so they are not part of the rendered text.
    text = re.sub(r"(?<!\w)_(.+?)_(?!\w)", r"\1", text)
    text = text.strip().lower()
    text = re.sub(r"[^\w\- ]", "", text)
    return text.replace(" ", "-")


class MarkdownFile:
    """The links and anchors of one Markdown file, found in a single pass over its lines."""

    def __init__(self, path):
        self.path = path
        self.links = []  # (line number, target)
        self.anchors = set()
        self._parse()

    def _parse(self):
        with open(self.path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
        slug_counts = {}
        fence = None
        in_comment = False
        previous = ""
        for number, line in enumerate(lines, start=1):
            marker = FENCE.match(line)
            if fence is not None:
                # A fence closes with the same character and at least as many of them.
                if marker and marker.group(1)[0] == fence[0] and len(marker.group(1)) >= len(fence):
                    fence = None
                continue
            if marker:
                fence = marker.group(1)
                continue
            heading = None
            atx = ATX_HEADING.match(line)
            if atx:
                heading = atx.group(2)
            elif (
                SETEXT_UNDERLINE.match(line)
                and previous.strip()
                and not previous.lstrip().startswith(("-", "*", "+", ">", "#"))
            ):
                heading = previous.strip()
            previous = line
            if heading is not None:
                slug = slugify(heading)
                count = slug_counts.get(slug, 0)
                slug_counts[slug] = count + 1
                self.anchors.add(slug if count == 0 else f"{slug}-{count}")
            # Spaces keep the columns while hiding code spans and HTML comments, which the
            # renderer does not turn into links, from the link patterns.
            visible = CODE_SPAN.sub(lambda match: " " * len(match.group(0)), line)
            if in_comment:
                end = visible.find("-->")
                if end < 0:
                    continue
                visible = " " * (end + 3) + visible[end + 3 :]
                in_comment = False
            visible = HTML_COMMENT.sub(lambda match: " " * len(match.group(0)), visible)
            start = visible.find("<!--")
            if start >= 0:
                visible = visible[:start]
                in_comment = True
            for match in HTML_ANCHOR.finditer(visible):
                self.anchors.add(match.group(1).lower())
            for match in INLINE_LINK.finditer(visible):
                self.links.append((number, match.group(1) or match.group(2)))
            for match in HTML_LINK.finditer(visible):
                self.links.append((number, match.group(1)))
            definition = REFERENCE_DEFINITION.match(visible)
            if definition:
                self.links.append((number, definition.group(1)))


def tracked_markdown_files(root):
    """Returns the tracked Markdown paths relative to root, without node_modules content."""
    result = subprocess.run(
        ["git", "-C", root, "ls-files", "-z", "--", "*.md"],
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        sys.stderr.write(result.stderr.decode(errors="replace"))
        sys.exit(2)
    names = [name for name in result.stdout.decode().split("\0") if name]
    return [name for name in names if "node_modules" not in name.split("/")]


def check_external(urls):
    """Fetches each URL with curl. Returns {url: reason} for the ones that fail."""
    failures = {}
    for url in sorted(urls):
        result = subprocess.run(
            ["curl", "-sS", "-L", "-o", os.devnull, "--max-time", "20", "-w", "%{http_code}", url],
            capture_output=True,
            text=True,
            check=False,
        )
        code = result.stdout.strip()
        if result.returncode != 0:
            failures[url] = result.stderr.strip() or f"curl exit {result.returncode}"
        elif not code.startswith(("2", "3")):
            failures[url] = f"HTTP {code}"
    return failures


def main(arguments):
    """Runs the check and returns the exit status."""
    external = "--external" in arguments or os.environ.get("NINLIL_LINK_CHECK_EXTERNAL") == "true"
    root = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=False
    )
    if root.returncode != 0:
        sys.stderr.write(root.stderr)
        return 2
    root = root.stdout.strip()
    names = tracked_markdown_files(root)
    parsed = {}

    def load(relative):
        if relative not in parsed:
            parsed[relative] = MarkdownFile(os.path.join(root, relative))
        return parsed[relative]

    problems = []
    link_count = 0
    anchor_count = 0
    external_urls = {}
    for name in names:
        document = load(name)
        for number, target in document.links:
            if not target:
                continue
            if SCHEME.match(target) or target.startswith("//"):
                if external and target.lower().startswith(("http:", "https:")):
                    external_urls.setdefault(target, []).append((name, number))
                continue
            link_count += 1
            path, _, fragment = target.partition("#")
            path = urllib.parse.unquote(path.partition("?")[0])
            fragment = urllib.parse.unquote(fragment)
            if path == "":
                resolved = name
            elif path.startswith("/"):
                resolved = os.path.normpath(path.lstrip("/"))
            else:
                resolved = os.path.normpath(os.path.join(os.path.dirname(name), path))
            if resolved.startswith(".."):
                problems.append((name, number, f"link leaves the repository: {target}"))
                continue
            if not os.path.exists(os.path.join(root, resolved)):
                problems.append((name, number, f"file not found: {target}"))
                continue
            is_markdown_file = resolved.endswith(".md") and os.path.isfile(
                os.path.join(root, resolved)
            )
            if fragment and is_markdown_file:
                anchor_count += 1
                if fragment.lower() not in load(resolved).anchors:
                    problems.append((name, number, f"anchor not found in {resolved}: #{fragment}"))
    if external:
        for url, reason in check_external(external_urls).items():
            for name, number in external_urls[url]:
                problems.append((name, number, f"external link failed ({reason}): {url}"))

    for name, number, message in sorted(problems):
        print(f"{name}:{number}: {message}")
    print(
        f"Checked {len(names)} Markdown files, {link_count} relative links, {anchor_count} anchors"
        f"{f', {len(external_urls)} external URLs' if external else ''}: {len(problems)} problems."
    )
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
