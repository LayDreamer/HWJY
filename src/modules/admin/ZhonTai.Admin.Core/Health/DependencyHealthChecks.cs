using FreeRedis;
using FreeSql;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Tools.Cache;

namespace ZhonTai.Admin.Core.Health;

public static class DependencyHealthChecks
{
    public static IServiceCollection AddDependencyHealthChecks(this IServiceCollection services, DbConfig database, CacheConfig cache)
    {
        var builder = services.AddHealthChecks();
        var databases = new[] { database }.Concat(database.Dbs ?? []);
        foreach (var db in databases)
        {
            var key = db.Key;
            builder.Add(new HealthCheckRegistration("database-" + key,
                provider => new DatabaseReadinessCheck(provider, key), HealthStatus.Unhealthy, ["ready"], TimeSpan.FromSeconds(5)));
        }
        if (cache.Type == CacheType.Redis)
            builder.Add(new HealthCheckRegistration("redis", provider => new RedisReadinessCheck(provider),
                HealthStatus.Unhealthy, ["ready"], TimeSpan.FromSeconds(5)));
        return services;
    }

    public static void MapDependencyHealthChecks(this WebApplication app, HealthChecksConfig config)
    {
        if (!config.Enable) return;
        if (string.Equals(config.LivenessPath, config.Path, StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.LivenessPath, config.ReadinessPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Liveness and readiness paths must be different.");
        foreach (var path in new[] { config.Path, config.ReadinessPath }.Distinct(StringComparer.OrdinalIgnoreCase))
            app.MapHealthChecks(path, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready"),
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });
        app.MapHealthChecks(config.LivenessPath, new HealthCheckOptions { Predicate = _ => false });
    }
}

public sealed class DatabaseReadinessCheck(IServiceProvider services, string key) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = services.GetRequiredService<FreeSqlCloud>().Use(key);
            var sql = db.Ado.DataType == DataType.Oracle ? "SELECT 1 FROM DUAL" : "SELECT 1";
            await db.Ado.ExecuteScalarAsync(sql, cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            // Do not expose provider exceptions: they can contain connection strings.
            return HealthCheckResult.Unhealthy("Database connection is unavailable.");
        }
    }
}

public sealed class RedisReadinessCheck(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await services.GetRequiredService<RedisClient>().CallAsync(new CommandPacket("PING", null)).WaitAsync(cancellationToken);
            return string.Equals(Convert.ToString(result), "PONG", StringComparison.Ordinal)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Redis is unavailable.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy("Redis connection is unavailable.");
        }
    }
}
