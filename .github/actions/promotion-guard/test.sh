#!/usr/bin/env bash
# Tests for the promotion guard. Run from anywhere: bash .github/actions/promotion-guard/test.sh
# Needs git, docker (for the registry checks; network) and, for the consistency check, yq.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$here/../../.." && pwd)"
failures=0

pass() { echo "ok   - $1"; }
fail() { echo "FAIL - $1"; failures=$((failures + 1)); }

# ---- hash.sh in a throwaway repository ---------------------------------------------------------------------------
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
git -C "$tmp" init -q
git -C "$tmp" config user.email test@example.com
git -C "$tmp" config user.name test
mkdir -p "$tmp/a" "$tmp/b"
echo one > "$tmp/a/x"
echo two > "$tmp/b/y"
echo three > "$tmp/root.txt"
git -C "$tmp" add -A && git -C "$tmp" commit -qm first

h1="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
h2="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h1" =~ ^[0-9a-f]{12}$ ]] && pass "hash is 12 hex characters" || fail "hash is 12 hex characters: got '$h1'"
[[ "$h1" == "$h2" ]] && pass "hash is deterministic" || fail "hash is deterministic"

echo four >> "$tmp/b/y" && git -C "$tmp" commit -qam "outside the inputs"
h3="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h3" == "$h1" ]] && pass "a change outside the inputs leaves the hash alone" || fail "a change outside the inputs leaves the hash alone"

echo five >> "$tmp/a/x"
h4="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h4" == "$h1" ]] && pass "the working tree is ignored: only HEAD counts" || fail "the working tree is ignored: only HEAD counts"

git -C "$tmp" commit -qam "inside the inputs"
h5="$(cd "$tmp" && bash "$here/hash.sh" a root.txt)"
[[ "$h5" != "$h1" ]] && pass "a committed change inside the inputs changes the hash" || fail "a committed change inside the inputs changes the hash"

h6="$(cd "$tmp" && bash "$here/hash.sh" a b root.txt)"
[[ "$h6" != "$h5" ]] && pass "the input list is part of the hash" || fail "the input list is part of the hash"

if (cd "$tmp" && bash "$here/hash.sh" a nope 2>/dev/null); then
  fail "a path missing from the tree is an error"
else
  pass "a path missing from the tree is an error"
fi

# ---- resolve.sh against real registries ----------------------------------------------------------------------------
if out="$(bash "$here/resolve.sh" ghcr.io/joseph-leo/worldrankguesser-game staged-000000000000 2>&1)"; then
  fail "an unknown staged tag is refused"
else
  [[ "$out" == *"never passed staging"* ]] && pass "an unknown staged tag is refused, naming the reason" || fail "an unknown staged tag is refused, naming the reason: got '$out'"
fi

if bash "$here/staged-tag.sh" staged-0123456789ab >/dev/null 2>&1; then pass "a staged tag is accepted as a rollback target"; else fail "a staged tag is accepted as a rollback target"; fi
if out="$(bash "$here/staged-tag.sh" sha-0123456789abcdef 2>&1)"; then
  fail "a tag that is not staged-* is refused as a rollback target"
else
  [[ "$out" == *"not a staged-<hash> tag"* ]] && pass "a tag that is not staged-* is refused as a rollback target" || fail "a tag that is not staged-* is refused as a rollback target: got '$out'"
fi

# Microsoft's registry: no anonymous pull limit, and an image this repository pulls anyway.
digest="$(bash "$here/resolve.sh" mcr.microsoft.com/dotnet/runtime 11.0.0-rc.1-resolute 2>/dev/null || true)"
[[ "$digest" == sha256:* ]] && pass "an existing tag resolves to its digest" || fail "an existing tag resolves to its digest: got '$digest'"

# ---- each deploy workflow's push filter equals its env.PATHS (directories listed there without /**) ---------------
for wf in deploy-game deploy-scraper; do
  file="$repo_root/.github/workflows/$wf.yml"
  if [[ ! -f "$file" ]]; then fail "$wf.yml exists"; continue; fi
  if ! command -v yq >/dev/null 2>&1; then echo "skip - yq is not installed: $wf filter/inputs consistency"; continue; fi
  filter="$(yq -r '.["on"].push.paths[]' "$file" | sed 's|/\*\*$||' | sort)"
  inputs="$(yq -r '.env.PATHS' "$file" | sed '/^[[:space:]]*$/d' | sort)"
  if [[ "$filter" == "$inputs" ]]; then
    pass "$wf: the push filter and the hash inputs agree"
  else
    fail "$wf: the push filter and the hash inputs differ"
    diff <(echo "$filter") <(echo "$inputs") || true
  fi
done

echo
if [[ "$failures" -eq 0 ]]; then echo "all guard tests passed"; else echo "$failures guard test(s) failed"; exit 1; fi
