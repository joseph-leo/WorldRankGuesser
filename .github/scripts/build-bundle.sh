#!/usr/bin/env bash
# Builds a self-contained linux-x64 EF Core migrations bundle for one project:
#   .github/scripts/build-bundle.sh src/WorldRankGuesser.Api artifacts/efbundle-game
#   .github/scripts/build-bundle.sh src/SportsRankingService artifacts/efbundle-scraper
# `dotnet ef` runs the project's Main at design time to find the DbContext. The API's context factory needs a
# connection string to be *configured* (nothing is connected to), and Production keeps appsettings.Development.json
# out; the scraper's appsettings.json carries its own dev string. The bundle's own run gets the real string through
# the same variable (never --connection: the app's startup demands a configured string before it could apply).
# `--target-runtime` is the bundle's option; the common `--runtime` would only set the restore RID.
# `dotnet ef` 11 reads the project's metadata without restoring first, so on a fresh checkout it fails with
# NETSDK1004 (no obj/project.assets.json) before it builds anything: restore explicitly (seen on the first deploy,
# 2026-09-24; a local run only passed because obj already existed).
set -euo pipefail

project="${1:?project directory}"
output="${2:?output path}"

export ConnectionStrings__WorldRankGuesserConnection="${ConnectionStrings__WorldRankGuesserConnection:-Server=design-time;Database=WorldRankGuesser;Encrypt=True}"
export ASPNETCORE_ENVIRONMENT=Production

dotnet tool restore
dotnet restore "$project"
dotnet ef migrations bundle \
  --project "$project" --startup-project "$project" --configuration Release \
  --self-contained --target-runtime linux-x64 \
  --output "$output" --force
