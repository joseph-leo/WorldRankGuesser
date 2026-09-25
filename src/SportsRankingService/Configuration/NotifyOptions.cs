namespace SportsRankingService.Configuration;

/// <summary>
/// The game to tell after a run that stored a new release: its POST /api/rankings/refresh URL and the shared token
/// it checks. Bound from the <c>Notify</c> section: in Azure the Job's environment variables <c>Notify__Url</c> and
/// <c>Notify__Token</c>; unset locally, which sends nothing.
/// </summary>
public sealed class NotifyOptions
{
    public const string SectionName = "Notify";

    /// <summary>Absolute URL of the game's refresh endpoint. Null, empty or blank: no notification.</summary>
    public string? Url { get; set; }

    /// <summary>The token sent in X-Refresh-Token.</summary>
    public string? Token { get; set; }
}
