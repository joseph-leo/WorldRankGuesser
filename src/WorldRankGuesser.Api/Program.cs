using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using WorldRankGuesser.Api.Configuration;
using WorldRankGuesser.Api.Countries;
using WorldRankGuesser.Api.Endpoints;
using WorldRankGuesser.Api.Games;
using WorldRankGuesser.Api.Persistence;
using WorldRankGuesser.Api.Players;
using WorldRankGuesser.Api.Rankings;

var builder = WebApplication.CreateBuilder(args);

// The build-time OpenAPI generator runs this entry point; it must not try to reach a database.
var isOpenApiBuild = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.Services.AddOpenApi();

// Strict numbers: the web default also accepts "12" for 12, which makes every integer `number | string` in the generated types.
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// ---- Options -------------------------------------------------------------------------------------------------------
builder.Services.AddOptions<ScoringOptions>()
    .Bind(builder.Configuration.GetSection(ScoringOptions.Section))
    .Validate(o => o.Cap > 0, "Scoring:Cap must be positive.")
    .ValidateOnStart();

builder.Services.AddOptions<GameOptions>()
    .Bind(builder.Configuration.GetSection(GameOptions.Section))
    .Validate(GameOptions.IsValid, "Game: needs at least one category, unique category IDs, at least one sport per category, and limits that are not negative.")
    .ValidateOnStart();

builder.Services.AddOptions<RankingsOptions>()
    .Bind(builder.Configuration.GetSection(RankingsOptions.Section))
    .Validate(o => o.RefreshMinutes >= 1, "Rankings:RefreshMinutes must be at least 1.")
    .ValidateOnStart();

builder.Services.AddOptions<RateLimitOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitOptions.Section))
    .Validate(o => o.GameStartsPerPlayerPerHour > 0 && o.GameStartsPerIpPerHour > 0, "RateLimits: limits must be positive.")
    .ValidateOnStart();

// ---- Persistence ---------------------------------------------------------------------------------------------------
// The connection string is read when the context is first resolved, so test hosts can override it.
builder.Services.AddDbContext<GameDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString(GameDbContext.ConnectionStringName)
        ?? throw new InvalidOperationException($"Connection string '{GameDbContext.ConnectionStringName}' is not configured.");

    GameDbContext.Configure(options, connectionString);
});

// ---- Rankings and game ---------------------------------------------------------------------------------------------
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(Random.Shared);
builder.Services.AddSingleton(CountryCatalog.LoadEmbedded());
builder.Services.AddSingleton<IRankingsStore, RankingsStore>();
builder.Services.AddScoped<IRankingsReader, RankingsReader>();

if (!isOpenApiBuild)
{
    builder.Services.AddHostedService<RankingsRefreshService>();
}

builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<GameService>();

// ---- Identity: an anonymous player in an HttpOnly cookie -----------------------------------------------------------
// Keys live in the database so cookies survive restarts and scale-to-zero.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("WorldRankGuesser");
if (!isOpenApiBuild)
{
    dataProtection.PersistKeysToDbContext<GameDbContext>();
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = PlayerIdentity.CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(365);
        options.SlidingExpiration = true;

        // An API never redirects to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });

// ---- Rate limiting: game starts only, per player and per IP --------------------------------------------------------
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Matching on the endpoint (not the path string) means a trailing slash or different casing can't bypass the limit:
    // routing has already run by the time UseRateLimiter executes, because minimal hosting inserts UseRouting at the
    // head of the pipeline whenever it isn't called explicitly.
    static bool IsGameStart(HttpContext http) =>
        HttpMethods.IsPost(http.Request.Method)
        && http.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "StartGame";

    static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    static RateLimitPartition<string> HourlyLimit(HttpContext http, string key, Func<RateLimitOptions, int> limit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            // Read at first use, so configuration overrides (tests, environment) are honoured.
            PermitLimit = limit(http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value),
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
        });

    limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(http => IsGameStart(http)
            ? HourlyLimit(http, $"player:{http.User.GetPlayerId()?.ToString() ?? "anonymous:" + Ip(http)}", o => o.GameStartsPerPlayerPerHour)
            : RateLimitPartition.GetNoLimiter("none")),
        PartitionedRateLimiter.Create<HttpContext, string>(http => IsGameStart(http)
            ? HourlyLimit(http, $"ip:{Ip(http)}", o => o.GameStartsPerIpPerHour)
            : RateLimitPartition.GetNoLimiter("none")));
});

// Unhandled errors and bare status codes become RFC 9457 problem details, never HTML or stack traces.
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();      // before the limiter, so the player partition can see the cookie
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthEndpoints();
app.MapGameEndpoints();

// The single-page app's client-side routes (/play/..., /results/...) all load index.html.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
