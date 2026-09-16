namespace SportsRankingService.Services
{
    public interface IRankingUpdater
    {
        /// <summary>Fetches every enabled feed and inserts its rows. One failing feed does not affect the others.</summary>
        Task UpdateAllAsync(CancellationToken cancellationToken);
    }
}
