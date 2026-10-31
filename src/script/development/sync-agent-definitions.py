#!/usr/bin/env python3
"""Writes each AI coding assistant's agent definitions from the tool-neutral roles in src/agents/.

What it does
  Every role in src/agents/<name>.md (front matter: name, description, tier, access; body: the
  role's instructions) becomes one agent definition per supported assistant, in the folder and
  format that assistant reads:
    .claude/agents/<name>.md   Claude Code (model alias, permission mode, tool list)
    .gemini/agents/<name>.md   Gemini CLI (turn and time limits; tools and model inherited)
    .agents/agents/<name>.md   Antigravity (model tier)
    .codex/agents/<name>.toml  Codex (reasoning effort; instructions in developer_instructions)
  The body is copied unchanged after a comment that names the source, so the roles read the
  same in every assistant. A generated file that no longer has a source role is deleted.

  Tool lists are written for Claude Code only. Antigravity skips a whole agent file when one
  value is invalid, and the other assistants' tool names differ by version, so those files
  inherit the session's tools and the role text states what the role may change.

Usage
  python3 src/script/development/sync-agent-definitions.py          write the definitions
  python3 src/script/development/sync-agent-definitions.py --check  only compare; the lint job

Inputs: src/agents/*.md, relative to the repository root (found from this file's location).
Outputs: the files above; with --check, nothing, and a list of the files that differ.
Exit status: 0 when the definitions match (or were written), 1 when --check finds a difference
or a role is invalid.
"""

import json
import sys
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
SOURCE_DIRECTORY = REPOSITORY_ROOT / "src" / "agents"

# tier: how much reasoning the role needs. deep designs, standard implements, fast edits.
TIERS = ("deep", "standard", "fast")

# access: what the role may do. design reads and writes a design file, edit changes code and
# runs commands, mechanical edits existing files and runs checks, verify only reads and runs.
CLAUDE_TOOLS = {
    "design": "Read, Write, Grep, Glob",
    "edit": "Read, Write, Edit, Bash, Grep, Glob",
    "mechanical": "Read, Edit, Grep, Glob, Bash",
    "verify": "Read, Grep, Glob, Bash",
}

# Aliases, not model IDs, so the files follow each assistant's current models.
CLAUDE_MODELS = {"deep": "opus", "standard": "sonnet", "fast": "haiku"}

# Antigravity accepts only these tiers; any other value makes it skip the file silently.
ANTIGRAVITY_MODELS = {"deep": "pro", "standard": "inherit", "fast": "flash"}

CODEX_EFFORT = {"deep": "high", "standard": "medium", "fast": "low"}

# Gemini CLI stops a subagent after 30 turns or 10 minutes by default; an implementing role runs
# for hundreds of tool calls and up to an hour.
GEMINI_LIMITS = {
    "design": (150, 60),
    "edit": (400, 120),
    "mechanical": (150, 60),
    "verify": (300, 120),
}


def quote(value):
    """Returns value as a double-quoted string that is valid in both YAML and TOML."""
    return json.dumps(value)


def read_role(path):
    """Parses one role file into its front matter fields and body, or exits on an invalid role."""
    text = path.read_text(encoding="utf-8")
    parts = text.split("---\n", 2)
    if len(parts) != 3 or parts[0] != "":
        sys.exit(f"{path}: must start with front matter between '---' lines")
    fields = {}
    for line in parts[1].strip().splitlines():
        key, separator, value = line.partition(": ")
        if not separator:
            sys.exit(f"{path}: front matter line is not 'key: value': {line}")
        fields[key] = value.strip()
    errors = []
    if fields.get("name") != path.stem:
        errors.append(f"name must be '{path.stem}'")
    if not fields.get("description"):
        errors.append("description is missing")
    if fields.get("tier") not in TIERS:
        errors.append(f"tier must be one of {', '.join(TIERS)}")
    if fields.get("access") not in CLAUDE_TOOLS:
        errors.append(f"access must be one of {', '.join(CLAUDE_TOOLS)}")
    if "'''" in parts[2]:
        errors.append("the body must not contain ''' (it ends the Codex TOML string)")
    if errors:
        sys.exit(f"{path}: " + "; ".join(errors))
    return fields, parts[2].lstrip("\n")


def markdown_agent(front_matter, notice, body):
    """Joins front matter lines, the generated-file notice and the body into one Markdown agent."""
    return "---\n" + "\n".join(front_matter) + "\n---\n\n" + notice + "\n\n" + body


def render(fields, body):
    """Returns {relative output path: content} for one role."""
    name = fields["name"]
    tier = fields["tier"]
    access = fields["access"]
    source = f"src/agents/{name}.md"
    script = "src/script/development/sync-agent-definitions.py"
    notice = f"<!-- Generated from {source} by {script}. Edit the source and run the script. -->"
    description = quote(fields["description"])
    max_turns, timeout_minutes = GEMINI_LIMITS[access]
    return {
        f".claude/agents/{name}.md": markdown_agent(
            [
                f"name: {name}",
                f"description: {description}",
                f"model: {CLAUDE_MODELS[tier]}",
                f"permissionMode: {'default' if access == 'verify' else 'acceptEdits'}",
                f"tools: {CLAUDE_TOOLS[access]}",
            ],
            notice,
            body,
        ),
        f".gemini/agents/{name}.md": markdown_agent(
            [
                f"name: {name}",
                f"description: {description}",
                f"max_turns: {max_turns}",
                f"timeout_mins: {timeout_minutes}",
            ],
            notice,
            body,
        ),
        f".agents/agents/{name}.md": markdown_agent(
            [
                f"name: {name}",
                f"description: {description}",
                f"model: {ANTIGRAVITY_MODELS[tier]}",
            ],
            notice,
            body,
        ),
        f".codex/agents/{name}.toml": (
            f"# Generated from {source} by {script}. Edit the source and run the script.\n"
            f"name = {quote(name)}\n"
            f"description = {description}\n"
            f"model_reasoning_effort = {quote(CODEX_EFFORT[tier])}\n"
            f"developer_instructions = '''\n{body}'''\n"
        ),
    }


def main():
    """Writes or checks every generated definition and removes or reports stale ones."""
    check = sys.argv[1:] == ["--check"]
    if sys.argv[1:] and not check:
        sys.exit("usage: sync-agent-definitions.py [--check]")

    roles = sorted(SOURCE_DIRECTORY.glob("*.md"))
    if not roles:
        sys.exit(f"no roles found in {SOURCE_DIRECTORY}")

    expected = {}
    for role in roles:
        expected.update(render(*read_role(role)))

    output_globs = (".claude/agents/*.md", ".gemini/agents/*.md", ".agents/agents/*.md", ".codex/agents/*.toml")
    existing = {
        str(path.relative_to(REPOSITORY_ROOT))
        for pattern in output_globs
        for path in REPOSITORY_ROOT.glob(pattern)
    }

    different = []
    for relative, content in sorted(expected.items()):
        path = REPOSITORY_ROOT / relative
        if not path.exists() or path.read_text(encoding="utf-8") != content:
            different.append(relative)
            if not check:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
    stale = sorted(existing - expected.keys())
    if not check:
        for relative in stale:
            (REPOSITORY_ROOT / relative).unlink()

    if check:
        if different or stale:
            for relative in different:
                print(f"out of date: {relative}", file=sys.stderr)
            for relative in stale:
                print(f"no source role: {relative}", file=sys.stderr)
            print("Run python3 src/script/development/sync-agent-definitions.py and commit the result.", file=sys.stderr)
            return 1
        print(f"ok: {len(roles)} roles, {len(expected)} definitions")
        return 0

    print(f"{len(roles)} roles: {len(different)} definitions written, {len(stale)} removed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
