using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Configuration;
using Jellyfin.Plugin.ShowOrganizer.ExternalIds;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using TMDbLib.Client;
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
            public TvEpisode? MockEpisode { get; set; }
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

                if (MockEpisode != null)
                {
                    return Task.FromResult<TvEpisode?>(MockEpisode);
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
        public void ShowOrderReference_RawAndLegacyPrefix_ResolveToIdenticalTmdbGroupReference()
        {
            Assert.True(ShowOrderReference.TryParse("648fc7202f8d0900e3864f62", out var rawRef));
            Assert.True(ShowOrderReference.TryParse("tmdb:648fc7202f8d0900e3864f62", out var legacyRef));

            Assert.Equal(rawRef.Provider, legacyRef.Provider);
            Assert.Equal(rawRef.OrderId, legacyRef.OrderId);
            Assert.Equal("tmdb", rawRef.Provider);
            Assert.Equal("648fc7202f8d0900e3864f62", rawRef.OrderId);
        }

        [Fact]
        public void ShowOrderReference_TryParse_ValidValues()
        {
            Assert.True(ShowOrderReference.TryParse("648fc7202f8d0900e3864f62", out var resultRaw));
            Assert.Equal("tmdb", resultRaw.Provider);
            Assert.Equal("648fc7202f8d0900e3864f62", resultRaw.OrderId);

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
                    Order = seasonNum, // 1-based group order
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

        private class ControllableTmdbClientService : TmdbClientService
        {
            private TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
            public int ExecuteCount { get; private set; }
            public bool ShouldFail { get; set; }
            public bool AutoRelease { get; set; }

            public ControllableTmdbClientService(IMemoryCache cache) : base(cache) { }

            public TMDbClient? GetTestClient() => GetClient();

            public void ReleaseConfig()
            {
                _tcs.TrySetResult(true);
            }

            protected override async Task ExecuteGetConfigAsync(TMDbClient client)
            {
                ExecuteCount++;
                if (ShouldFail)
                {
                    throw new InvalidOperationException("Simulated network failure");
                }

                if (!AutoRelease)
                {
                    await _tcs.Task;
                }

                client.SetConfig(new TMDbLib.Objects.General.TMDbConfig
                {
                    Images = new TMDbLib.Objects.General.ConfigImageTypes
                    {
                        SecureBaseUrl = "https://image.tmdb.org/t/p/",
                        BaseUrl = "http://image.tmdb.org/t/p/"
                    }
                });
            }
        }

        [Fact]
        public async Task TmdbClientService_EnsureClientConfigAsync_InFlightCallersBlockAndShareSingleTask()
        {
            var cache = new TestMemoryCache();
            var service = new ControllableTmdbClientService(cache);

            // Start Caller A
            var taskA = service.EnsureClientConfigAsync();

            // Verify GetConfigAsync has started and is currently in flight
            Assert.Equal(1, service.ExecuteCount);
            Assert.False(taskA.IsCompleted);

            // Start Callers B and C while A is still in flight
            var taskB = service.EnsureClientConfigAsync();
            var taskC = service.GetImageUrlAsync("w500", "/poster.jpg");

            // Verify Callers B and C have NOT completed early while config remains blocked
            Assert.False(taskB.IsCompleted);
            Assert.False(taskC.IsCompleted);

            // Verify GetConfigAsync was called exactly ONCE so far
            Assert.Equal(1, service.ExecuteCount);

            // Release the in-flight configuration fetch
            service.ReleaseConfig();

            // Await all tasks
            await Task.WhenAll(taskA, taskB, taskC);

            // Verify all callers completed successfully
            Assert.True(taskA.IsCompletedSuccessfully);
            Assert.True(taskB.IsCompletedSuccessfully);
            Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", await taskC);

            // Verify GetConfigAsync was executed exactly ONCE total
            Assert.Equal(1, service.ExecuteCount);
        }

        [Fact]
        public async Task TmdbClientService_EnsureClientConfigAsync_RetriesOnTransientFailure()
        {
            var cache = new TestMemoryCache();
            var service = new ControllableTmdbClientService(cache)
            {
                ShouldFail = true,
                AutoRelease = true
            };

            // Attempt 1: Fails
            await service.EnsureClientConfigAsync();
            Assert.Equal(1, service.ExecuteCount);

            // Configure attempt 2 to succeed
            service.ShouldFail = false;

            var client = service.GetTestClient();
            Assert.False(client?.HasConfig ?? false);

            // Attempt 2: Retries and succeeds
            await service.EnsureClientConfigAsync();
            Assert.Equal(2, service.ExecuteCount);

            // Attempt 3: Should skip because config is now initialized (HasConfig == true)
            await service.EnsureClientConfigAsync();
            Assert.Equal(2, service.ExecuteCount);
        }

        [Fact]
        public async Task EpisodeProvider_ConfigNotInitialized_CompletesWithoutInvalidOperationException()
        {
            var cache = new TestMemoryCache();
            var mockService = new MockTmdbClientService(cache)
            {
                MockGroupCollection = new TvGroupCollection
                {
                    Id = "69681f95c0c672f8f05b21b4",
                    Name = "Dragon Ball Recut (Sagas)",
                    Groups = new List<TvGroup>
                    {
                        new TvGroup
                        {
                            Order = 1,
                            Episodes = new List<TvGroupEpisode>
                            {
                                new TvGroupEpisode { Order = 0, SeasonNumber = 1, EpisodeNumber = 1 }
                            }
                        }
                    }
                }
            };

            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo
            {
                ParentIndexNumber = 1,
                IndexNumber = 1,
                MetadataLanguage = "en"
            };
            info.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";

            var result = await provider.GetMetadata(info, CancellationToken.None);

            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
            Assert.Equal("S01E01", result.Item.Name);
            Assert.Equal(1, result.Item.ParentIndexNumber);
            Assert.Equal(1, result.Item.IndexNumber);
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
        public async Task SeasonMapping_1BasedOrder_Season1ToOrder1_Season2ToOrder2_Season9ToOrder9()
        {
            var cache = new TestMemoryCache();
            var sagas = new[]
            {
                "Emperor Pilaf Saga", "Tournament Saga", "Red Ribbon Army Saga",
                "General Blue Saga", "Commander Red Saga", "Fortuneteller Baba Saga",
                "Tien Shinhan Saga", "King Piccolo Saga", "Piccolo Jr. Saga"
            };

            var groups = sagas.Select((sagaName, idx) => new TvGroup
            {
                Order = idx + 1, // 1-based TMDb group order
                Name = sagaName,
                Episodes = new List<TvGroupEpisode>
                {
                    new TvGroupEpisode { Order = 0, SeasonNumber = idx + 1, EpisodeNumber = 101 }
                }
            }).ToList();

            var collection = new TvGroupCollection
            {
                Id = "69681f95c0c672f8f05b21b4",
                Name = "Dragon Ball Recut (Sagas)",
                Groups = groups
            };

            var mockService = new MockTmdbClientService(cache)
            {
                MockGroupCollection = collection
            };

            var resolver = new TmdbExactOrderResolver(mockService);
            var orderRef = new ShowOrderReference("tmdb", "69681f95c0c672f8f05b21b4");

            // Season 1 -> Group Order 1
            var s1 = await resolver.ResolveCoordinatesAsync(12609, 1, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal((1, 101), s1);

            // Season 2 -> Group Order 2
            var s2 = await resolver.ResolveCoordinatesAsync(12609, 2, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal((2, 101), s2);

            // Season 9 -> Group Order 9
            var s9 = await resolver.ResolveCoordinatesAsync(12609, 9, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal((9, 101), s9);

            // Invalid season numbers (<= 0) fail gracefully
            var s0 = await resolver.ResolveCoordinatesAsync(12609, 0, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal((-1, -1), s0);

            var sNeg = await resolver.ResolveCoordinatesAsync(12609, -1, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal((-1, -1), sNeg);
        }

        [Fact]
        public async Task SeasonMapping_RealGroupOrder_Season1ToOrder1_Season9ToOrder9()
        {
            var cache = new TestMemoryCache();
            var sagas = new[]
            {
                "Emperor Pilaf Saga", "Tournament Saga", "Red Ribbon Army Saga",
                "General Blue Saga", "Commander Red Saga", "Fortune Teller Baba Saga",
                "Tien Shinhan Saga", "King Piccolo Saga", "Piccolo Jr. Saga"
            };

            var groups = sagas.Select((sagaName, idx) => new TvGroup
            {
                Order = idx + 1, // TMDb 1-based group order (Order 1..9)
                Name = sagaName
            }).ToList();

            var collection = new TvGroupCollection
            {
                Id = "69681f95c0c672f8f05b21b4",
                Name = "Dragon Ball Recut (Sagas)",
                Groups = groups
            };

            var mockService = new MockTmdbClientService(cache)
            {
                MockGroupCollection = collection
            };

            var provider = new ShowOrganizerSeasonProvider(mockService, null!, NullLogger<ShowOrganizerSeasonProvider>.Instance);

            for (int seasonNumber = 1; seasonNumber <= 9; seasonNumber++)
            {
                var info = new SeasonInfo
                {
                    IndexNumber = seasonNumber,
                    MetadataLanguage = "en"
                };
                info.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
                info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";

                var result = await provider.GetMetadata(info, CancellationToken.None);

                Assert.True(result.HasMetadata);
                Assert.Equal(seasonNumber, result.Item.IndexNumber);
                Assert.Equal(sagas[seasonNumber - 1], result.Item.Name);
            }
        }

        [Fact]
        public async Task EpisodeResolution_BoundaryTests_PreservesCustomJellyfinNumbering()
        {
            var cache = new TestMemoryCache();
            var groups = new List<TvGroup>();
            for (int g = 1; g <= 9; g++)
            {
                groups.Add(new TvGroup
                {
                    Order = g, // 1-based TMDb group order (Group 1..9)
                    Name = $"Saga {g}",
                    Episodes = new List<TvGroupEpisode>
                    {
                        new TvGroupEpisode { Order = 0, SeasonNumber = g, EpisodeNumber = 101 }, // First ep
                        new TvGroupEpisode { Order = 1, SeasonNumber = g, EpisodeNumber = 102 }, // Mid ep
                        new TvGroupEpisode { Order = 2, SeasonNumber = g, EpisodeNumber = 103 }  // Last ep
                    }
                });
            }

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

            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            // Season 1 First Episode (S01E01) -> Group Order 1, Episode Order 0
            var infoS01First = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };
            infoS01First.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            infoS01First.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";
            var resS01First = await provider.GetMetadata(infoS01First, CancellationToken.None);
            Assert.True(resS01First.HasMetadata);
            Assert.Equal(1, resS01First.Item.ParentIndexNumber);
            Assert.Equal(1, resS01First.Item.IndexNumber);

            // Season 1 Last Episode (S01E03) -> Group Order 1, Episode Order 2
            var infoS01Last = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 3, MetadataLanguage = "en" };
            infoS01Last.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            infoS01Last.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";
            var resS01Last = await provider.GetMetadata(infoS01Last, CancellationToken.None);
            Assert.True(resS01Last.HasMetadata);
            Assert.Equal(1, resS01Last.Item.ParentIndexNumber);
            Assert.Equal(3, resS01Last.Item.IndexNumber);

            // Season 2 First Episode (S02E01) -> Group Order 2, Episode Order 0
            var infoS02First = new EpisodeInfo { ParentIndexNumber = 2, IndexNumber = 1, MetadataLanguage = "en" };
            infoS02First.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            infoS02First.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";
            var resS02First = await provider.GetMetadata(infoS02First, CancellationToken.None);
            Assert.True(resS02First.HasMetadata);
            Assert.Equal(2, resS02First.Item.ParentIndexNumber);
            Assert.Equal(1, resS02First.Item.IndexNumber);

            // Season 5 Middle Saga (S05E01) -> Group Order 5, Episode Order 0
            var infoS05 = new EpisodeInfo { ParentIndexNumber = 5, IndexNumber = 1, MetadataLanguage = "en" };
            infoS05.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            infoS05.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";
            var resS05 = await provider.GetMetadata(infoS05, CancellationToken.None);
            Assert.True(resS05.HasMetadata);
            Assert.Equal(5, resS05.Item.ParentIndexNumber);
            Assert.Equal(1, resS05.Item.IndexNumber);

            // Season 9 First Episode (S09E01) -> Group Order 9, Episode Order 0
            var infoS09First = new EpisodeInfo { ParentIndexNumber = 9, IndexNumber = 1, MetadataLanguage = "en" };
            infoS09First.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            infoS09First.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";
            var resS09First = await provider.GetMetadata(infoS09First, CancellationToken.None);
            Assert.True(resS09First.HasMetadata);
            Assert.Equal(9, resS09First.Item.ParentIndexNumber);
            Assert.Equal(1, resS09First.Item.IndexNumber);

            // Season 9 Last Episode (S09E03) -> Group Order 9, Episode Order 2
            var infoS09Last = new EpisodeInfo { ParentIndexNumber = 9, IndexNumber = 3, MetadataLanguage = "en" };
            infoS09Last.SeriesProviderIds["ShowOrganizer"] = "tmdb:69681f95c0c672f8f05b21b4";
            infoS09Last.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "12609";
            var resS09Last = await provider.GetMetadata(infoS09Last, CancellationToken.None);
            Assert.True(resS09Last.HasMetadata);
            Assert.Equal(9, resS09Last.Item.ParentIndexNumber);
            Assert.Equal(3, resS09Last.Item.IndexNumber);
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

        [Fact]
        public void PluginDisposal_ClearsStaticInstanceAndResetsProviderState()
        {
            var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
            typeof(Plugin).GetProperty(nameof(Plugin.Instance))?.SetValue(null, plugin);

            Assert.Same(plugin, Plugin.Instance);

            var disposeMethod = typeof(Plugin).GetMethod("Dispose", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            disposeMethod?.Invoke(plugin, new object[] { true });

            Assert.Null(Plugin.Instance);
        }

        [Fact]
        public void PluginServiceRegistrator_RegistersServicesAsTransient()
        {
            var services = new ServiceCollection();
            var registrator = new PluginServiceRegistrator();
            registrator.RegisterServices(services, null!);

            var tmdbServiceDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(TmdbClientService));
            Assert.NotNull(tmdbServiceDescriptor);
            Assert.Equal(ServiceLifetime.Transient, tmdbServiceDescriptor.Lifetime);

            var resolverDescriptor = services.FirstOrDefault(sd => sd.ServiceType == typeof(TmdbExactOrderResolver));
            Assert.NotNull(resolverDescriptor);
            Assert.Equal(ServiceLifetime.Transient, resolverDescriptor.Lifetime);
        }

        [Fact]
        public void ServiceLifecycle_IDisposableLoggingTest()
        {
            var cache = new TestMemoryCache();
            var service = new TmdbClientService(cache, null, NullLogger<TmdbClientService>.Instance);
            Assert.NotNull(service);
            service.Dispose();

            var resolver = new TmdbExactOrderResolver(service, NullLogger<TmdbExactOrderResolver>.Instance);
            Assert.NotNull(resolver);
            resolver.Dispose();
        }

        [Fact]
        public void BasePlugin_DoesNotImplementIDisposable_PluginImplementsIDisposableDirectly()
        {
            var baseType = typeof(BasePlugin<PluginConfiguration>);
            var isDisposable = typeof(IDisposable).IsAssignableFrom(baseType);
            Assert.False(isDisposable);

            var disposeMethods = baseType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name.Contains("Dispose"))
                .ToList();
            Assert.Empty(disposeMethods);
        }

        [Fact]
        public void ExternalId_ProviderName_Is_TheMovieDb_Show_Group_And_Key_Is_ShowOrganizer()
        {
            var extId = new ShowOrganizerExternalId();
            Assert.Equal("TheMovieDb Show Group", extId.ProviderName);
            Assert.Equal("ShowOrganizer", extId.Key);
            Assert.Equal(ExternalIdMediaType.Series, extId.Type);
        }

        [Fact]
        public async Task ProviderFallback_NeitherIdPresent_DeclinesCleanly()
        {
            var cache = new TestMemoryCache();
            var mockService = new MockTmdbClientService(cache);
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task ProviderFallback_TmdbIdPresent_ShowOrganizerIdAbsent_DeclinesCleanly()
        {
            var cache = new TestMemoryCache();
            var mockService = new MockTmdbClientService(cache);
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task ProviderFallback_ShowOrganizerIdPresent_TmdbIdAbsent_DeclinesCleanly()
        {
            var cache = new TestMemoryCache();
            var mockService = new MockTmdbClientService(cache);
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };
            info.SeriesProviderIds["ShowOrganizer"] = "tmdb:648fc7202f8d0900e3864f62";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
            Assert.Null(result.Item);
        }

        [Fact]
        public async Task ProviderFallback_BothIdsPresentAndValid_PerformsMapping()
        {
            var cache = new TestMemoryCache();
            var groupCollection = new TvGroupCollection
            {
                Id = "648fc7202f8d0900e3864f62",
                Name = "Saga Order",
                Groups = new List<TvGroup>
                {
                    new TvGroup
                    {
                        Order = 1,
                        Name = "Saiyan Saga",
                        Episodes = new List<TvGroupEpisode>
                        {
                            new TvGroupEpisode { Order = 0, SeasonNumber = 1, EpisodeNumber = 1 }
                        }
                    }
                }
            };
            var mockService = new MockTmdbClientService(cache)
            {
                MockGroupCollection = groupCollection,
                MockEpisode = new TvEpisode { Name = "Saiyan Arrival", Overview = "Raditz arrives" }
            };
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };
            info.SeriesProviderIds["ShowOrganizer"] = "tmdb:648fc7202f8d0900e3864f62";
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
            Assert.Equal("Saiyan Arrival", result.Item.Name);
        }

        [Fact]
        public async Task ProviderFallback_MalformedShowOrganizerId_DeclinesCleanly()
        {
            var cache = new TestMemoryCache();
            var mockService = new MockTmdbClientService(cache);
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };
            info.SeriesProviderIds["ShowOrganizer"] = "invalid_no_colon";
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
        }

        [Fact]
        public async Task ProviderFallback_UnsupportedPrefix_DeclinesCleanly()
        {
            var cache = new TestMemoryCache();
            var mockService = new MockTmdbClientService(cache);
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };
            info.SeriesProviderIds["ShowOrganizer"] = "unsupported:12345";
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
        }

        [Fact]
        public async Task ProviderFallback_UnmappableCustomCoordinates_DeclinesCleanly()
        {
            var cache = new TestMemoryCache();
            var groupCollection = new TvGroupCollection
            {
                Id = "648fc7202f8d0900e3864f62",
                Name = "Saga Order",
                Groups = new List<TvGroup>
                {
                    new TvGroup { Order = 1, Name = "Saiyan Saga", Episodes = new List<TvGroupEpisode>() }
                }
            };
            var mockService = new MockTmdbClientService(cache) { MockGroupCollection = groupCollection };
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 99, IndexNumber = 99, MetadataLanguage = "en" };
            info.SeriesProviderIds["ShowOrganizer"] = "tmdb:648fc7202f8d0900e3864f62";
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.False(result.HasMetadata);
        }

        [Fact]
        public async Task ProviderFallback_RawTmdbGroupIdWithoutPrefix_PerformsMapping()
        {
            var cache = new TestMemoryCache();
            var groupCollection = new TvGroupCollection
            {
                Id = "648fc7202f8d0900e3864f62",
                Name = "Saga Order",
                Groups = new List<TvGroup>
                {
                    new TvGroup
                    {
                        Order = 1,
                        Name = "Saiyan Saga",
                        Episodes = new List<TvGroupEpisode>
                        {
                            new TvGroupEpisode { Order = 0, SeasonNumber = 1, EpisodeNumber = 1 }
                        }
                    }
                }
            };
            var mockService = new MockTmdbClientService(cache)
            {
                MockGroupCollection = groupCollection,
                MockEpisode = new TvEpisode { Name = "Saiyan Arrival", Overview = "Raditz arrives" }
            };
            var resolver = new TmdbExactOrderResolver(mockService);
            var provider = new ShowOrganizerEpisodeProvider(mockService, resolver, null!, NullLogger<ShowOrganizerEpisodeProvider>.Instance);

            var info = new EpisodeInfo { ParentIndexNumber = 1, IndexNumber = 1, MetadataLanguage = "en" };
            info.SeriesProviderIds["ShowOrganizer"] = "648fc7202f8d0900e3864f62"; // Raw ID without prefix
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);
            Assert.True(result.HasMetadata);
            Assert.NotNull(result.Item);
            Assert.Equal("Saiyan Arrival", result.Item.Name);
        }

        [Fact]
        public void BuildVersion_AssemblyFileVersion_Matches_BuildYamlVersion()
        {
            var asm = typeof(Plugin).Assembly;
            var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location).FileVersion;
            Assert.NotNull(fileVersion);

            var yamlPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "build.yaml"));
            if (File.Exists(yamlPath))
            {
                var yamlContent = File.ReadAllText(yamlPath);
                var match = System.Text.RegularExpressions.Regex.Match(yamlContent, @"version:\s*[""']?([^""'\r\n]+)[""']?");
                if (match.Success)
                {
                    var expectedVersion = match.Groups[1].Value.Trim();
                    Assert.Equal(expectedVersion, fileVersion);
                }
            }
        }
    }
}
