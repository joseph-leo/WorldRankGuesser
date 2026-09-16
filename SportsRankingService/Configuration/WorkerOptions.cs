namespace SportsRankingService.Configuration;

/// <summary>Bound from the "Worker" section of appsettings.json.</summary>
public sealed class WorkerOptions
{
    /// <summary>Time between ranking updates. Rankings change weekly at most, so daily is plenty.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);
}
