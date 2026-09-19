using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;
using WorldRankGuesser.Api.Persistence;

namespace WorldRankGuesser.Api.Tests.Integration;

/// <summary>
/// One SQL Server container for the whole test run. Two databases: one migrated and seeded with rankings,
/// one migrated but with no rankings table (to test readiness failure). Tests share the seeded database,
/// so they must never assert on global row counts.
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

        await using (var db = CreateContext(ConnectionString))
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync(RankingsSeed.CreateTableSql);
            await RankingsSeed.InsertAsync(db);
        }

        await using (var empty = CreateContext(EmptyConnectionString))
        {
            await empty.Database.MigrateAsync();
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public GameDbContext CreateContext() => CreateContext(ConnectionString);

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
