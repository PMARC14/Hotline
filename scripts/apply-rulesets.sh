#!/usr/bin/env bash
# Applies .github/rulesets/*.json to the GitHub repository (creates each ruleset, or updates the one with that name).
# Rulesets on a private repository need GitHub Pro; on a public one they're free. Needs the GitHub CLI (gh auth login).
#   scripts/apply-rulesets.sh [OWNER/REPO]      default PMARC14/Hotline
source "$(dirname "$0")/lib/common.sh"

repository=${1:-PMARC14/Hotline}
gh api "repos/$repository/rulesets" >/dev/null ||
    die "could not read rulesets (a private repository needs GitHub Pro, or make it public first)"
for file in "$ROOT"/.github/rulesets/*.json; do
    name=$(sed -nE 's/^ *"name": *"([^"]+)".*/\1/p' "$file" | head -1)
    [ -n "$name" ] || die "no \"name\" in $file"
    id=$(gh api "repos/$repository/rulesets" --jq ".[] | select(.name == \"$name\") | .id" | head -1)
    if [ -n "$id" ]; then
        gh api -X PUT "repos/$repository/rulesets/$id" --input "$file" >/dev/null && echo "updated ruleset '$name'"
    else
        gh api -X POST "repos/$repository/rulesets" --input "$file" >/dev/null && echo "created ruleset '$name'"
    fi
done
