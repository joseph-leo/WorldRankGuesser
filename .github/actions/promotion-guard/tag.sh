#!/usr/bin/env bash
# Adds <tag> to the image already pushed at <digest>, without pulling or rebuilding.
#   tag.sh ghcr.io/joseph-leo/worldrankguesser-game sha256:... staged-0123456789ab
set -euo pipefail

image="${1:?image}"
digest="${2:?digest}"
tag="${3:?tag}"

docker buildx imagetools create --tag "${image}:${tag}" "${image}@${digest}"
echo "tagged ${image}:${tag} -> ${digest}"
