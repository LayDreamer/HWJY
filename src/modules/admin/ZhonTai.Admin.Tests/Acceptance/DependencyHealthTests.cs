using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FreeRedis;
using FreeSql;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Health;
using ZhonTai.Admin.Tools.Cache;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class DependencyHealthTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadinessQueriesMainAndLogDatabaseAndKeepsLivenessIndependent(bool logAvailable)
    {
        var path = Path.Combine(Path.GetTempPath(), "health-" + Guid.NewGuid().ToString("N") + ".db");
        using var db = new FreeSqlBuilder().UseConnectionString(DataType.Sqlite, "Data Source=" + path + ";Pooling=false").Build();
        var cloud = new FreeSqlCloud();
        cloud.Register("main", () => db);
        cloud.Register("log", () => logAvailable ? db : throw new InvalidOperationException("password=must-not-leak"));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(cloud);
        builder.Services.AddDependencyHealthChecks(new DbConfig { Key = "main", Dbs = [new DbConfig { Key = "log" }] }, new CacheConfig());
        await using var app = builder.Build();
        app.MapDependencyHealthChecks(new HealthChecksConfig());
        await app.StartAsync();
        try
        {
            var client = app.GetTestClient();
            var expectedStatus = logAvailable ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
            var response = await client.GetAsync("/health/ready");
            Assert.Equal(expectedStatus, response.StatusCode);
            var report = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Equal(2, report.Entries.Count);
            Assert.Equal(HealthStatus.Healthy, report.Entries["database-main"].Status);
            Assert.Equal(logAvailable ? HealthStatus.Healthy : HealthStatus.Unhealthy, report.Entries["database-log"].Status);
            Assert.DoesNotContain("must-not-leak", await response.Content.ReadAsStringAsync());
            Assert.Equal(expectedStatus, (await client.GetAsync("/health")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        }
        finally
        {
            await app.StopAsync();
            db.Dispose();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ConfiguredRedisCannotReportHealthyWithoutAConnection()
    {
        using var redis = new RedisClient("127.0.0.1:1,connectTimeout=100,syncTimeout=100");
        using var services = new ServiceCollection().AddSingleton(redis).BuildServiceProvider();
        var result = await new RedisReadinessCheck(services).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
    }
}
