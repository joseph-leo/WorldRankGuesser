using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SportsRankingService.Services;
using SportsRankingService.Enums;
using SportsRankingService.Models;

namespace SportsRankingService
{
    public class Worker(ILogger<Worker> logger, IRankingUpdater rankingUpdater) : BackgroundService
    {
        private readonly ILogger<Worker> _logger = logger;
        //private readonly IServiceProvider _serviceProvider = serviceProvider;
        private readonly IRankingUpdater _rankingUpdater = rankingUpdater;
        //private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                try
                {
                    await _rankingUpdater.UpdateRankingsAsync(RankingType.World, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A failed tick must not stop the host (BackgroundServiceExceptionBehavior.StopHost is the default).
                    _logger.LogError(ex, "Ranking update tick failed");
                }

                await Task.Delay(100000, stoppingToken);
            }
        }
    }
}