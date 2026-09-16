using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;
using SportsRankingService.Services;

namespace SportsRankingService
{
    public sealed class Worker(IRankingUpdater rankingUpdater, IOptions<WorkerOptions> options, ILogger<Worker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            TimeSpan interval = options.Value.Interval;
            using PeriodicTimer timer = new(interval);

            try
            {
                do
                {
                    logger.LogInformation("Ranking update starting at {Time}; next run in {Interval}", DateTimeOffset.Now, interval);

                    try
                    {
                        await rankingUpdater.UpdateAllAsync(stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        // A failed run must not stop the host (BackgroundServiceExceptionBehavior.StopHost is the default).
                        logger.LogError(ex, "Ranking update run failed");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down.
            }
        }
    }
}
