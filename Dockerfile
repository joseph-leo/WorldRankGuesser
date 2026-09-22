# The game image: the SvelteKit build served from the API's wwwroot. Build from the repo root:
#   docker build -t worldrankguesser-game .
# The .NET tags follow global.json: when it moves, they move.

FROM node:24 AS web
WORKDIR /web
COPY src/WorldRankGuesser.Web/package.json src/WorldRankGuesser.Web/package-lock.json ./
RUN npm ci
COPY src/WorldRankGuesser.Web/ ./
# Uses the committed src/lib/api/schema.d.ts; API types are never regenerated here (that needs the built API).
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:11.0.100-rc.1 AS api
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/WorldRankGuesser.Api/ src/WorldRankGuesser.Api/
# Build-time OpenAPI generation boots the app and writes into the Web folder; the committed document is the contract.
RUN dotnet publish src/WorldRankGuesser.Api -c Release -o /app -p:OpenApiGenerateDocuments=false -p:UseAppHost=false
COPY --from=web /web/build /app/wwwroot

# "extra" because Microsoft.Data.SqlClient refuses globalization-invariant mode and only "extra" ships ICU.
# Non-root (user app), no shell, port 8080. No HEALTHCHECK: Container Apps probes the app itself.
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-rc.1-resolute-chiseled-extra AS runtime
WORKDIR /app
COPY --from=api /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorldRankGuesser.Api.dll"]
