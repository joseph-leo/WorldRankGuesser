#!/usr/bin/env bash
# Accepts a tag only if it is a staged-<hash> tag: a rollback may name one of those and nothing else, because a
# sha-<sha> tag says "built", never "passed staging".
#   staged-tag.sh staged-0123456789ab
set -euo pipefail

tag="${1:?tag}"

if [[ "$tag" != staged-* ]]; then
  echo "::error::'${tag}' is not a staged-<hash> tag: only an image that passed staging may be deployed to production" >&2
  exit 1
fi
