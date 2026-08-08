using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Configuration;
using Jellyfin.Plugin.ShowOrganizer.Models;
using Jellyfin.Plugin.ShowOrganizer.Providers.Tmdb;
using Jellyfin.Plugin.ShowOrganizer.Services;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Updates;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using TMDbLib.Objects.TvShows;
using Xunit;

namespace Jellyfin.Plugin.ShowOrganizer.Tests
{
    public class ResolverTests
    {
        private class TestMemoryCache : IMemoryCache
        {
            private readonly Dictionary<object, object> _cache = new Dictionary<object, object>();

            public void Dispose() { }

            public ICacheEntry CreateEntry(object key)
            {
                return new TestCacheEntry(key, this);
            }

            public void Remove(object key)
            {
                _cache.Remove(key);
            }

            public bool TryGetValue(object key, out object? value)
            {
                return _cache.TryGetValue(key, out value);
            }

            public void SetValue(object key, object value)
            {
                _cache[key] = value;
            }
        }

        private class TestCacheEntry : ICacheEntry
        {
            private readonly object _key;
            private readonly TestMemoryCache _cache;

            public TestCacheEntry(object key, TestMemoryCache cache)
            {
                _key = key;
                _cache = cache;
            }

            public object Key => _key;
            public object? Value { get; set; }
            public DateTimeOffset? AbsoluteExpiration { get; set; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
            public IList<IChangeToken> ExpirationTokens => new List<IChangeToken>();
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => new List<PostEvictionCallbackRegistration>();
            public CacheItemPriority Priority { get; set; }
            public long? Size { get; set; }

            public void Dispose()
            {
                if (Value != null)
                {
                    _cache.SetValue(_key, Value);
                }
            }
        }

        private class MockTmdbClientService : TmdbClientService
        {
            public TvGroupCollection? MockGroupCollection { get; set; }
            public bool GetTvEpisodeGroupsCalled { get; private set; }
            public bool GetTvEpisodeCalled { get; private set; }

            public MockTmdbClientService(IMemoryCache cache) : base(cache) { }

            public override Task<TvGroupCollection?> GetTvEpisodeGroupsAsync(int tvShowId, string groupId, string? language, CancellationToken cancellationToken)
            {
                GetTvEpisodeGroupsCalled = true;
                return Task.FromResult(MockGroupCollection);
            }

            public override Task<TvEpisode?> GetTvEpisodeAsync(int tvShowId, int seasonNumber, int episodeNumber, string? language, string? imageLanguages, string? countryCode, CancellationToken cancellationToken)
            {
                GetTvEpisodeCalled = true;
                if (seasonNumber <= 0 || episodeNumber <= 0)
                {
                    return Task.FromResult<TvEpisode?>(null);
                }

                return Task.FromResult<TvEpisode?>(new TvEpisode
                {
                    Name = $"S{seasonNumber:00}E{episodeNumber:00}",
                    Overview = "Test Overview",
                    AirDate = new DateTime(2009, 4, 5)
                });
            }
        }

        private class ThrowingTmdbClientService : TmdbClientService
        {
            public ThrowingTmdbClientService() : base(new TestMemoryCache()) { }

            public override Task<TvGroupCollection?> GetTvEpisodeGroupsAsync(int tvShowId, string groupId, string? language, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("TmdbClientService.GetTvEpisodeGroupsAsync was unexpectedly called.");
            }

            public override Task<TvEpisode?> GetTvEpisodeAsync(int tvShowId, int seasonNumber, int episodeNumber, string? language, string? imageLanguages, string? countryCode, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("TmdbClientService.GetTvEpisodeAsync was unexpectedly called.");
            }
        }

        private class ThrowingExactOrderResolver : TmdbExactOrderResolver
        {
            public ThrowingExactOrderResolver() : base(new ThrowingTmdbClientService()) { }

            public override Task<(int SeasonNumber, int EpisodeNumber)> ResolveCoordinatesAsync(int seriesTmdbId, int customSeasonNumber, int customEpisodeNumber, ShowOrderReference orderRef, string? language, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("TmdbExactOrderResolver.ResolveCoordinatesAsync was unexpectedly called.");
            }
        }

        public static class TmdbUtils
        {
            public static string? ApiKey { get; set; } = "jellyfin_bundled_utils_key_777";
        }

        private class TestTmdbConfig : BasePluginConfiguration
        {
            public string TmdbApiKey { get; set; } = "jellyfin_tmdb_key_123";
        }

        private class TestTmdbPlugin : IPlugin, IHasPluginConfiguration
        {
            public string Name => "TheMovieDb";
            public string Description => "TMDB Provider";
            public Guid Id => Guid.Parse("f6a9c636-f00e-436b-9c29-450f3815049c");
            public Version Version => new Version(1, 0, 0);
            public string AssemblyFilePath => "";
            public bool CanUninstall => false;
            public string DataFolderPath => "";

            public Type ConfigurationType => typeof(TestTmdbConfig);
            public BasePluginConfiguration Configuration { get; set; } = new TestTmdbConfig();

            public PluginInfo GetPluginInfo() => new PluginInfo(Name, Version, Description, Id, CanUninstall);
            public void OnUninstalling() { }
            public void UpdateConfiguration(BasePluginConfiguration configuration) { Configuration = configuration; }
        }

        private class TestPluginManager : IPluginManager
        {
            public IReadOnlyList<LocalPlugin> Plugins { get; }

            public TestPluginManager(IPlugin plugin)
            {
                var localPlugin = (LocalPlugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(LocalPlugin));
                typeof(LocalPlugin).GetProperty(nameof(LocalPlugin.Instance))?.SetValue(localPlugin, plugin);
                Plugins = new[] { localPlugin };
            }

            public void CreatePlugins() { }
            public IEnumerable<Assembly> LoadAssemblies() => Array.Empty<Assembly>();
            public void RegisterServices(Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection) { }
            public bool SaveManifest(PluginManifest manifest, string path) => true;
            public Task<bool> PopulateManifest(PackageInfo package, Version version, string targetPath, PluginStatus status) => Task.FromResult(true);
            public void ImportPluginFrom(string path) { }
            public void FailPlugin(Assembly assembly) { }
            public void DisablePlugin(LocalPlugin plugin) { }
            public void EnablePlugin(LocalPlugin plugin) { }
            public LocalPlugin? GetPlugin(Guid id, Version? version = null) => null;
            public bool RemovePlugin(LocalPlugin plugin) => true;
        }

        [Fact]
        public void ShowOrderReference_TryParse_ValidValues()
        {
            Assert.True(ShowOrderReference.TryParse("tmdb:648fc7202f8d0900e3864f62", out var result));
            Assert.Equal("tmdb", result.Provider);
            Assert.Equal("648fc7202f8d0900e3864f62", result.OrderId);

            Assert.True(ShowOrderReference.TryParse(" tvdb : group-abc-123 ", out var result2));
            Assert.Equal("tvdb", result2.Provider);
            Assert.Equal("group-abc-123", result2.OrderId);
        }

        [Fact]
        public void ShowOrderReference_TryParse_InvalidValues()
        {
            Assert.False(ShowOrderReference.TryParse(null, out _));
            Assert.False(ShowOrderReference.TryParse("", out _));
            Assert.False(ShowOrderReference.TryParse("   ", out _));
            Assert.False(ShowOrderReference.TryParse("tmdb", out _));
            Assert.False(ShowOrderReference.TryParse("tmdb:", out _));
            Assert.False(ShowOrderReference.TryParse(":123", out _));
        }

        [Fact]
        public async Task TmdbExactOrderResolver_DbzKaiBoundaryTests()
        {
            var groupSizes = new[] { 18, 36, 29, 15, 24, 18, 27 };
            var groups = new List<TvGroup>();
            var absoluteEpisodeCounter = 1;

            for (int i = 0; i < groupSizes.Length; i++)
            {
                var groupEpisodes = new List<TvGroupEpisode>();
                var size = groupSizes[i];
                var seasonNum = i + 1;

                for (int e = 0; e < size; e++)
                {
                    groupEpisodes.Add(new TvGroupEpisode
                    {
                        Order = e,
                        SeasonNumber = 1,
                        EpisodeNumber = absoluteEpisodeCounter++
                    });
                }

                groups.Add(new TvGroup
                {
                    Id = $"group-id-{seasonNum}",
                    Name = $"Saga {seasonNum}",
                    Order = seasonNum,
                    Episodes = groupEpisodes
                });
            }

            var tvGroupCollection = new TvGroupCollection
            {
                Id = "648fc7202f8d0900e3864f62",
                Name = "Saga Order",
                Groups = groups
            };

            var cache = new TestMemoryCache();
            var service = new MockTmdbClientService(cache)
            {
                MockGroupCollection = tvGroupCollection
            };
            var resolver = new TmdbExactOrderResolver(service);
            var orderRef = new ShowOrderReference("tmdb", "648fc7202f8d0900e3864f62");

            var (s1, ep1) = await resolver.ResolveCoordinatesAsync(61709, 1, 18, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s1);
            Assert.Equal(18, ep1);

            var (s2, ep2) = await resolver.ResolveCoordinatesAsync(61709, 2, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s2);
            Assert.Equal(19, ep2);

            var (s3, ep3) = await resolver.ResolveCoordinatesAsync(61709, 2, 36, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s3);
            Assert.Equal(54, ep3);

            var (s4, ep4) = await resolver.ResolveCoordinatesAsync(61709, 3, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s4);
            Assert.Equal(55, ep4);

            var (s5, ep5) = await resolver.ResolveCoordinatesAsync(61709, 3, 29, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s5);
            Assert.Equal(83, ep5);

            var (s6, ep6) = await resolver.ResolveCoordinatesAsync(61709, 4, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s6);
            Assert.Equal(84, ep6);
        }

        [Fact]
        public async Task EpisodeProvider_OptInFallbackTest_MissingShowOrganizerId()
        {
            var provider = new ShowOrganizerEpisodeProvider(
                new ThrowingTmdbClientService(),
                new ThrowingExactOrderResolver(),
                null!,
                NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo
            {
                ParentIndexNumber = 1,
                IndexNumber = 1,
                SeriesDisplayOrder = "original",
                MetadataLanguage = "en",
                MetadataCountryCode = "US"
            };

            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
        }

        [Fact]
        public async Task SeasonProvider_OptInFallbackTest_MissingShowOrganizerId()
        {
            var provider = new ShowOrganizerSeasonProvider(
                new ThrowingTmdbClientService(),
                null!,
                NullLogger<ShowOrganizerSeasonProvider>.Instance);

            var info = new SeasonInfo
            {
                IndexNumber = 1,
                MetadataLanguage = "en",
                MetadataCountryCode = "US"
            };

            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
        }

        [Fact]
        public void TmdbClientService_CredentialFallback_ExplicitOverride()
        {
            var cache = new TestMemoryCache();
            var service = new TmdbClientService(cache, null, NullLogger<TmdbClientService>.Instance);

            var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            var config = new PluginConfiguration { TmdbApiKey = "override_key_999" };
            typeof(Plugin).GetProperty(nameof(Plugin.Configuration))?.SetValue(plugin, config);
            typeof(Plugin).GetProperty(nameof(Plugin.Instance))?.SetValue(null, plugin);

            try
            {
                var key = service.ResolveTmdbApiKey();
                Assert.Equal("override_key_999", key);
            }
            finally
            {
                typeof(Plugin).GetProperty(nameof(Plugin.Instance))?.SetValue(null, null);
            }
        }

        [Fact]
        public void TmdbClientService_CredentialFallback_JellyfinPluginManager()
        {
            var cache = new TestMemoryCache();
            var plugin = new TestTmdbPlugin();
            var pm = new TestPluginManager(plugin);

            var service = new TmdbClientService(cache, pm, NullLogger<TmdbClientService>.Instance);
            var key = service.ResolveTmdbApiKey();

            Assert.Equal("jellyfin_tmdb_key_123", key);
        }

        [Fact]
        public void TmdbClientService_CredentialFallback_RealJellyfinState_TmdbUtilsApiKeyAvailable()
        {
            var cache = new TestMemoryCache();
            // ShowOrganizer key is empty
            // Jellyfin PluginConfiguration.TmdbApiKey is empty
            var emptyPlugin = new TestTmdbPlugin();
            ((TestTmdbConfig)emptyPlugin.Configuration).TmdbApiKey = string.Empty;
            var pm = new TestPluginManager(emptyPlugin);

            var service = new TmdbClientService(cache, pm, NullLogger<TmdbClientService>.Instance);
            var key = service.ResolveTmdbApiKey();

            // Should resolve TmdbUtils.ApiKey from loaded assembly
            Assert.Equal("jellyfin_bundled_utils_key_777", key);
        }

        [Fact]
        public void TmdbClientService_CredentialFallback_NoKeyAvailable_WarningAndGracefulFailure()
        {
            var cache = new TestMemoryCache();
            var service = new TmdbClientService(cache, null, NullLogger<TmdbClientService>.Instance);

            var oldKey = TmdbUtils.ApiKey;
            TmdbUtils.ApiKey = null;

            try
            {
                var key = service.ResolveTmdbApiKey();
                Assert.Null(key);
            }
            finally
            {
                TmdbUtils.ApiKey = oldKey;
            }
        }

        [Fact]
        public async Task ShowOrganizerSeasonProvider_AppliesGroupSeasonNames()
        {
            var cache = new TestMemoryCache();
            var groups = new List<TvGroup>
            {
                new TvGroup { Order = 1, Name = "Saiyan Saga" },
                new TvGroup { Order = 2, Name = "Namek Saga" }
            };

            var collection = new TvGroupCollection
            {
                Id = "69681f95c0c672f8f05b21b4",
                Name = "Dragon Ball Recut",
                Groups = groups
            };

            var mockService = new MockTmdbClientService(cache)
            {
                MockGroupCollection = collection
            };

            var provider = new ShowOrganizerSeasonProvider(mockService, null!, NullLogger<ShowOrganizerSeasonProvider>.Instance);

            var info = new SeasonInfo
            {
                IndexNumber = 1,
                MetadataLanguage = "en"
            };
            info.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.Equal("Saiyan Saga", result.Item.Name);
            Assert.Equal(1, result.Item.IndexNumber);
        }

        [Fact]
        public async Task ShowOrganizerEpisodeProvider_FailedGroupRetrieval_ReturnsNoMetadataWithoutFallback()
        {
            var cache = new TestMemoryCache();
            var mockService = new MockTmdbClientService(cache)
            {
                MockGroupCollection = null // Group retrieval fails
            };

            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo
            {
                ParentIndexNumber = 1,
                IndexNumber = 1,
                MetadataLanguage = "en"
            };
            info.SeriesProviderIds["ShowOrganizer"] = "tmdb:invalid_group_id";
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";

            var defaultResult = new MetadataResult<Episode>();
            Assert.False(defaultResult.HasMetadata);

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.False(result.HasMetadata);
        }
    }
}
