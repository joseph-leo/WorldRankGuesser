# The scraper image. Build from the repo root:
#   docker build -f src/SportsRankingService/Dockerfile -t worldrankguesser-scraper .
# Runs every enabled feed once and exits 0, or 1 if any feed failed; arguments after the image name are the
# scraper's (`--only Soccer`). The .NET tags follow global.json: when it moves, they move.

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/SportsRankingService/ src/SportsRankingService/
RUN dotnet publish src/SportsRankingService -c Release -o /app -p:UseAppHost=false

# The plain runtime, not chiseled: CurlFetcher starts curl as a process, so the image needs curl and root CAs.
FROM mcr.microsoft.com/dotnet/runtime:11.0.0-rc.1-resolute
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "SportsRankingService.dll"]
