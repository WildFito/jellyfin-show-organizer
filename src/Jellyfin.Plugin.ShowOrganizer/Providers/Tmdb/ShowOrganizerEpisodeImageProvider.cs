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
        private readonly ShowOrganizerEligibilityEvaluator _eligibilityEvaluator;

        public ShowOrganizerEpisodeImageProvider(
            TmdbClientService tmdbClientService,
            TmdbExactOrderResolver resolver,
            IHttpClientFactory httpClientFactory,
            ILogger<ShowOrganizerEpisodeImageProvider> logger)
            : this(tmdbClientService, resolver, httpClientFactory, logger, null!)
        {
        }

        public ShowOrganizerEpisodeImageProvider(
            TmdbClientService tmdbClientService,
            TmdbExactOrderResolver resolver,
            IHttpClientFactory httpClientFactory,
            ILogger<ShowOrganizerEpisodeImageProvider> logger,
            ShowOrganizerEligibilityEvaluator eligibilityEvaluator)
        {
            _tmdbClientService = tmdbClientService;
            _resolver = resolver;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _eligibilityEvaluator = eligibilityEvaluator ?? new ShowOrganizerEligibilityEvaluator();
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

            var eligibility = _eligibilityEvaluator.Evaluate(series.ProviderIds, _logger);
            if (eligibility.State != ShowOrganizerEligibilityState.Eligible)
            {
                return Enumerable.Empty<RemoteImageInfo>();
            }

            var orderRef = eligibility.OrderReference!;
            var seriesTmdbId = eligibility.SeriesTmdbId;

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
