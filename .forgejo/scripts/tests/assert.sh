#!/usr/bin/env bash
# Minimal assertion helpers shared by the pipeline script tests (*.test.sh in this folder).
#
# Source it from a test script; it does not run anything by itself. It keeps a pass/fail count
# and `finish` exits non-zero when any assertion failed. The tests need only bash and git:
# they use bare local repositories as the "origin" and never reach the network.
#
# Inputs: none. Outputs: one "ok"/"FAIL" line per assertion on stdout, failure details on stderr.

failures=0
checks=0

# Records a passing assertion. Arguments: description.
pass() {
    checks=$((checks + 1))
    echo "ok   - $1"
}

# Records a failing assertion. Arguments: description, detail.
fail() {
    checks=$((checks + 1))
    failures=$((failures + 1))
    echo "FAIL - $1"
    echo "       $2" >&2
}

# Asserts two strings are equal. Arguments: description, expected, actual.
assert_eq() {
    if [[ "$2" == "$3" ]]; then
        pass "$1"
    else
        fail "$1" "expected '$2' but got '$3'"
    fi
}

# Asserts a string contains a substring. Arguments: description, haystack, needle.
assert_contains() {
    if [[ "$2" == *"$3"* ]]; then
        pass "$1"
    else
        fail "$1" "expected output to contain '$3' but it was: $2"
    fi
}

# Prints the summary and exits non-zero when any assertion failed.
finish() {
    echo "${checks} checks, ${failures} failed"
    if ((failures > 0)); then
        exit 1
    fi
    exit 0
}
