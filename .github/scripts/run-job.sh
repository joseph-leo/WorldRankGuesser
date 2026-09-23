#!/usr/bin/env bash
# Starts a Container Apps Job once and waits for that execution to succeed:
#   .github/scripts/run-job.sh caj-wrg-staging-scraper rg-wrg-staging 3900
# The scraper exits 1 when any feed failed, which marks the execution Failed; the Job's one retry then runs it
# again, so the wait allows two 30-minute runs and some slack. Status values: Running, Processing, Succeeded,
# Failed, Stopped, Degraded and Unknown; Unknown is polled through like Running.
set -euo pipefail

job="${1:?job name}"
rg="${2:?resource group}"
timeout="${3:-3900}"

execution="$(az containerapp job start --name "$job" --resource-group "$rg" --query name -o tsv)"
echo "started $job execution $execution"
deadline=$(( $(date +%s) + timeout ))
misses=0

while :; do
  # A status read can fail transiently (throttling, a network blip) during a long poll; five in a row is a failure.
  if status="$(az containerapp job execution show --name "$job" --resource-group "$rg" --job-execution-name "$execution" --query properties.status -o tsv 2>/dev/null)" && [[ -n "$status" ]]; then
    misses=0
    echo "$(date -u +%T) $status"
  else
    misses=$((misses + 1))
    status=""
    echo "$(date -u +%T) could not read the execution status (miss $misses of 5)"
    if [[ "$misses" -ge 5 ]]; then
      echo "::error::could not read the status of $job execution $execution five times in a row" >&2
      exit 1
    fi
  fi
  case "$status" in
    Succeeded) exit 0 ;;
    Failed|Stopped|Degraded)
      echo "::error::$job execution $execution ended with $status; read its console logs in Log Analytics (ContainerAppConsoleLogs_CL) or with: az containerapp job logs show --name $job --resource-group $rg --execution $execution --container scraper" >&2
      exit 1 ;;
  esac
  if [[ "$(date +%s)" -ge "$deadline" ]]; then
    echo "::error::timed out after ${timeout}s waiting for $job execution $execution" >&2
    exit 1
  fi
  sleep 20
done
