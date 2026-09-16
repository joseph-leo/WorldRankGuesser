using Microsoft.EntityFrameworkCore;
using SportsRankingService;
using SportsRankingService.Configuration;
using SportsRankingService.Persistence;
using SportsRankingService.Services;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        // Next to the binaries (copied on build), so the worker finds it regardless of the working directory.
        config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "serviceconfig.json"), optional: false, reloadOnChange: true);
    })
    .ConfigureServices((hostContext, services) =>
    {
        IConfiguration configuration = hostContext.Configuration;

        services.AddHostedService<Worker>();
        services.Configure<WorkerOptions>(configuration.GetSection("Worker"));
        services.Configure<RankingSourcesOptions>(configuration);

        services.AddDbContext<RankingsDbContext>(
            options => options.UseSqlServer(configuration.GetConnectionString("WorldRankGuesserConnection")));
        services.AddScoped<IRankingRepository, RankingRepository>();

        services.AddRankingPipeline();
    })
    .ConfigureLogging(logging =>
    {
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
    })
    .Build();

host.Run();
