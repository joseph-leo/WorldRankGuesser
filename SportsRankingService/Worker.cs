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

                await DoWorkAsync(stoppingToken);

                //using (var scope = _serviceProvider.CreateScope())
                //{
                //    var ranks = await _scrapeService.GetSportRanksAsync();

                //    var dbContext = scope.ServiceProvider.GetRequiredService<WorldRankGuesserContext>();

                //    await dbContext.AddRangeAsync(ranks, stoppingToken);
                //    dbContext.SaveChanges();

                //    string? tableName = GetTableName<SportsRanking>(dbContext);
                //    _logger.LogInformation("{RowCount} rows inserted to {db}", ranks.Count, tableName);
                //}

                await Task.Delay(100000, stoppingToken);
            }
        }

        private async Task DoWorkAsync(CancellationToken stoppingToken)
        {
            await _rankingUpdater.UpdateRankingsAsync(RankingType.World, stoppingToken);
        }


        //private static string GetTableName(DbContext context)
        //{
        //    string tableName = string.Empty;

        //    if (context is not null)
        //    {
        //        IEntityType? entityType = context.Model.FindEntityType(typeof(TEntity));
        //        tableName = entityType?.GetTableName() ?? string.Empty;
        //    }
            
        //    return tableName;
        //}
    }
}