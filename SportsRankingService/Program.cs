using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService;
using SportsRankingService.RankingsDb;
using SportsRankingService.Factories;
using SportsRankingService.Repository;
using SportsRankingService.Services;
using System;

// Run the worker service
IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((Action<HostBuilderContext, IServiceCollection>)((hostContext, services) =>
    {
        services.AddHostedService<Worker>();

        IConfiguration configuration = hostContext.Configuration;


        services.AddDbContext<WorldRankGuesserContext>(
            options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection") + ";Encrypt=False"));

        //services.AddScoped<IWorldRankRepository, WorldRankRepository>();
        services.AddTransient<IRankingUpdater, RankingUpdater>();
        services.AddParserFactory();
        services.AddScrapeServiceFactory();

    })).ConfigureLogging(logging =>
    {
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
    })
    .Build();

host.Run();

