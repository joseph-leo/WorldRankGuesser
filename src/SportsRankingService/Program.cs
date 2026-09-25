using Microsoft.EntityFrameworkCore;
using SportsRankingService.Configuration;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

// A run-once console app: an external scheduler (Task Scheduler, cron) runs it weekly.
// `--only <feed>` (repeatable) reruns a subset, e.g. the feeds a previous run reported as failed.
// At design time `dotnet ef` runs this program with its own arguments (--applicationName) only to find the host,
// so they are not a feed filter.
FeedFilter filter = FeedFilter.All;

if (!EF.IsDesignTime)
{
    try
    {
        filter = FeedFilter.Parse(args);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

// The generic host is kept only as the configuration / logging / DI container.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    // The command line holds only --only, which the host's own command-line provider would otherwise read as a config key.
    Args = [],
    // appsettings.json and serviceconfig.json sit next to the binaries, so the working directory does not matter.
    ContentRootPath = AppContext.BaseDirectory,
});

// Required at run time, where it is the feed list; optional at design time (dotnet ef, a migrations bundle), where
// only the DbContext matters and no configuration file travels with a self-contained bundle.
builder.Configuration.AddJsonFile("serviceconfig.json", optional: EF.IsDesignTime);

builder.Services.Configure<RankingSourcesOptions>(builder.Configuration);
// Unset locally (those feeds go direct); the Job sets Proxy__Url and Proxy__Token (infra/scraper.bicep).
builder.Services.Configure<ProxyOptions>(builder.Configuration.GetSection(ProxyOptions.SectionName));
// Unset locally (no game is told); the Job sets Notify__Url and Notify__Token (infra/scraper.bicep).
builder.Services.Configure<NotifyOptions>(builder.Configuration.GetSection(NotifyOptions.SectionName));
builder.Services.AddDbContext<RankingsDbContext>(
    options => RankingsDbContext.Configure(options, builder.Configuration.GetConnectionString("WorldRankGuesserConnection")));
builder.Services.AddScoped<IRankingRepository, RankingRepository>();
builder.Services.AddRankingPipeline();

builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// Build() stays outside the try: `dotnet ef` intercepts it at design time by throwing HostAbortedException.
using IHost host = builder.Build();
ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SportsRankingService");

using CancellationTokenSource shutdown = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

try
{
    using IServiceScope scope = host.Services.CreateScope();
    IRankingUpdater updater = scope.ServiceProvider.GetRequiredService<IRankingUpdater>();

    UpdateSummary summary = await updater.UpdateAllAsync(filter, shutdown.Token);

    // Tells the game to re-read the view when a release was stored; a failure here is logged and never fails the run.
    await host.Services.GetRequiredService<RefreshNotifier>().NotifyAsync(summary, shutdown.Token);

    return summary.Failed > 0 ? 1 : 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    logger.LogWarning("Ranking update cancelled");
    return 1;
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Ranking update run failed");
    return 1;
}
