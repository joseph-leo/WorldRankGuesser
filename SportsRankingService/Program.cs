using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService;
using SportsRankingService.RankingsDb;
using SportsRankingService.Factories;
using SportsRankingService.Repository;
using SportsRankingService.Services;

// Run the worker service
IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();

        IConfiguration configuration = hostContext.Configuration;

        services.AddWorldRankServiceFactory();

        services.AddDbContext<WorldRankGuesserContext>(
            options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection") + ";Encrypt=False"));

        services.AddScoped<IWorldRankRepository, WorldRankRepository>();
        services.AddScoped<IRankingUpdater, RankingUpdater>();
    })
    .Build();

host.Run();

