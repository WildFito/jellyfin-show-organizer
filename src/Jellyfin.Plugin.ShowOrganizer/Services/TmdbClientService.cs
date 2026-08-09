using System;
using System.Linq;
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
        private const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p/";

        private readonly IMemoryCache _memoryCache;
        private readonly IPluginManager? _pluginManager;
        private readonly ILogger<TmdbClientService>? _logger;
        private TMDbClient? _tmDbClient;
        private readonly object _clientLock = new object();
        private readonly SemaphoreSlim _configLock = new SemaphoreSlim(1, 1);
        private bool _configAttempted = false;
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

        public virtual async Task EnsureClientConfigAsync(CancellationToken cancellationToken = default)
        {
            var client = GetClient();
            if (client == null || client.HasConfig || _configAttempted)
            {
                return;
            }

            await _configLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (client.HasConfig || _configAttempted)
                {
                    return;
                }

                _configAttempted = true;
                try
                {
                    await client.GetConfigAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to retrieve TMDb client configuration. Falling back to standard image CDN URLs.");
                }
            }
            finally
            {
                _configLock.Release();
            }
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
            if (_pluginManager != null)
            {
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
                                    var prop = config.GetType().GetProperty("TmdbApiKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
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

                            var keyFromAsm = GetApiKeyFromAssembly(instance.GetType().Assembly);
                            if (!string.IsNullOrWhiteSpace(keyFromAsm))
                            {
                                return keyFromAsm;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Error checking Jellyfin TMDb plugin configuration.");
                }
            }

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var keyFromAsm = GetApiKeyFromAssembly(asm);
                    if (!string.IsNullOrWhiteSpace(keyFromAsm))
                    {
                        return keyFromAsm;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error checking AppDomain assemblies for TMDb credentials.");
            }

            return null;
        }

        private static string? GetApiKeyFromAssembly(Assembly asm)
        {
            try
            {
                var type = asm.GetType("MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils")
                        ?? asm.GetType("MediaBrowser.Providers.Tmdb.TmdbUtils")
                        ?? asm.GetTypes().FirstOrDefault(t => t.Name.Equals("TmdbUtils", StringComparison.OrdinalIgnoreCase));

                if (type == null)
                {
                    return null;
                }

                var prop = type.GetProperty("ApiKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase)
                        ?? type.GetProperty("TmdbApiKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop != null)
                {
                    var val = prop.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }

                var field = type.GetField("ApiKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase)
                         ?? type.GetField("TmdbApiKey", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase)
                         ?? type.GetField("API_KEY", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (field != null)
                {
                    var val = field.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }
            catch
            {
                // Ignore type load / reflection errors
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
                _logger?.LogDebug("Skipping TMDb episode group retrieval for series {SeriesId}: No usable TMDb client.", tvShowId);
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

                var rawName = collection.Name?.Trim();
                var groupName = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim('"');

                _logger?.LogInformation("Retrieved TMDb episode group {GroupId} for series {SeriesId}: \"{GroupName}\" ({GroupCount} groups).", groupId, tvShowId, groupName, collection.Groups.Count);
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

        public virtual async Task<string?> GetProfileUrlAsync(string? path, CancellationToken cancellationToken = default)
        {
            return await GetImageUrlAsync("original", path, cancellationToken).ConfigureAwait(false);
        }

        public virtual async Task<string?> GetImageUrlAsync(string size, string? path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var cleanPath = path.TrimStart('/');
            var cleanSize = string.IsNullOrWhiteSpace(size) ? "original" : size;

            try
            {
                await EnsureClientConfigAsync(cancellationToken).ConfigureAwait(false);

                var client = GetClient();
                if (client != null && client.HasConfig)
                {
                    var url = client.GetImageUrl(cleanSize, path, true);
                    if (url != null)
                    {
                        return url.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Error formatting TMDb image URL via TMDbClient. Falling back to standard CDN.");
            }

            return $"{TmdbImageBaseUrl}{cleanSize}/{cleanPath}";
        }

        public string? GetProfileUrl(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            return $"{TmdbImageBaseUrl}original/{path.TrimStart('/')}";
        }

        public string? GetImageUrl(string size, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            var cleanSize = string.IsNullOrWhiteSpace(size) ? "original" : size;
            return $"{TmdbImageBaseUrl}{cleanSize}/{path.TrimStart('/')}";
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
                _configLock.Dispose();
            }
        }
    }
}
