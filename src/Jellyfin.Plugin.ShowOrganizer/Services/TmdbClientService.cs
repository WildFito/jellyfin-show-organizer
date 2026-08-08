using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TMDbLib.Client;
using TMDbLib.Objects.TvShows;

namespace Jellyfin.Plugin.ShowOrganizer.Services
{
    public class TmdbClientService : IDisposable
    {
        private const int CacheDurationInHours = 1;

        private readonly IMemoryCache _memoryCache;
        private readonly IPluginManager? _pluginManager;
        private readonly ILogger<TmdbClientService>? _logger;
        private TMDbClient? _tmDbClient;
        private readonly object _clientLock = new object();
        private bool _credentialLogged = false;

        public TmdbClientService(IMemoryCache memoryCache)
            : this(memoryCache, null, null)
        {
        }

        public TmdbClientService(IMemoryCache memoryCache, IPluginManager? pluginManager, ILogger<TmdbClientService>? logger)
        {
            _memoryCache = memoryCache;
            _pluginManager = pluginManager;
            _logger = logger;
        }

        protected virtual TMDbClient? GetClient()
        {
            if (_tmDbClient == null)
            {
                lock (_clientLock)
                {
                    if (_tmDbClient == null)
                    {
                        var apiKey = ResolveTmdbApiKey();
                        if (string.IsNullOrWhiteSpace(apiKey))
                        {
                            return null;
                        }

                        _tmDbClient = new TMDbClient(apiKey)
                        {
                            ThrowApiExceptions = false
                        };
                    }
                }
            }
            return _tmDbClient;
        }

        public virtual string? ResolveTmdbApiKey()
        {
            var overrideKey = Plugin.Instance?.Configuration?.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(overrideKey))
            {
                if (!_credentialLogged)
                {
                    _logger?.LogInformation("Using ShowOrganizer-configured TMDb credentials.");
                    _credentialLogged = true;
                }
                return overrideKey.Trim();
            }

            var jellyfinKey = GetJellyfinTmdbApiKey();
            if (!string.IsNullOrWhiteSpace(jellyfinKey))
            {
                if (!_credentialLogged)
                {
                    _logger?.LogInformation("Using Jellyfin TMDb credentials.");
                    _credentialLogged = true;
                }
                return jellyfinKey.Trim();
            }

            if (!_credentialLogged)
            {
                _logger?.LogWarning("No usable TMDb API credentials are available. Episode-group lookups will fail.");
                _credentialLogged = true;
            }

            return null;
        }

        private string? GetJellyfinTmdbApiKey()
        {
            if (_pluginManager == null)
            {
                return null;
            }

            try
            {
                foreach (var localPlugin in _pluginManager.Plugins)
                {
                    var instance = localPlugin.Instance;
                    if (instance == null)
                    {
                        continue;
                    }

                    if (instance.Name.Contains("MovieDb", StringComparison.OrdinalIgnoreCase) ||
                        instance.Name.Contains("TMDB", StringComparison.OrdinalIgnoreCase))
                    {
                        if (instance is IHasPluginConfiguration configPlugin)
                        {
                            var config = configPlugin.Configuration;
                            if (config != null)
                            {
                                var prop = config.GetType().GetProperty("TmdbApiKey", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                if (prop != null)
                                {
                                    var val = prop.GetValue(config) as string;
                                    if (!string.IsNullOrWhiteSpace(val))
                                    {
                                        return val;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error checking Jellyfin TMDb plugin configuration.");
            }

            return null;
        }

        public virtual async Task<TvGroupCollection?> GetTvEpisodeGroupsAsync(int tvShowId, string groupId, string? language, CancellationToken cancellationToken)
        {
            var normalizedLanguage = NormalizeLanguage(language);
            var key = $"group-{tvShowId}-{groupId}-{normalizedLanguage}";

            if (_memoryCache.TryGetValue(key, out TvGroupCollection? cachedCollection))
            {
                _logger?.LogDebug("Cache hit for TMDb episode group {GroupId} for series {SeriesId}.", groupId, tvShowId);
                return cachedCollection;
            }

            var client = GetClient();
            if (client == null)
            {
                _logger?.LogWarning("Failed to retrieve TMDb episode group {GroupId} for series {SeriesId}: No usable TMDb client.", groupId, tvShowId);
                return null;
            }

            TvGroupCollection? collection = null;
            try
            {
                collection = await client.GetTvEpisodeGroupsAsync(groupId, normalizedLanguage, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to retrieve TMDb episode group {GroupId} for series {SeriesId}: API request exception.", groupId, tvShowId);
                return null;
            }

            if (collection != null && collection.Groups != null)
            {
                _memoryCache.Set(key, collection, TimeSpan.FromHours(CacheDurationInHours));
                _logger?.LogInformation("Retrieved TMDb episode group {GroupId} for series {SeriesId}: \"{GroupName}\" ({GroupCount} groups).", groupId, tvShowId, collection.Name, collection.Groups.Count);
            }
            else
            {
                _logger?.LogWarning("Failed to retrieve TMDb episode group {GroupId} for series {SeriesId}: Group not found or TMDb API error.", groupId, tvShowId);
            }

            return collection;
        }

        public virtual async Task<TvEpisode?> GetTvEpisodeAsync(int tvShowId, int seasonNumber, int episodeNumber, string? language, string? imageLanguages, string? countryCode, CancellationToken cancellationToken)
        {
            var client = GetClient();
            if (client == null)
            {
                return null;
            }

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
            return client?.GetImageUrl("original", path, true)?.ToString();
        }

        public string? GetImageUrl(string size, string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            var client = GetClient();
            return client?.GetImageUrl(size, path, true)?.ToString();
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
