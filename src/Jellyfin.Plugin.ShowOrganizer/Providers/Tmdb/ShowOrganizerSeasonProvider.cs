using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.ShowOrganizer.Providers.Tmdb;

public class ShowOrganizerSeasonProvider(
    TmdbClientService tmdbClientService,
    IHttpClientFactory httpClientFactory,
    ILogger<ShowOrganizerSeasonProvider> logger,
    ShowOrganizerEligibilityEvaluator? eligibilityEvaluator = null) : IRemoteMetadataProvider<Season, SeasonInfo>
{
    private readonly TmdbClientService _tmdbClientService = tmdbClientService;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<ShowOrganizerSeasonProvider> _logger = logger;
    private readonly ShowOrganizerEligibilityEvaluator _eligibilityEvaluator = eligibilityEvaluator ?? new ShowOrganizerEligibilityEvaluator();

    public string Name => "ShowOrganizer";

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Season>();

        var seriesIdentity = ShowOrganizerEligibilityEvaluator.GetSeriesIdentity(info);
        var eligibility = _eligibilityEvaluator.Evaluate(info.SeriesProviderIds, seriesIdentity, _logger);
        if (eligibility.State != ShowOrganizerEligibilityState.Eligible)
        {
            return result;
        }

        var orderRef = eligibility.OrderReference!;
        var seriesTmdbId = eligibility.SeriesTmdbId;

        var customSeasonNumber = info.IndexNumber;
        if (!customSeasonNumber.HasValue || customSeasonNumber.Value <= 0)
        {
            return result;
        }

        var groupCollection = await _tmdbClientService.GetTvEpisodeGroupsAsync(
            seriesTmdbId,
            orderRef.OrderId,
            info.MetadataLanguage,
            cancellationToken).ConfigureAwait(false);

        if (groupCollection?.Groups == null)
        {
            return result;
        }

        TvGroup? matchingGroup = null;
        var groups = groupCollection.Groups;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].Order == customSeasonNumber.Value)
            {
                matchingGroup = groups[i];
                break;
            }
        }

        if (matchingGroup == null)
        {
            return result;
        }

        var cleanName = matchingGroup.Name?.Trim(' ', '"') ?? string.Empty;
        _logger.LogDebug("ShowOrganizer: Mapped custom season S{Season:02} -> episode group Order {GroupOrder} ({GroupName}).", customSeasonNumber.Value, customSeasonNumber.Value, cleanName);

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
