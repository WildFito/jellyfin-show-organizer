using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using TMDbLib.Client;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.ShowOrganizer.Services
{
    public class TmdbClientService : IDisposable
    {
        private const int CacheDurationInHours = 1;

        private readonly IMemoryCache _memoryCache;
        private TMDbClient? _tmDbClient;
        private readonly object _clientLock = new object();

        public TmdbClientService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        private TMDbClient GetClient()
        {
            if (_tmDbClient == null)
            {
                lock (_clientLock)
                {
                    if (_tmDbClient == null)
                    {
                        var apiKey = Plugin.Instance?.Configuration?.TmdbApiKey ?? string.Empty;
                        _tmDbClient = new TMDbClient(apiKey)
                        {
                            ThrowApiExceptions = false
                        };
                    }
                }
            }
            return _tmDbClient;
        }

        public virtual async Task<TvGroupCollection?> GetTvEpisodeGroupsAsync(int tvShowId, string groupId, string? language, CancellationToken cancellationToken)
        {
            var normalizedLanguage = NormalizeLanguage(language);
            var key = $"group-{tvShowId}-{groupId}-{normalizedLanguage}";

            if (_memoryCache.TryGetValue(key, out TvGroupCollection? cachedCollection))
            {
                return cachedCollection;
            }

            var client = GetClient();
            var collection = await client.GetTvEpisodeGroupsAsync(groupId, normalizedLanguage, cancellationToken).ConfigureAwait(false);

            if (collection != null)
            {
                _memoryCache.Set(key, collection, TimeSpan.FromHours(CacheDurationInHours));
            }

            return collection;
        }

        public virtual async Task<TvEpisode?> GetTvEpisodeAsync(int tvShowId, int seasonNumber, int episodeNumber, string? language, string? imageLanguages, string? countryCode, CancellationToken cancellationToken)
        {
            var client = GetClient();
            var normalizedLanguage = NormalizeLanguage(language);
            
            return await client.GetTvEpisodeAsync(
                tvShowId,
                seasonNumber,
                episodeNumber,
                language: normalizedLanguage,
                includeImageLanguage: imageLanguages,
                extraMethods: TvEpisodeMethods.Credits | TvEpisodeMethods.Images | TvEpisodeMethods.ExternalIds | TvEpisodeMethods.Videos,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public string? GetProfileUrl(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var client = GetClient();
            return client.GetImageUrl("original", path, true)?.ToString();
        }

        public string? GetImageUrl(string size, string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var client = GetClient();
            return client.GetImageUrl(size, path, true)?.ToString();
        }

        private static string? NormalizeLanguage(string? language)
        {
            if (string.IsNullOrEmpty(language))
            {
                return language;
            }

            var index = language.IndexOf('-', StringComparison.Ordinal);
            if (index == -1)
            {
                return language;
            }

            return language.Substring(0, index);
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
                _tmDbClient?.Dispose();
            }
        }
    }
}
