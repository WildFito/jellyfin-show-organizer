using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Models;
using Jellyfin.Plugin.ShowOrganizer.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShowOrganizer.Providers.Tmdb
{
    public class ShowOrganizerSeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>
    {
        private readonly TmdbClientService _tmdbClientService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ShowOrganizerSeasonProvider> _logger;

        public ShowOrganizerSeasonProvider(
            TmdbClientService tmdbClientService,
            IHttpClientFactory httpClientFactory,
            ILogger<ShowOrganizerSeasonProvider> logger)
        {
            _tmdbClientService = tmdbClientService;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public string Name => "ShowOrganizer";

        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();

            if (!info.SeriesProviderIds.TryGetValue("ShowOrganizer", out string? showOrganizerId) || string.IsNullOrWhiteSpace(showOrganizerId))
            {
                return result;
            }

            if (!ShowOrderReference.TryParse(showOrganizerId, out var orderRef))
            {
                _logger.LogWarning("ShowOrganizer ID '{Id}' is malformed.", showOrganizerId);
                return result;
            }

            if (orderRef.Provider != "tmdb")
            {
                _logger.LogWarning("ShowOrganizer provider '{Provider}' is unsupported.", orderRef.Provider);
                return result;
            }

            info.SeriesProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out string? tmdbId);
            var seriesTmdbId = Convert.ToInt32(tmdbId, CultureInfo.InvariantCulture);
            if (seriesTmdbId <= 0)
            {
                return result;
            }

            var customSeasonNumber = info.IndexNumber;
            if (!customSeasonNumber.HasValue || customSeasonNumber.Value <= 0)
            {
                return result;
            }

            var targetOrder = customSeasonNumber.Value - 1;

            var groupCollection = await _tmdbClientService.GetTvEpisodeGroupsAsync(
                seriesTmdbId,
                orderRef.OrderId,
                info.MetadataLanguage,
                cancellationToken).ConfigureAwait(false);

            if (groupCollection?.Groups == null)
            {
                return result;
            }

            var matchingGroup = groupCollection.Groups.Find(g => g.Order == targetOrder);
            if (matchingGroup == null)
            {
                return result;
            }

            var cleanName = matchingGroup.Name?.Trim(' ', '"') ?? string.Empty;
            _logger.LogDebug("ShowOrganizer: Mapped custom season S{Season:02} -> episode-group Order {GroupOrder} (\"{GroupName}\").", customSeasonNumber.Value, targetOrder, cleanName);

            result.HasMetadata = true;
            result.Item = new Season
            {
                IndexNumber = customSeasonNumber,
                Name = cleanName
            };

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<RemoteSearchResult>>(Array.Empty<RemoteSearchResult>());
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }
    }
}
