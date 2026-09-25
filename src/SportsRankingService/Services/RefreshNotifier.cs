using System.Text.Json;
using Microsoft.Extensions.Options;
using SportsRankingService.Configuration;

namespace SportsRankingService.Services;

/// <summary>
/// After a run that stored at least one new release, one POST tells the game to re-read the rankings view, so a
/// scrape reaches new boards within seconds instead of at the game's next 12-hour refresh
/// (docs/superpowers/specs/2026-09-25-rankings-refresh-notification-design.md). The only link to the game besides the
/// view, and it carries no data: "read again". A failure is a warning and never changes the run's exit code, since
/// the game's timer still covers it; the client waits two minutes because the game may first have to wake its paused
/// database. Nothing configured: nothing sent, said once.
/// </summary>
public sealed class RefreshNotifier
{
    public const string ClientName = "notify";
    public const string TokenHeader = "X-Refresh-Token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NotifyOptions _options;
    private readonly ILogger<RefreshNotifier> _logger;

    public RefreshNotifier(IHttpClientFactory httpClientFactory, IOptions<NotifyOptions> options, ILogger<RefreshNotifier> logger)
        : this(httpClientFactory, options.Value, logger)
    {
    }

    internal RefreshNotifier(IHttpClientFactory httpClientFactory, NotifyOptions options, ILogger<RefreshNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyAsync(UpdateSummary summary, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Url))
        {
            _logger.LogInformation("Notify:Url is not configured: no game is told about new releases");
            return;
        }

        if (summary.Inserted == 0)
        {
            return;
        }

        try
        {
            HttpClient client = _httpClientFactory.CreateClient(ClientName);
            using HttpRequestMessage request = new(HttpMethod.Post, _options.Url);
            request.Headers.TryAddWithoutValidation(TokenHeader, _options.Token ?? "");
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("The game answered {StatusCode} to the refresh notification; its next timed refresh covers it", (int)response.StatusCode);
                return;
            }

            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _logger.LogInformation("Game notified: {Rows} rows, {Countries} drawable countries",
                body.RootElement.GetProperty("rows").GetInt32(), body.RootElement.GetProperty("drawableCountries").GetInt32());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "The refresh notification to the game failed; its next timed refresh covers it");
        }
    }
}
