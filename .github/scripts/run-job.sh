#!/usr/bin/env bash
# Starts a Container Apps Job once and waits for that execution to succeed:
#   .github/scripts/run-job.sh caj-wrg-staging-scraper rg-wrg-staging 2100
# The scraper exits 1 when any feed failed, which marks the execution Failed; the Job's one retry then runs it
# again, so the wait allows two runs. Status values: Running, Processing, Succeeded, Failed, Stopped, Degraded, Unknown.
set -euo pipefail

job="${1:?job name}"
rg="${2:?resource group}"
timeout="${3:-2100}"

execution="$(az containerapp job start --name "$job" --resource-group "$rg" --query name -o tsv)"
echo "started $job execution $execution"
deadline=$(( $(date +%s) + timeout ))

while :; do
  status="$(az containerapp job execution show --name "$job" --resource-group "$rg" --job-execution-name "$execution" --query properties.status -o tsv)"
  echo "$(date -u +%T) $status"
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
