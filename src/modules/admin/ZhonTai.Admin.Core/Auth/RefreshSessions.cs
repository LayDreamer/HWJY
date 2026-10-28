using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeRedis;
using Microsoft.Extensions.Caching.Memory;

namespace ZhonTai.Admin.Core.Auth;

public record RefreshSession(long UserId, string CredentialStamp, DateTimeOffset ExpiresAt);
public record RefreshTicket(string SessionId, string Token, DateTimeOffset ExpiresAt);

public interface IRefreshSessionStore
{
    RefreshTicket Create(RefreshSession session);
    RefreshSession Read(string token);
    RefreshTicket Rotate(string token);
    bool IsActive(string sessionId);
    void Revoke(string token);
    void RevokeUser(long userId);
}

/// <summary>Opaque credentials are never stored in plaintext. A replay revokes the entire session.</summary>
public static class RefreshCredentials
{
    public static readonly TimeSpan ReplayGracePeriod = TimeSpan.FromSeconds(10);
    public static string Stamp(string passwordHash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? "")));
    public static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static bool TryParse(string token, out string id, out string hash)
    {
        id = hash = null;
        if (token == null || token.Length != 97 || token[32] != '.' ||
            !Guid.TryParseExact(token[..32], "N", out _) || !token[33..].All(Uri.IsHexDigit)) return false;
        id = token[..32];
        hash = Stamp(token[33..]);
        return true;
    }
}

public sealed class MemoryRefreshSessionStore(IMemoryCache cache, TimeProvider clock) : IRefreshSessionStore
{
    private readonly object _indexSync = new();
    private readonly Dictionary<long, HashSet<string>> _userSessions = new();

    private sealed class Entry(RefreshSession session, string hash)
    {
        public readonly object Sync = new();
        public readonly RefreshSession Session = session;
        public readonly Dictionary<string, DateTimeOffset> Used = [];
        public string Current = hash;
        public bool Revoked;
    }

    private static string Key(string id) => "auth:refresh:" + id;
    public RefreshTicket Create(RefreshSession session)
    {
        if (session.ExpiresAt <= clock.GetUtcNow()) throw new ArgumentException("Refresh session must expire in the future.");
        var id = Guid.NewGuid().ToString("N");
        var secret = RefreshCredentials.Secret();
        // Publication, user-wide revocation and eviction cleanup share one index lock.
        // Otherwise a revoked or evicted index can leave a live session unreachable.
        lock (_indexSync)
        {
            cache.Set(Key(id), new Entry(session, RefreshCredentials.Stamp(secret)), new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = session.ExpiresAt
            }.RegisterPostEvictionCallback((_, _, _, state) =>
            {
                var item = ((MemoryRefreshSessionStore Store, long UserId, string Id))state;
                item.Store.RemoveIndex(item.UserId, item.Id);
            }, (this, session.UserId, id)));
            if (!_userSessions.TryGetValue(session.UserId, out var sessions))
                _userSessions[session.UserId] = sessions = new();
            sessions.Add(id);
        }
        return new(id, id + "." + secret, session.ExpiresAt);
    }

    private T Access<T>(string token, Func<Entry, string, T> action, bool revoke = false)
    {
        if (!RefreshCredentials.TryParse(token, out var id, out var hash) || !cache.TryGetValue<Entry>(Key(id), out var entry)) return default;
        lock (entry.Sync)
        {
            if (entry.Revoked || entry.Session.ExpiresAt <= clock.GetUtcNow()) return default;
            if (entry.Used.TryGetValue(hash, out var usedAt))
            {
                // A known rotated credential may always revoke its own session,
                // including while a concurrent refresh is still returning to the client.
                if (revoke || clock.GetUtcNow() - usedAt > RefreshCredentials.ReplayGracePeriod)
                {
                    entry.Revoked = true;
                    cache.Remove(Key(id));
                    RemoveIndex(entry.Session.UserId, id);
                }
                return default;
            }
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(entry.Current), Convert.FromHexString(hash))) return default;
            return action(entry, id);
        }
    }

    public RefreshSession Read(string token) => Access(token, (entry, _) => entry.Session);
    public RefreshTicket Rotate(string token) => Access(token, (entry, id) =>
    {
        var secret = RefreshCredentials.Secret();
        entry.Used[entry.Current] = clock.GetUtcNow();
        entry.Current = RefreshCredentials.Stamp(secret);
        return new RefreshTicket(id, id + "." + secret, entry.Session.ExpiresAt);
    });
    public bool IsActive(string sessionId) => sessionId != null && cache.TryGetValue<Entry>(Key(sessionId), out var entry)
        && !entry.Revoked && entry.Session.ExpiresAt > clock.GetUtcNow();
    public void Revoke(string token) => Access<object>(token, (entry, id) =>
    {
        entry.Revoked = true;
        cache.Remove(Key(id));
        RemoveIndex(entry.Session.UserId, id);
        return null;
    }, revoke: true);
    public void RevokeUser(long userId)
    {
        lock (_indexSync)
        {
            if (!_userSessions.Remove(userId, out var sessions)) return;
            foreach (var id in sessions) cache.Remove(Key(id));
        }
    }
    private void RemoveIndex(long userId, string id)
    {
        lock (_indexSync)
        {
            if (!_userSessions.TryGetValue(userId, out var sessions)) return;
            sessions.Remove(id);
            if (sessions.Count == 0) _userSessions.Remove(userId);
        }
    }
}

public sealed class RedisRefreshSessionStore(IRedisClient redis, string prefix, TimeProvider clock) : IRefreshSessionStore
{
    private const string AccessScript = """
        local current = redis.call('HGET', KEYS[1], 'current')
        if not current then return '' end
        local usedAt = redis.call('HGET', KEYS[1], 'used:' .. ARGV[1])
        if usedAt then
          local redisTime = redis.call('TIME')
          local now = redisTime[1] * 1000 + math.floor(redisTime[2] / 1000)
          if ARGV[2] == 'revoke' or now - tonumber(usedAt) > tonumber(ARGV[4]) then
            local userkey = redis.call('HGET', KEYS[1], 'userkey')
            if userkey then redis.call('SREM', userkey, KEYS[1]) end
            redis.call('DEL', KEYS[1])
          end
          return ''
        end
        if current ~= ARGV[1] then return '' end
        local data = redis.call('HGET', KEYS[1], 'data')
        if ARGV[2] == 'rotate' then
          local redisTime = redis.call('TIME')
          local now = redisTime[1] * 1000 + math.floor(redisTime[2] / 1000)
          redis.call('HSET', KEYS[1], 'used:' .. current, now, 'current', ARGV[3])
        elseif ARGV[2] == 'revoke' then
          local userkey = redis.call('HGET', KEYS[1], 'userkey')
          if userkey then redis.call('SREM', userkey, KEYS[1]) end
          redis.call('DEL', KEYS[1])
        end
        return data
        """;
    private string Key(string id) => prefix + ":refresh:{" + id + "}";
    private string UserKey(long userId) => prefix + ":refresh-user:{" + userId + "}";
    public RefreshTicket Create(RefreshSession session)
    {
        var ttl = (long)(session.ExpiresAt - clock.GetUtcNow()).TotalMilliseconds;
        if (ttl <= 0) throw new ArgumentException("Refresh session must expire in the future.");
        var id = Guid.NewGuid().ToString("N");
        var secret = RefreshCredentials.Secret();
        var userKey = UserKey(session.UserId);
        redis.Eval("redis.call('HSET',KEYS[1],'current',ARGV[1],'data',ARGV[2],'userkey',KEYS[2]); redis.call('PEXPIRE',KEYS[1],ARGV[3]); redis.call('SADD',KEYS[2],KEYS[1]); redis.call('PEXPIRE',KEYS[2],ARGV[3]); return 1",
            [Key(id), userKey], RefreshCredentials.Stamp(secret), JsonSerializer.Serialize(session), ttl);
        return new(id, id + "." + secret, session.ExpiresAt);
    }
    private RefreshSession Access(string token, string operation, string nextHash = "")
    {
        if (!RefreshCredentials.TryParse(token, out var id, out var hash)) return null;
        var json = Convert.ToString(redis.Eval(
            AccessScript,
            [Key(id)],
            hash,
            operation,
            nextHash,
            (long)RefreshCredentials.ReplayGracePeriod.TotalMilliseconds));
        if (string.IsNullOrEmpty(json)) return null;
        var session = JsonSerializer.Deserialize<RefreshSession>(json);
        return session.ExpiresAt > clock.GetUtcNow() ? session : null;
    }
    public RefreshSession Read(string token) => Access(token, "read");
    public bool IsActive(string sessionId) => sessionId != null && redis.Exists(Key(sessionId));
    public RefreshTicket Rotate(string token)
    {
        var secret = RefreshCredentials.Secret();
        var session = Access(token, "rotate", RefreshCredentials.Stamp(secret));
        return session == null ? null : new(token[..32], token[..32] + "." + secret, session.ExpiresAt);
    }
    public void Revoke(string token) => Access(token, "revoke");
    public void RevokeUser(long userId) => redis.Eval("local members=redis.call('SMEMBERS',KEYS[1]); for _,key in ipairs(members) do redis.call('DEL',key) end; redis.call('DEL',KEYS[1]); return #members", [UserKey(userId)]);
}
