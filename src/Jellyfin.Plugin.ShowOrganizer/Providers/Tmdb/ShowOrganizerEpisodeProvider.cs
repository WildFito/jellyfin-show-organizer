using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ShowOrganizer.Models;
using Jellyfin.Plugin.ShowOrganizer.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.ShowOrganizer.Providers.Tmdb
{
    public class ShowOrganizerEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private readonly TmdbClientService _tmdbClientService;
        private readonly TmdbExactOrderResolver _resolver;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ShowOrganizerEpisodeProvider> _logger;

        public ShowOrganizerEpisodeProvider(
            TmdbClientService tmdbClientService,
            TmdbExactOrderResolver resolver,
            IHttpClientFactory httpClientFactory,
            ILogger<ShowOrganizerEpisodeProvider> logger)
        {
            _tmdbClientService = tmdbClientService;
            _resolver = resolver;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public int Order => 0;

        public string Name => "ShowOrganizer";

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            if (!searchInfo.IndexNumber.HasValue)
            {
                return Enumerable.Empty<RemoteSearchResult>();
            }

            var metadataResult = await GetMetadata(searchInfo, cancellationToken).ConfigureAwait(false);

            if (!metadataResult.HasMetadata)
            {
                return Enumerable.Empty<RemoteSearchResult>();
            }

            var item = metadataResult.Item;

            return new[]
            {
                new RemoteSearchResult
                {
                    IndexNumber = item.IndexNumber,
                    Name = item.Name,
                    ParentIndexNumber = item.ParentIndexNumber,
                    PremiereDate = item.PremiereDate,
                    ProductionYear = item.ProductionYear,
                    ProviderIds = item.ProviderIds,
                    SearchProviderName = Name,
                    IndexNumberEnd = item.IndexNumberEnd
                }
            };
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var metadataResult = new MetadataResult<Episode>();

            if (info.IsMissingEpisode)
            {
                return metadataResult;
            }

            // Check opt-in: Series.ProviderIds["ShowOrganizer"]
            if (!info.SeriesProviderIds.TryGetValue("ShowOrganizer", out string? showOrganizerId) || string.IsNullOrWhiteSpace(showOrganizerId))
            {
                return metadataResult; // No metadata, let next provider run
            }

            if (!ShowOrderReference.TryParse(showOrganizerId, out var orderRef))
            {
                _logger.LogWarning("ShowOrganizer ID '{Id}' is malformed.", showOrganizerId);
                return metadataResult;
            }

            if (orderRef.Provider != "tmdb")
            {
                _logger.LogWarning("ShowOrganizer provider '{Provider}' is unsupported.", orderRef.Provider);
                return metadataResult;
            }

            info.SeriesProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out string? tmdbId);
            var seriesTmdbId = Convert.ToInt32(tmdbId, CultureInfo.InvariantCulture);
            if (seriesTmdbId <= 0)
            {
                return metadataResult;
            }

            var seasonNumber = info.ParentIndexNumber ?? 1;
            var episodeNumber = info.IndexNumber;

            if (!episodeNumber.HasValue)
            {
                return metadataResult;
            }

            var (resolvedSeason, resolvedEpisode) = await _resolver.ResolveCoordinatesAsync(
                seriesTmdbId,
                seasonNumber,
                episodeNumber.Value,
                orderRef,
                info.MetadataLanguage,
                cancellationToken).ConfigureAwait(false);

            if (resolvedSeason <= 0 || resolvedEpisode <= 0)
            {
                _logger.LogWarning("ShowOrganizer: Failed to map custom S{Season:02}E{Episode:02} for series {SeriesId} using group {GroupId}.", seasonNumber, episodeNumber.Value, seriesTmdbId, orderRef.OrderId);
                metadataResult.HasMetadata = false;
                metadataResult.Item = null!;
                return metadataResult;
            }

            _logger.LogInformation("Activated for series (TMDb {TmdbId}) using episode group {GroupId}.", seriesTmdbId, orderRef.OrderId);
            _logger.LogDebug("Mapped custom S{Season:02}E{Episode:02} -> TMDb S{CanonicalSeason:02}E{CanonicalEpisode:02} using group {GroupId}.", seasonNumber, episodeNumber.Value, resolvedSeason, resolvedEpisode, orderRef.OrderId);

            TvEpisode? episodeResult = null;
            if (info.IndexNumberEnd.HasValue)
            {
                var startindex = episodeNumber.Value;
                var endindex = info.IndexNumberEnd.Value;
                List<TvEpisode>? result = null;

                for (int episode = startindex; episode <= endindex; episode++)
                {
                    var (currSeason, currEpisode) = await _resolver.ResolveCoordinatesAsync(
                        seriesTmdbId,
                        seasonNumber,
                        episode,
                        orderRef,
                        info.MetadataLanguage,
                        cancellationToken).ConfigureAwait(false);

                    var episodeInfo = await _tmdbClientService.GetTvEpisodeAsync(seriesTmdbId, currSeason, currEpisode, info.MetadataLanguage, null, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
                    if (episodeInfo is not null)
                    {
                        (result ??= new List<TvEpisode>()).Add(episodeInfo);
                    }
                }

                if (result is not null)
                {
                    episodeResult = new TvEpisode()
                    {
                        Name = result[0].Name,
                        Overview = result[0].Overview,
                        AirDate = result[0].AirDate,
                        VoteAverage = result[0].VoteAverage,
                        ExternalIds = result[0].ExternalIds,
                        Videos = result[0].Videos,
                        Credits = result[0].Credits
                    };

                    if (result.Count > 1)
                    {
                        var name = new StringBuilder(episodeResult.Name);
                        var overview = new StringBuilder(episodeResult.Overview);

                        for (int i = 1; i < result.Count; i++)
                        {
                            name.Append(" / ").Append(result[i].Name);
                            overview.Append(" / ").Append(result[i].Overview);
                        }

                        episodeResult.Name = name.ToString();
                        episodeResult.Overview = overview.ToString();
                    }
                }
                else
                {
                    return metadataResult;
                }
            }
            else
            {
                episodeResult = await _tmdbClientService.GetTvEpisodeAsync(seriesTmdbId, resolvedSeason, resolvedEpisode, info.MetadataLanguage, null, info.MetadataCountryCode, cancellationToken).ConfigureAwait(false);
            }

            if (episodeResult is null)
            {
                return metadataResult;
            }

            metadataResult.HasMetadata = true;
            metadataResult.QueriedById = true;

            if (!string.IsNullOrEmpty(episodeResult.Overview))
            {
                metadataResult.ResultLanguage = info.MetadataLanguage;
            }

            var item = new Episode
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                IndexNumberEnd = info.IndexNumberEnd,
                Name = episodeResult.Name,
                PremiereDate = episodeResult.AirDate,
                ProductionYear = episodeResult.AirDate?.Year,
                Overview = episodeResult.Overview,
                CommunityRating = Convert.ToSingle(episodeResult.VoteAverage)
            };

            var externalIds = episodeResult.ExternalIds;
            item.TrySetProviderId(MetadataProvider.Tvdb, externalIds?.TvdbId);
            item.TrySetProviderId(MetadataProvider.Imdb, externalIds?.ImdbId);
            item.TrySetProviderId(MetadataProvider.TvRage, externalIds?.TvrageId);

            if (episodeResult.Videos?.Results is not null)
            {
                foreach (var video in episodeResult.Videos.Results)
                {
                    if (IsTrailerType(video))
                    {
                        item.AddTrailerUrl("https://www.youtube.com/watch?v=" + video.Key);
                    }
                }
            }

            var credits = episodeResult.Credits;
            var config = Plugin.Instance?.Configuration;
            var hideCast = config?.HideMissingCastMembers ?? false;
            var maxCast = config?.MaxCastMembers ?? 20;

            if (credits?.Cast is not null)
            {
                var castQuery = hideCast
                    ? credits.Cast.Where(a => !string.IsNullOrEmpty(a.ProfilePath)).OrderBy(a => a.Order)
                    : credits.Cast.OrderBy(a => a.Order);

                foreach (var actor in castQuery.Take(maxCast))
                {
                    if (string.IsNullOrWhiteSpace(actor.Name))
                    {
                        continue;
                    }

                    var personInfo = new PersonInfo
                    {
                        Name = actor.Name.Trim(),
                        Role = actor.Character?.Trim() ?? string.Empty,
                        Type = PersonKind.Actor,
                        SortOrder = actor.Order,
                        ImageUrl = _tmdbClientService.GetProfileUrl(actor.ProfilePath)
                    };

                    if (actor.Id > 0)
                    {
                        personInfo.SetProviderId(MetadataProvider.Tmdb, actor.Id.ToString(CultureInfo.InvariantCulture));
                    }

                    metadataResult.AddPerson(personInfo);
                }
            }

            if (credits?.GuestStars is not null)
            {
                var guestQuery = hideCast
                    ? credits.GuestStars.Where(a => !string.IsNullOrEmpty(a.ProfilePath)).OrderBy(a => a.Order)
                    : credits.GuestStars.OrderBy(a => a.Order);

                foreach (var guest in guestQuery.Take(maxCast))
                {
                    if (string.IsNullOrWhiteSpace(guest.Name))
                    {
                        continue;
                    }

                    var personInfo = new PersonInfo
                    {
                        Name = guest.Name.Trim(),
                        Role = guest.Character?.Trim() ?? string.Empty,
                        Type = PersonKind.GuestStar,
                        SortOrder = guest.Order,
                        ImageUrl = _tmdbClientService.GetProfileUrl(guest.ProfilePath)
                    };

                    if (guest.Id > 0)
                    {
                        personInfo.SetProviderId(MetadataProvider.Tmdb, guest.Id.ToString(CultureInfo.InvariantCulture));
                    }

                    metadataResult.AddPerson(personInfo);
                }
            }

            var hideCrew = config?.HideMissingCrewMembers ?? false;
            var maxCrew = config?.MaxCrewMembers ?? 10;

            if (credits?.Crew is not null)
            {
                var crewQuery = credits.Crew
                    .Select(crewMember => new
                    {
                        CrewMember = crewMember,
                        PersonType = MapCrewToPersonType(crewMember)
                    })
                    .Where(entry => entry.PersonType == PersonKind.Director || entry.PersonType == PersonKind.Writer || entry.PersonType == PersonKind.Producer);

                if (hideCrew)
                {
                    crewQuery = crewQuery.Where(entry => !string.IsNullOrEmpty(entry.CrewMember.ProfilePath));
                }

                foreach (var entry in crewQuery.Take(maxCrew))
                {
                    var crewMember = entry.CrewMember;

                    if (string.IsNullOrWhiteSpace(crewMember.Name))
                    {
                        continue;
                    }

                    var personInfo = new PersonInfo
                    {
                        Name = crewMember.Name.Trim(),
                        Role = crewMember.Job?.Trim() ?? string.Empty,
                        Type = entry.PersonType,
                        ImageUrl = _tmdbClientService.GetProfileUrl(crewMember.ProfilePath)
                    };

                    if (crewMember.Id > 0)
                    {
                        personInfo.SetProviderId(MetadataProvider.Tmdb, crewMember.Id.ToString(CultureInfo.InvariantCulture));
                    }

                    metadataResult.AddPerson(personInfo);
                }
            }

            metadataResult.Item = item;
            return metadataResult;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(NamedClient.Default).GetAsync(url, cancellationToken);
        }

        private static bool IsTrailerType(TMDbLib.Objects.General.Video video)
        {
            return string.Equals(video.Type, "Trailer", StringComparison.OrdinalIgnoreCase)
                && string.Equals(video.Site, "YouTube", StringComparison.OrdinalIgnoreCase);
        }

        private static PersonKind MapCrewToPersonType(TMDbLib.Objects.General.Crew crewMember)
        {
            if (string.Equals(crewMember.Department, "Directing", StringComparison.OrdinalIgnoreCase))
            {
                return PersonKind.Director;
            }
            if (string.Equals(crewMember.Department, "Writing", StringComparison.OrdinalIgnoreCase))
            {
                return PersonKind.Writer;
            }
            if (string.Equals(crewMember.Department, "Production", StringComparison.OrdinalIgnoreCase)
                && string.Equals(crewMember.Job, "Producer", StringComparison.OrdinalIgnoreCase))
            {
                return PersonKind.Producer;
            }
            return PersonKind.Unknown;
        }
    }
}
