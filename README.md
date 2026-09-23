# WorldRankGuesser

A guessing game: you are dealt ten countries, one at a time, and put each into a different sport. You score the
country's world rank in that sport, and the lowest total wins.

- `src/WorldRankGuesser.Api` — ASP.NET Core API (.NET 11) that owns the rules and the state.
- `src/WorldRankGuesser.Web` — SvelteKit front end.
- `src/SportsRankingService` — the scraper that fills the rankings view weekly ([its README](src/SportsRankingService/README.md)).

## Running locally

1. `docker compose up -d --wait`, then `dotnet tool restore`, `dotnet ef database update --project src/SportsRankingService` and `dotnet run --project src/SportsRankingService` so the database has rankings.
2. `dotnet ef database update --project src/WorldRankGuesser.Api`.
3. `dotnet run --project src/WorldRankGuesser.Api`
4. In `src/WorldRankGuesser.Web`: `npm install`, then `npm run dev`, and open http://localhost:5173.

Or everything in containers, the way it is deployed: `docker compose --profile stack up --build -d` (SQL Server, both migration sets, then the game on http://localhost:8080) and `docker compose run --rm scraper` once, so it has rankings.

The game will be live at https://games.foweeti.com (phase 2c, going live, is in progress). Design and plans are in `docs/superpowers/`; how it is deployed is in
`infra/README.md`.

No licence is granted: the code is published to be read, and all rights are reserved. Nobody may rehost the game.
