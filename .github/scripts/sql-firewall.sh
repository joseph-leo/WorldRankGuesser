#!/usr/bin/env bash
# Admits the runner to the environment's one SQL server for the migration bundle, then removes it:
#   RG=rg-wrg-staging RULE=gha-123-1 .github/scripts/sql-firewall.sh open    # writes fqdn=... to $GITHUB_OUTPUT
#   RG=rg-wrg-staging RULE=gha-123-1 .github/scripts/sql-firewall.sh close
# The runner's address comes from ipify (IPv4; Azure SQL rules are IPv4 only). One server per resource group.
set -euo pipefail

action="${1:?open|close}"
: "${RG:?RG is the resource group}" "${RULE:?RULE is the firewall rule name}"

server="$(az sql server list --resource-group "$RG" --query '[0].name' -o tsv)"
[[ -n "$server" ]] || { echo "::error::no SQL server in resource group $RG" >&2; exit 1; }

case "$action" in
  open)
    ip="$(curl -fsS https://api.ipify.org)"
    az sql server firewall-rule create --resource-group "$RG" --server "$server" --name "$RULE" \
      --start-ip-address "$ip" --end-ip-address "$ip" --output none
    fqdn="$(az sql server show --resource-group "$RG" --name "$server" --query fullyQualifiedDomainName -o tsv)"
    echo "admitted $ip to $fqdn as $RULE"
    echo "fqdn=$fqdn" >> "${GITHUB_OUTPUT:-/dev/stdout}"
    ;;
  close)
    az sql server firewall-rule delete --resource-group "$RG" --server "$server" --name "$RULE" --output none
    echo "removed $RULE from $server"
    ;;
  *)
    echo "usage: sql-firewall.sh open|close" >&2
    exit 2
    ;;
esac
