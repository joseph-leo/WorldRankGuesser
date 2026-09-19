# WorldRankGuesser

A guessing game: you are dealt ten countries, one at a time, and put each into a different sport. You score the
country's world rank in that sport, and the lowest total wins.

- `src/WorldRankGuesser.Api` — ASP.NET Core API (.NET 11) that owns the rules and the state.
- `src/WorldRankGuesser.Web` — SvelteKit front end.
- Rankings are scraped weekly by [SportsRankingService](../SportsRankingService) into SQL Server.

## Running locally

1. In the SportsRankingService repo: `docker compose up -d --wait`, apply its migrations and run it once so the database has rankings.
2. Here: `dotnet tool restore`, then `dotnet ef database update --project src/WorldRankGuesser.Api`.
3. `dotnet run --project src/WorldRankGuesser.Api`
4. In `src/WorldRankGuesser.Web`: `npm install`, then `npm run dev`, and open http://localhost:5173.

Design and plans are in `docs/superpowers/`.
