using LightJSC.Core.Models;

namespace LightJSC.Core.Interfaces;

public interface IWatchlistRepository
{
    Task<IReadOnlyList<WatchlistEntry>> FetchUpdatedAsync(DateTime sinceUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<WatchlistEntry>> FetchAllActiveAsync(CancellationToken cancellationToken);
}

