using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Models;
using TMDbLib.Objects.TvShows;

using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShowOrganizer.Services
{
    public class TmdbExactOrderResolver : IDisposable
    {
        private readonly TmdbClientService _tmdbClientService;
        private readonly ILogger<TmdbExactOrderResolver>? _logger;

        public TmdbExactOrderResolver(TmdbClientService tmdbClientService)
            : this(tmdbClientService, null)
        {
        }

        public TmdbExactOrderResolver(TmdbClientService tmdbClientService, ILogger<TmdbExactOrderResolver>? logger)
        {
            _tmdbClientService = tmdbClientService;
            _logger = logger;
            _logger?.LogInformation("ShowOrganizer: TmdbExactOrderResolver created.");
        }

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

            var targetGroupOrder = customSeasonNumber - 1;
            var season = groupCollection.Groups.Find(s => s.Order == targetGroupOrder);
            if (season?.Episodes == null)
            {
                return (-1, -1);
            }

            var episode = season.Episodes.Find(e => e.Order == customEpisodeNumber - 1);
            if (episode != null)
            {
                return (episode.SeasonNumber, episode.EpisodeNumber);
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
                _logger?.LogInformation("ShowOrganizer: TmdbExactOrderResolver disposed.");
            }
        }
    }
}
