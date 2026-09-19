using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;
using WorldRankGuesser.Api.Endpoints;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Rankings;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ScoringOptions>()
    .Bind(builder.Configuration.GetSection(ScoringOptions.Section))
    .Validate(o => o.Cap > 0, "Scoring:Cap must be positive.")
    .ValidateOnStart();

builder.Services.AddOptions<GameOptions>()
    .Bind(builder.Configuration.GetSection(GameOptions.Section))
    .Validate(GameOptions.IsValid, "Game: needs at least one category, unique category IDs, and at least one sport per category.")
    .ValidateOnStart();

// The connection string is read when the context is first resolved, so test hosts can override it.
builder.Services.AddDbContext<GameDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(GameDbContext.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{GameDbContext.ConnectionStringName}' is not configured.");

    GameDbContext.Configure(options, connectionString);
});

builder.Services.AddOptions<RankingsOptions>()
    .Bind(builder.Configuration.GetSection(RankingsOptions.Section))
    .Validate(o => o.RefreshMinutes >= 1, "Rankings:RefreshMinutes must be at least 1.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(CountryCatalog.LoadEmbedded());
builder.Services.AddSingleton<IRankingsStore, RankingsStore>();
builder.Services.AddScoped<IRankingsReader, RankingsReader>();
builder.Services.AddHostedService<RankingsRefreshService>();

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

public partial class Program;
