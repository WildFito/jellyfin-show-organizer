using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ShowOrganizer.Models;
using Jellyfin.Plugin.ShowOrganizer.Providers.Tmdb;
using Jellyfin.Plugin.ShowOrganizer.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
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
                var entry = new TestCacheEntry(key, this);
                return entry;
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
            // Set up Dragon Ball Z Kai mock saga episode groups
            // S01 = 18, S02 = 36, S03 = 29, S04 = 15, S05 = 24, S06 = 18, S07 = 27 (Total: 167)
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
                        Order = e, // 0-indexed order in group
                        SeasonNumber = 1, // Original DBZ Kai season on TMDB is S01
                        EpisodeNumber = absoluteEpisodeCounter++ // Original TMDB episode numbering
                    });
                }

                groups.Add(new TvGroup
                {
                    Id = $"group-id-{seasonNum}",
                    Name = $"Saga {seasonNum}",
                    Order = seasonNum, // 1-indexed group order matching custom season number
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

            // Boundary test mappings
            // S01E18 -> original 18
            var (s1, ep1) = await resolver.ResolveCoordinatesAsync(61709, 1, 18, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s1);
            Assert.Equal(18, ep1);

            // S02E01 -> original 19
            var (s2, ep2) = await resolver.ResolveCoordinatesAsync(61709, 2, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s2);
            Assert.Equal(19, ep2);

            // S02E36 -> original 54
            var (s3, ep3) = await resolver.ResolveCoordinatesAsync(61709, 2, 36, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s3);
            Assert.Equal(54, ep3);

            // S03E01 -> original 55
            var (s4, ep4) = await resolver.ResolveCoordinatesAsync(61709, 3, 1, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s4);
            Assert.Equal(55, ep4);

            // S03E29 -> original 83
            var (s5, ep5) = await resolver.ResolveCoordinatesAsync(61709, 3, 29, orderRef, "en", CancellationToken.None);
            Assert.Equal(1, s5);
            Assert.Equal(83, ep5);

            // S04E01 -> original 84
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

            // Ensure ShowOrganizer ID is NOT present in SeriesProviderIds
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);

            // Should return immediately with HasMetadata = false and without triggering throwing services
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

            // Ensure ShowOrganizer ID is NOT present in SeriesProviderIds
            info.SeriesProviderIds[MetadataProvider.Tmdb.ToString()] = "61709";

            var result = await provider.GetMetadata(info, CancellationToken.None);

            // Should return immediately with HasMetadata = false and without triggering throwing service
            Assert.False(result.HasMetadata);
        }
    }
}
