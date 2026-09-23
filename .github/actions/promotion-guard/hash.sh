#!/usr/bin/env bash
# Prints the hash of a deploy workflow's inputs: the first 12 hex characters of the SHA-256 of the tree entries of
# exactly the given paths at HEAD (`git ls-tree -r`: mode, type, blob id and path of every file under them). The
# working tree plays no part. A path that is not in the tree is an error: a typo must not silently drop an input.
#   hash.sh src/WorldRankGuesser.Api Dockerfile ...
set -euo pipefail

if [[ $# -eq 0 ]]; then
  echo "hash.sh: no input paths given" >&2
  exit 2
fi

for path in "$@"; do
  if ! git cat-file -e "HEAD:${path}" 2>/dev/null; then
    echo "hash.sh: '${path}' is not in HEAD's tree" >&2
    exit 2
  fi
done

git ls-tree -r --full-tree HEAD -- "$@" | sha256sum | cut -c1-12
