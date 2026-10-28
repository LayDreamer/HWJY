using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;
using ZhonTai.Admin.Core.Auth;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class RefreshSessionStoreTests
{
    [Fact]
    public void RotationToleratesConcurrentRetryButLaterReplayRevokesSession()
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new MemoryRefreshSessionStore(cache, clock);
        var session = new RefreshSession(42, "credential-stamp", clock.GetUtcNow().AddMinutes(5));
        var first = store.Create(session);

        Assert.Equal(session, store.Read(first.Token));
        var second = store.Rotate(first.Token);
        Assert.NotNull(second);
        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.True(store.IsActive(first.SessionId));

        Assert.Null(store.Read(first.Token));
        Assert.True(store.IsActive(first.SessionId));
        Assert.NotNull(store.Read(second.Token));

        clock.Advance(RefreshCredentials.ReplayGracePeriod.Add(TimeSpan.FromMilliseconds(1)));
        Assert.Null(store.Read(first.Token));
        Assert.False(store.IsActive(first.SessionId));
        Assert.Null(store.Read(second.Token));
    }

    [Fact]
    public void RevokeUserInvalidatesEverySessionForThatUserOnly()
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var store = new MemoryRefreshSessionStore(cache, TimeProvider.System);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        var first = store.Create(new RefreshSession(42, "first", expiry));
        var second = store.Create(new RefreshSession(42, "second", expiry));
        var other = store.Create(new RefreshSession(7, "other", expiry));

        store.RevokeUser(42);

        Assert.False(store.IsActive(first.SessionId));
        Assert.False(store.IsActive(second.SessionId));
        Assert.True(store.IsActive(other.SessionId));
        Assert.Null(store.Read(first.Token));
        Assert.NotNull(store.Read(other.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-refresh-token")]
    public void InvalidCredentialIsRejected(string token)
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var store = new MemoryRefreshSessionStore(cache, TimeProvider.System);

        Assert.Null(store.Read(token));
        Assert.Null(store.Rotate(token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void LogoutWithARotatedCredentialRevokesTheEntireSession(int elapsedSeconds)
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new MemoryRefreshSessionStore(cache, clock);
        var original = store.Create(new RefreshSession(42, "stamp", clock.GetUtcNow().AddMinutes(5)));
        var rotated = store.Rotate(original.Token);
        clock.Advance(TimeSpan.FromSeconds(elapsedSeconds));

        store.Revoke(original.Token);
        store.Revoke(original.Token); // Retrying logout is harmless.

        Assert.False(store.IsActive(original.SessionId));
        Assert.Null(store.Read(rotated.Token));
        Assert.Null(store.Rotate(rotated.Token));
    }

    [Fact]
    public void KnowingASessionIdWithoutItsCredentialCannotRevokeIt()
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var store = new MemoryRefreshSessionStore(cache, TimeProvider.System);
        var original = store.Create(new RefreshSession(42, "stamp", DateTimeOffset.UtcNow.AddMinutes(5)));
        var rotated = store.Rotate(original.Token);

        store.Revoke(original.SessionId + "." + RefreshCredentials.Secret());

        Assert.True(store.IsActive(original.SessionId));
        Assert.NotNull(store.Read(rotated.Token));
    }

    [Fact]
    public async Task RevokeUserCannotMissASessionWhoseCachePublicationIsInProgress()
    {
        using var cache = new BlockingMemoryCache();
        var store = new MemoryRefreshSessionStore(cache, TimeProvider.System);
        var create = Task.Run(() => store.Create(new RefreshSession(42, "stamp", DateTimeOffset.UtcNow.AddMinutes(5))));
        Task revoke = Task.CompletedTask;
        try
        {
            await cache.PublicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var revokeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            revoke = Task.Run(() =>
            {
                revokeStarted.SetResult();
                store.RevokeUser(42);
            });
            await revokeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The old implementation finishes revocation before the entry is published.
            // A synchronized implementation waits until publication completes.
            await Task.WhenAny(revoke, Task.Delay(250));
        }
        finally
        {
            cache.AllowPublication.Set();
        }
        var ticket = await create.WaitAsync(TimeSpan.FromSeconds(5));
        await revoke.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(store.IsActive(ticket.SessionId));
        Assert.Null(store.Read(ticket.Token));
    }

    [Fact]
    public async Task ConcurrentEvictionAndCreationKeepEverySurvivingSessionRevocable()
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var store = new MemoryRefreshSessionStore(cache, TimeProvider.System);
        var survivors = new ConcurrentBag<RefreshTicket>();
        await Task.WhenAll(Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                var ticket = store.Create(new RefreshSession(42, "evicted", DateTimeOffset.UtcNow.AddMinutes(5)));
                store.Revoke(ticket.Token);
            }
        }), Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
                survivors.Add(store.Create(new RefreshSession(42, "survivor", DateTimeOffset.UtcNow.AddMinutes(5))));
        }));

        store.RevokeUser(42);

        Assert.All(survivors, ticket => Assert.False(store.IsActive(ticket.SessionId)));
    }

    [Fact]
    public async Task ConcurrentRotationAndLogoutAlwaysEndWithAnInactiveSession()
    {
        using var cache = new MemoryCache(Options.Create(new MemoryCacheOptions()));
        var store = new MemoryRefreshSessionStore(cache, TimeProvider.System);
        for (var i = 0; i < 50; i++)
        {
            var ticket = store.Create(new RefreshSession(42, "stamp", DateTimeOffset.UtcNow.AddMinutes(5)));
            await Task.WhenAll(Task.Run(() => store.Rotate(ticket.Token)), Task.Run(() => store.Revoke(ticket.Token)));
            Assert.False(store.IsActive(ticket.SessionId));
        }
    }

    // Pause the real MemoryCache entry at publication, without adding hooks to production code.
    private sealed class BlockingMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(Options.Create(new MemoryCacheOptions()));
        public readonly TaskCompletionSource PublicationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim AllowPublication = new();
        public bool TryGetValue(object key, out object value) => _inner.TryGetValue(key, out value);
        public void Remove(object key) => _inner.Remove(key);
        public ICacheEntry CreateEntry(object key) => new BlockingEntry(_inner.CreateEntry(key), this);
        public void Dispose() { _inner.Dispose(); AllowPublication.Dispose(); }

        private sealed class BlockingEntry(ICacheEntry inner, BlockingMemoryCache cache) : ICacheEntry
        {
            public object Key => inner.Key;
            public object Value { get => inner.Value; set => inner.Value = value; }
            public DateTimeOffset? AbsoluteExpiration { get => inner.AbsoluteExpiration; set => inner.AbsoluteExpiration = value; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get => inner.AbsoluteExpirationRelativeToNow; set => inner.AbsoluteExpirationRelativeToNow = value; }
            public TimeSpan? SlidingExpiration { get => inner.SlidingExpiration; set => inner.SlidingExpiration = value; }
            public IList<IChangeToken> ExpirationTokens => inner.ExpirationTokens;
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => inner.PostEvictionCallbacks;
            public CacheItemPriority Priority { get => inner.Priority; set => inner.Priority = value; }
            public long? Size { get => inner.Size; set => inner.Size = value; }
            public void Dispose()
            {
                cache.PublicationStarted.TrySetResult();
                if (!cache.AllowPublication.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Cache publication was not released.");
                inner.Dispose();
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
