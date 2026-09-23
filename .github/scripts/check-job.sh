#!/usr/bin/env bash
# Fails unless the Job's most recent execution succeeded and started within the last <max age seconds>:
#   .github/scripts/check-job.sh caj-wrg-staging-scraper rg-wrg-staging 86400
# No execution at all fails too: a check dispatched before the first run must fail (the spec's acceptance).
set -euo pipefail

job="${1:?job name}"
rg="${2:?resource group}"
max_age="${3:-86400}"

latest="$(az containerapp job execution list --name "$job" --resource-group "$rg" \
  --query 'sort_by(@, &properties.startTime)[-1].{name: name, status: properties.status, start: properties.startTime}' -o json)"

if [[ -z "$latest" || "$latest" == "null" ]]; then
  echo "::error::$job has no execution yet" >&2
  exit 1
fi

name="$(echo "$latest" | jq -r .name)"
status="$(echo "$latest" | jq -r .status)"
start="$(echo "$latest" | jq -r .start)"
age=$(( $(date +%s) - $(date -d "$start" +%s) ))
echo "$job latest execution $name: $status, started $start (${age}s ago)"

if [[ "$status" != "Succeeded" ]]; then
  echo "::error::$job latest execution $name is $status" >&2
  exit 1
fi
if [[ "$age" -gt "$max_age" ]]; then
  echo "::error::$job latest execution is ${age}s old, more than ${max_age}s" >&2
  exit 1
fi
