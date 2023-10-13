using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportsRankingService.Models;
using SportsRankingService.RankingsDb;
using SportsRankingService.Utilities;
using SportsRankingService.Services;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SportsRankingService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RugbyService _rugbyService;

        public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider, RugbyService rugbyService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _rugbyService = rugbyService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var ranks = await _rugbyService.GetSportRanksAsync();

                    var dbContext = scope.ServiceProvider.GetRequiredService<WorldRankGuesserContext>();

                    await dbContext.AddRangeAsync(ranks, stoppingToken);
                    dbContext.SaveChanges();

                    string? tableName = GetTableName<SportsRanking>(dbContext);
                    _logger.LogInformation("{RowCount} rows inserted to {db}", ranks.Count, tableName);
                }

                await Task.Delay(10000, stoppingToken);
            }
        }

        private static string GetTableName<TEntity>(DbContext context) where TEntity : class
        {
            string tableName = string.Empty;

            if (context is not null)
            {
                IEntityType? entityType = context.Model.FindEntityType(typeof(TEntity));
                tableName = entityType?.GetTableName() ?? string.Empty;
            }
            
            return tableName;
        }
    }
}