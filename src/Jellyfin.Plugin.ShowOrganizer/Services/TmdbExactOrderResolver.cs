using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Models;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.ShowOrganizer.Services;

public class TmdbExactOrderResolver(
    TmdbClientService tmdbClientService,
    ILogger<TmdbExactOrderResolver>? logger = null) : IDisposable
{
    private readonly TmdbClientService _tmdbClientService = tmdbClientService;
    private readonly ILogger<TmdbExactOrderResolver>? _logger = logger;

    public virtual async Task<(int SeasonNumber, int EpisodeNumber)> ResolveCoordinatesAsync(
        int seriesTmdbId,
        int customSeasonNumber,
        int customEpisodeNumber,
        ShowOrderReference orderRef,
        string? language,
        CancellationToken cancellationToken)
    {
        if (orderRef.Provider != "tmdb" || customSeasonNumber <= 0 || customEpisodeNumber <= 0)
        {
            return (-1, -1);
        }

        var groupCollection = await _tmdbClientService.GetTvEpisodeGroupsAsync(seriesTmdbId, orderRef.OrderId, language, cancellationToken).ConfigureAwait(false);
        if (groupCollection?.Groups == null)
        {
            return (-1, -1);
        }

        TvGroup? season = null;
        var groups = groupCollection.Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].Order == customSeasonNumber)
            {
                season = groups[i];
                break;
            }
        }

        if (season?.Episodes == null)
        {
            return (-1, -1);
        }

        var targetOrder = customEpisodeNumber - 1;
        var episodes = season.Episodes;
        for (int i = 0; i < episodes.Count; i++)
        {
            var ep = episodes[i];
            if (ep.Order == targetOrder)
            {
                return (ep.SeasonNumber, (int)ep.EpisodeNumber);
            }
        }

        return (-1, -1);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger?.LogDebug("ShowOrganizer: TmdbExactOrderResolver disposed.");
        }
    }
}
