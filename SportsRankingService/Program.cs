using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsRankingService;
using SportsRankingService.RankingsDb;
using SportsRankingService.Utilities;
using SportsRankingService.Services;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<Worker>();

        IConfiguration configuration = hostContext.Configuration;

        services.AddSingleton<RugbyService>();

        services.AddDbContext<WorldRankGuesserContext>(
        options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection") + ";Encrypt=False"));
    })
    .Build();



host.Run();
