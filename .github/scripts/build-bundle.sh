#!/usr/bin/env bash
# Builds a self-contained linux-x64 EF Core migrations bundle for one project:
#   .github/scripts/build-bundle.sh src/WorldRankGuesser.Api artifacts/efbundle-game
#   .github/scripts/build-bundle.sh src/SportsRankingService artifacts/efbundle-scraper
# `dotnet ef` runs the project's Main at design time to find the DbContext. The API's context factory needs a
# connection string to be *configured* (nothing is connected to), and Production keeps appsettings.Development.json
# out; the scraper's appsettings.json carries its own dev string. The bundle's own run gets the real string through
# the same variable (never --connection: the app's startup demands a configured string before it could apply).
# `--target-runtime` is the bundle's option; the common `--runtime` would only set the restore RID.
set -euo pipefail

project="${1:?project directory}"
output="${2:?output path}"

export ConnectionStrings__WorldRankGuesserConnection="${ConnectionStrings__WorldRankGuesserConnection:-Server=design-time;Database=WorldRankGuesser;Encrypt=True}"
export ASPNETCORE_ENVIRONMENT=Production

dotnet tool restore
dotnet ef migrations bundle \
  --project "$project" --startup-project "$project" --configuration Release \
  --self-contained --target-runtime linux-x64 \
  --output "$output" --force
