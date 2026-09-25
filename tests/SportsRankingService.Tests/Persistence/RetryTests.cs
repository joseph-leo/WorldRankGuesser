using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SportsRankingService.Persistence;

namespace SportsRankingService.Tests.Persistence;

public class RetryTests
{
    /// <summary>
    /// A paused Azure SQL database answers 40613 while it resumes (about a minute in staging); the first save of a run
    /// must wait it out instead of failing its feed. Building the context and its strategy never opens a connection.
    /// </summary>
    [Fact]
    public void The_context_retries_transient_failures_long_enough_for_a_resume()
    {
        DbContextOptionsBuilder<RankingsDbContext> options = new();
        RankingsDbContext.Configure(options, "Server=unused;Database=unused");
        using RankingsDbContext db = new(options.Options);

        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

        // Pinned so a later edit cannot quietly shorten the wait.
        Assert.Equal(10, Assert.IsType<SqlServerRetryingExecutionStrategy>(strategy).MaxRetryCount);
    }
}
