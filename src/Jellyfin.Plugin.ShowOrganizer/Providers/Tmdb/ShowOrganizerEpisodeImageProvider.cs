using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Models;
using Jellyfin.Plugin.ShowOrganizer.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShowOrganizer.Providers.Tmdb
{
    public class ShowOrganizerEpisodeImageProvider : IRemoteImageProvider, IHasOrder
    {
        private readonly TmdbClientService _tmdbClientService;
        private readonly TmdbExactOrderResolver _resolver;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ShowOrganizerEpisodeImageProvider> _logger;

        public ShowOrganizerEpisodeImageProvider(
            TmdbClientService tmdbClientService,
            TmdbExactOrderResolver resolver,
            IHttpClientFactory httpClientFactory,
            ILogger<ShowOrganizerEpisodeImageProvider> logger)
        {
            _tmdbClientService = tmdbClientService;
            _resolver = resolver;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public int Order => 0;

        public string Name => "ShowOrganizer";

        public bool Supports(BaseItem item)
        {
            return item is Episode;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            yield return ImageType.Primary;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var episode = (Episode)item;
            var series = episode.Series;

            if (series is null)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var showOrganizerId = series.GetProviderId("ShowOrganizer");
            if (string.IsNullOrWhiteSpace(showOrganizerId))
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            if (!ShowOrderReference.TryParse(showOrganizerId, out var orderRef))
            {
                _logger.LogWarning("ShowOrganizer ID '{Id}' is malformed.", showOrganizerId);
                return Enumerable.Empty<RemoteImageInfo>();
            }

            if (orderRef.Provider != "tmdb")
            {
                _logger.LogWarning("ShowOrganizer provider '{Provider}' is unsupported.", orderRef.Provider);
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var seriesTmdbIdStr = series.GetProviderId(MetadataProvider.Tmdb.ToString());
            var seriesTmdbId = Convert.ToInt32(seriesTmdbIdStr, CultureInfo.InvariantCulture);

            if (seriesTmdbId <= 0)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var seasonNumber = episode.ParentIndexNumber ?? 1;
            var episodeNumber = episode.IndexNumber;

            if (!episodeNumber.HasValue)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var language = item.GetPreferredMetadataLanguage();

            var (resolvedSeason, resolvedEpisode) = await _resolver.ResolveCoordinatesAsync(
                seriesTmdbId,
                seasonNumber,
                episodeNumber.Value,
                orderRef,
                language,
                cancellationToken).ConfigureAwait(false);

            var episodeResult = await _tmdbClientService.GetTvEpisodeAsync(
                seriesTmdbId,
                resolvedSeason,
                resolvedEpisode,
                language,
                null,
                null,
                cancellationToken).ConfigureAwait(false);

            var stills = episodeResult?.Images?.Stills;
            if (stills is null)
            {
                if (episodeResult != null && !string.IsNullOrEmpty(episodeResult.StillPath))
                {
                    var imageUrl = await _tmdbClientService.GetImageUrlAsync("original", episodeResult.StillPath, cancellationToken).ConfigureAwait(false);
                    if (imageUrl != null)
                    {
                        return new[]
                        {
                            new RemoteImageInfo
                            {
                                Url = imageUrl,
                                ProviderName = "TheMovieDb",
                                Type = ImageType.Primary,
                                RatingType = RatingType.Score
                            }
                        };
                    }
                }
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var results = new List<RemoteImageInfo>();
            foreach (var img in stills)
            {
                var imageUrl = await _tmdbClientService.GetImageUrlAsync("original", img.FilePath, cancellationToken).ConfigureAwait(false);
                if (imageUrl != null)
                {
                    results.Add(new RemoteImageInfo
                    {
                        Url = imageUrl,
                        CommunityRating = img.VoteAverage,
                        VoteCount = img.VoteCount,
                        Width = img.Width,
                        Height = img.Height,
                        Language = img.Iso_639_1,
                        ProviderName = "TheMovieDb",
                        Type = ImageType.Primary,
                        RatingType = RatingType.Score
                    });
                }
            }
            return results;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
