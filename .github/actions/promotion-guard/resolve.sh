#!/usr/bin/env bash
# Resolves <image>:<tag> to its digest, or refuses: a staged-<hash> tag exists only after the staging job's smoke
# test passed on exactly these inputs, so a missing tag means these sources never passed staging.
#   resolve.sh ghcr.io/joseph-leo/worldrankguesser-game staged-0123456789ab
set -euo pipefail

image="${1:?image}"
tag="${2:?tag}"

# The plain output's "Digest:" line, not a --format template: buildx 0.30 (Docker Desktop) mishandles
# '{{.Manifest.Digest}}', printing the whole report, while the runner's 0.37 does not; the text line is the same in both.
if ! report="$(docker buildx imagetools inspect "${image}:${tag}" 2>/dev/null)"; then
  echo "::error::${image}:${tag} does not exist: these sources never passed staging" >&2
  exit 1
fi

digest="$(printf '%s\n' "$report" | awk '/^Digest:/ { print $2; exit }')"
if [[ "$digest" != sha256:* ]]; then
  echo "::error::no digest in the inspect output for ${image}:${tag}: ${report}" >&2
  exit 1
fi

echo "$digest"
