using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Models;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.ShowOrganizer.Services
{
    public class TmdbExactOrderResolver
    {
        private readonly TmdbClientService _tmdbClientService;

        public TmdbExactOrderResolver(TmdbClientService tmdbClientService)
        {
            _tmdbClientService = tmdbClientService;
        }

        public virtual async Task<(int SeasonNumber, int EpisodeNumber)> ResolveCoordinatesAsync(
            int seriesTmdbId,
            int customSeasonNumber,
            int customEpisodeNumber,
            ShowOrderReference orderRef,
            string? language,
            CancellationToken cancellationToken)
        {
            if (orderRef.Provider != "tmdb")
            {
                return (customSeasonNumber, customEpisodeNumber);
            }

            var groupCollection = await _tmdbClientService.GetTvEpisodeGroupsAsync(seriesTmdbId, orderRef.OrderId, language, cancellationToken).ConfigureAwait(false);
            if (groupCollection?.Groups == null)
            {
                return (customSeasonNumber, customEpisodeNumber);
            }

            var season = groupCollection.Groups.Find(s => s.Order == customSeasonNumber);
            if (season?.Episodes == null)
            {
                return (customSeasonNumber, customEpisodeNumber);
            }

            var episode = season.Episodes.Find(e => e.Order == customEpisodeNumber - 1);
            if (episode != null)
            {
                return (episode.SeasonNumber, episode.EpisodeNumber);
            }

            return (customSeasonNumber, customEpisodeNumber);
        }
    }
}
