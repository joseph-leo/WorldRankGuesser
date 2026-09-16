using Microsoft.EntityFrameworkCore;
using SportsRankingService.Configuration;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

// A run-once console app: an external scheduler (Task Scheduler, cron) runs it weekly.
// The generic host is kept only as the configuration / logging / DI container.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // appsettings.json and serviceconfig.json sit next to the binaries, so the working directory does not matter.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Configuration.AddJsonFile("serviceconfig.json", optional: false);

builder.Services.Configure<RankingSourcesOptions>(builder.Configuration);
builder.Services.AddDbContext<RankingsDbContext>(
    options => options.UseSqlServer(builder.Configuration.GetConnectionString("WorldRankGuesserConnection")));
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

    UpdateSummary summary = await updater.UpdateAllAsync(shutdown.Token);

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
