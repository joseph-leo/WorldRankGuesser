#!/usr/bin/env bash
# Fails the job with a named error when an environment variable is empty: a GitHub secret that does not exist
# expands to "" rather than failing, so a deploy would otherwise carry an empty value into Azure or Cloudflare.
#   require-env.sh NAME "what an empty value would do"
set -euo pipefail

name=$1
consequence=${2:-}

if [[ -z "${!name:-}" ]]; then
  echo "::error::the repository secret or variable $name is missing or empty${consequence:+: $consequence}"
  exit 1
fi
echo "$name is set"
