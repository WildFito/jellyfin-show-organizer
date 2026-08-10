using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ShowOrganizer.Models;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ShowOrganizer.Services
{
    public class ShowOrganizerEligibilityEvaluator
    {
        private static readonly ConcurrentDictionary<string, bool> _loggedWarnings = new(StringComparer.Ordinal);

        public static int ResetState()
        {
            var count = _loggedWarnings.Count;
            _loggedWarnings.Clear();
            return count;
        }

        public virtual ShowOrganizerEligibilityResult Evaluate(
            IReadOnlyDictionary<string, string> seriesProviderIds,
            ILogger? logger)
        {
            return Evaluate(seriesProviderIds, null, logger);
        }

        public virtual ShowOrganizerEligibilityResult Evaluate(
            IReadOnlyDictionary<string, string> seriesProviderIds,
            string? seriesIdentity,
            ILogger? logger)
        {
            if (seriesProviderIds == null)
            {
                return new ShowOrganizerEligibilityResult(
                    ShowOrganizerEligibilityState.Inactive,
                    null,
                    0,
                    "INACTIVE");
            }

            seriesProviderIds.TryGetValue("ShowOrganizer", out string? rawShowOrganizerId);
            seriesProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out string? rawTmdbId);

            var cleanShowOrganizerId = rawShowOrganizerId?.Trim() ?? string.Empty;
            var cleanTmdbId = rawTmdbId?.Trim() ?? string.Empty;
            var seriesKeyPart = string.IsNullOrWhiteSpace(seriesIdentity) ? string.Empty : $"SERIES:{seriesIdentity.Trim()}|";

            if (string.IsNullOrWhiteSpace(cleanShowOrganizerId))
            {
                return new ShowOrganizerEligibilityResult(
                    ShowOrganizerEligibilityState.Inactive,
                    null,
                    0,
                    "INACTIVE");
            }

            int.TryParse(cleanTmdbId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seriesTmdbId);

            if (!ShowOrderReference.TryParse(cleanShowOrganizerId, out var orderRef))
            {
                var fingerprint = $"{seriesKeyPart}MALFORMED|SO:{cleanShowOrganizerId}|TMDB:{cleanTmdbId}";
                if (logger != null && _loggedWarnings.TryAdd(fingerprint, true))
                {
                    logger.LogWarning("ShowOrganizer: Show Group ID '{Id}' is malformed.", cleanShowOrganizerId);
                }

                return new ShowOrganizerEligibilityResult(
                    ShowOrganizerEligibilityState.InvalidReference,
                    null,
                    seriesTmdbId,
                    fingerprint);
            }

            if (!string.Equals(orderRef.Provider, "tmdb", StringComparison.OrdinalIgnoreCase))
            {
                var fingerprint = $"{seriesKeyPart}UNSUPPORTED|SO:{cleanShowOrganizerId}|TMDB:{cleanTmdbId}";
                if (logger != null && _loggedWarnings.TryAdd(fingerprint, true))
                {
                    logger.LogWarning("ShowOrganizer: Show Group provider '{Provider}' is unsupported.", orderRef.Provider);
                }

                return new ShowOrganizerEligibilityResult(
                    ShowOrganizerEligibilityState.UnsupportedProvider,
                    orderRef,
                    seriesTmdbId,
                    fingerprint);
            }

            if (seriesTmdbId <= 0)
            {
                var fingerprint = $"{seriesKeyPart}MISSING_TMDB|SO:{cleanShowOrganizerId}|TMDB:{cleanTmdbId}";
                if (logger != null && _loggedWarnings.TryAdd(fingerprint, true))
                {
                    logger.LogWarning("ShowOrganizer: Series has Show Group ID '{ShowOrganizerId}' configured, but TheMovieDb Programme Id is missing. ShowOrganizer cannot resolve metadata for this series.", cleanShowOrganizerId);
                }

                return new ShowOrganizerEligibilityResult(
                    ShowOrganizerEligibilityState.InvalidMissingTmdbId,
                    orderRef,
                    0,
                    fingerprint);
            }

            var eligibleFingerprint = $"{seriesKeyPart}ELIGIBLE|SO:{orderRef.Provider}:{orderRef.OrderId}|TMDB:{seriesTmdbId}";
            return new ShowOrganizerEligibilityResult(
                ShowOrganizerEligibilityState.Eligible,
                orderRef,
                seriesTmdbId,
                eligibleFingerprint);
        }
    }
}
