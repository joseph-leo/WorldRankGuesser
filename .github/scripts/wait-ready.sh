#!/usr/bin/env bash
# Polls <base url>/readyz every 5 seconds until it answers 200, or fails after <timeout seconds>:
#   .github/scripts/wait-ready.sh https://ca-wrg-staging-game.example.azurecontainerapps.io 180
# A scaled-to-zero app and a paused database both need time; /readyz is 503 until the rankings are loaded.
set -euo pipefail

base="${1:?base url}"
timeout="${2:-180}"
deadline=$(( $(date +%s) + timeout ))

while :; do
  code="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 30 "$base/readyz" || echo 000)"
  echo "$(date -u +%T) /readyz -> $code"
  [[ "$code" == "200" ]] && exit 0
  if [[ "$(date +%s)" -ge "$deadline" ]]; then
    echo "::error::$base/readyz did not answer 200 within ${timeout}s" >&2
    exit 1
  fi
  sleep 5
done
