#!/usr/bin/env bash
# Prints the next release version: version.txt's major.minor (e.g. 0.3) plus the next patch number in that line,
# counted from the existing tags v<major.minor>.<patch>. A new line starts at 0: after v0.2.25 comes v0.2.26, and
# bumping version.txt to 0.3 gives v0.3.0, then v0.3.1. (The Store needs each version higher than the last as a whole,
# which this keeps.) Used by .github/workflows/release.yml; run it locally to see what the next release will be.
#   scripts/next-version.sh            uses the tags this clone has (run `git fetch --tags` first)
source "$(dirname "$0")/lib/common.sh"

line=$(tr -d '\r\n ' < "$ROOT/version.txt")
[[ $line =~ ^[0-9]+\.[0-9]+$ ]] || die "version.txt must hold major.minor (e.g. 0.3), not '$line'"
last=$(git -C "$ROOT" tag -l "v$line.*" | sed -n "s/^v${line//./\\.}\.\([0-9][0-9]*\)$/\1/p" | sort -n | tail -1)
printf '%s.%s\n' "$line" "$(( ${last:--1} + 1 ))"
