using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SportsRankingService.Persistence;
using Testcontainers.MsSql;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// One SQL Server container for the whole test run. Two databases: one with both migration sets (the scraper's dbo,
/// then the game's game schema) and seeded rankings; one with only the game's migrations and so no rankings view
/// (to test readiness failure). Tests share the seeded database, so they must never assert on global row counts.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public string ConnectionString { get; private set; } = "";

    public string EmptyConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        ConnectionString = WithDatabase("WorldRankGuesserTests");
        EmptyConnectionString = WithDatabase("WorldRankGuesserEmpty");

        // dbo first, as on a new database in production: the scraper's tables and views, then the game schema.
        await using (var rankings = CreateRankingsContext(ConnectionString))
        {
            await rankings.Database.MigrateAsync();
        }

        await using (var db = CreateContext(ConnectionString))
        {
            await db.Database.MigrateAsync();
        }

        await RankingsSeed.SaveAsync(ConnectionString);

        await using (var empty = CreateContext(EmptyConnectionString))
        {
            await empty.Database.MigrateAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public GameDbContext CreateContext() => CreateContext(ConnectionString);

    /// <summary>The scraper's context, for the fixture and the seed only; the API never sees it.</summary>
    public static RankingsDbContext CreateRankingsContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RankingsDbContext>().UseSqlServer(connectionString).Options);

    private static GameDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameDbContext>();
        GameDbContext.Configure(options, connectionString);
        return new GameDbContext(options.Options);
    }

    private string WithDatabase(string name) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name }.ConnectionString;
}

[CollectionDefinition("sql")]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>;
