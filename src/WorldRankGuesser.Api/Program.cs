using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Persistence;

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

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
