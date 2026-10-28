using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FreeSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;
using ZhonTai.Admin.Core.Consts;
using ZhonTai.Admin.Core.Db;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>Prevent a T4 host fixture from silently falling back to a T2/T3 or production schema.</summary>
[Collection("P0T4MySql")]
[Trait("P0Dependency", "MySql")]
public sealed class P0T4HostIsolationTests
{
    private const string T4Schema = "admincore_p0_t4_acceptance";

    [P0T4MySqlFact]
    [Trait("P0Gate", "Required")]
    public void DB10_NormalT4HostStartsWithAllWritableDatabasesIsolated()
    {
        var connection = P0MySqlAcceptanceEnvironment.RequireConnection("ADMINCORE_T4_MYSQL");
        Assert.False(string.IsNullOrWhiteSpace(connection));
        Assert.Equal(T4Schema, new MySqlConnectionStringBuilder(connection).Database);
        using var config = WriteT4Configs(connection);
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        using (var releaseDb = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connection).UseAutoSyncStructure(false).Build())
        {
            ResetP0ReleaseSchema(releaseDb);
            var executor = new P0MigrationExecutor(releaseDb, Path.GetDirectoryName(manifestPath)!);
            executor.Apply(manifestPath, "p0-t4-host");
        }
        var beforeStart = GetSchemaSignature(connection);
        using var host = new T4HostFactory(config.ConfigCenterPath);
        var cloud = host.Services.GetRequiredService<FreeSqlCloud>();

        // 本用例的职责是"全库隔离"：Host 必须连到 T4 专用 schema，且自动建库/结构同步/Seed 全部关闭时
        // 不得改动任何结构。种子数据由 Seed 流程负责，不在 release baseline 迁移内，因此不校验行数。
        Assert.Equal(T4Schema, cloud.Use(DbKeys.AdminDb).Ado.QuerySingle<string>("select database()"));
        Assert.Equal(T4Schema, cloud.Use(DbKeys.LogDb).Ado.QuerySingle<string>("select database()"));
        Assert.Equal(1, cloud.Use(DbKeys.AdminDb).Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_task'"));
        Assert.Equal(1, cloud.Use(DbKeys.AdminDb).Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));
        Assert.Equal(1, cloud.Use(DbKeys.LogDb).Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_login_log'"));

        // base_user 结构必须完整（列齐全），这是"结构未被破坏"的直接证据。
        Assert.Equal(29, cloud.Use(DbKeys.AdminDb).Ado.QuerySingle<int>(
            "select count(*) from information_schema.columns where table_schema = database() and table_name = 'base_user'"));

        // 启动前后 schema 签名必须完全一致：证明 Host 没有创建或修改任何表。
        Assert.Equal(beforeStart, GetSchemaSignature(connection));
    }

    [P0T4MySqlFact]
    [Trait("P0Gate", "Required")]
    public void DB13_ProductionGuardRejectsMissingPlatformStructureWithoutRepairingIt()
    {
        var connection = RequireT4Connection();
        using var config = WriteT4Configs(connection);
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        using var releaseDb = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connection).UseAutoSyncStructure(false).Build();
        ResetP0ReleaseSchema(releaseDb);
        new P0MigrationExecutor(connection, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-db13");
        var beforeFailure = GetSchemaSignature(connection);
        releaseDb.Ado.ExecuteNonQuery("drop table `base_user`");
        using var host = new T4HostFactory(config.ConfigCenterPath);
        var exception = Assert.ThrowsAny<Exception>(() => _ = host.Services);
        Assert.Contains("required release table base_user is missing", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, releaseDb.Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));
        Assert.NotEqual(beforeFailure, GetSchemaSignature(connection));

        ResetP0ReleaseSchema(releaseDb);
        new P0MigrationExecutor(connection, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-db13-cleanup");
    }

    [P0T4MySqlFact]
    [Trait("P0Gate", "Required")]
    public void DB11_WorkerFailsBeforeStartingPersistentTasksWhenSchemaIsInvalid()
    {
        var connection = RequireT4Connection();
        using var config = WriteT4Configs(connection);
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        using var releaseDb = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connection).UseAutoSyncStructure(false).Build();
        ResetP0ReleaseSchema(releaseDb);
        new P0MigrationExecutor(connection, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-db11");
        releaseDb.Ado.ExecuteNonQuery("drop table `base_user`");
        var hostAssembly = typeof(Program).Assembly.Location;
        var workerStartInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = $"\"{hostAssembly}\" --worker",
            WorkingDirectory = Path.GetDirectoryName(hostAssembly)!,
            UseShellExecute = false
        };
        workerStartInfo.Environment["ADMINCORE_CONFIGCENTER_PATH"] = config.ConfigCenterPath;
        using var worker = Process.Start(workerStartInfo)!;
        if (!worker.WaitForExit(15000))
        {
            worker.Kill(true);
            Assert.Fail("Worker did not fail fast for the invalid release schema.");
        }
        Assert.NotEqual(0, worker.ExitCode);
        Assert.Equal(0, releaseDb.Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));

        ResetP0ReleaseSchema(releaseDb);
        new P0MigrationExecutor(connection, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-db11-cleanup");
    }

    private static void ResetP0ReleaseSchema(IFreeSql db)
    {
        var tables = db.Ado.Query<string>("select table_name from information_schema.tables where table_schema = database() and (table_name like 'base_%' or table_name like 'p0_%')");
        foreach (var table in tables)
        {
            db.Ado.ExecuteNonQuery($"drop table if exists `{table}`");
        }
    }

    private static string GetSchemaSignature(string connection)
    {
        using var db = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connection).UseAutoSyncStructure(false).Build();
        return string.Join("|", db.Ado.Query<string>("select concat(table_name, ':', count(*)) from information_schema.columns where table_schema = database() group by table_name order by table_name"));
    }

    private static T4ConfigScope WriteT4Configs(string connection)
    {
        var scope = T4ConfigScope.Create();
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        var guard = new Dictionary<string, object> { ["enabled"] = true, ["manifestPath"] = manifestPath };
        var logDb = new Dictionary<string, object> { ["key"] = "logdb", ["assemblyNames"] = new[] { "ZhonTai.Admin" }, ["includeEntityDbs"] = new[] { "Log" }, ["excludeEntityDbs"] = Array.Empty<string>(), ["createDb"] = false, ["syncStructure"] = false, ["syncData"] = false, ["type"] = "MySql", ["connectionString"] = connection };
        var db = new Dictionary<string, object> { ["key"] = "admindb", ["assemblyNames"] = new[] { "ZhonTai.Admin" }, ["includeEntityDbs"] = Array.Empty<string>(), ["excludeEntityDbs"] = new[] { "Log" }, ["createDb"] = false, ["syncStructure"] = false, ["syncData"] = false, ["syncUpdateData"] = false, ["generateData"] = false, ["type"] = "MySql", ["connectionString"] = connection, ["dbs"] = new[] { logDb } };
        File.WriteAllText(Path.Combine(scope.ConfigCenterPath, "dbconfig.P0T4HostIsolation.json"), JsonSerializer.Serialize(new Dictionary<string, object> { ["DbConfig"] = db, ["P0SchemaGuard"] = guard }));
        File.WriteAllText(Path.Combine(scope.ConfigCenterPath, "appconfig.P0T4HostIsolation.json"), "{\"AppConfig\":{\"varifyCode\":{\"enable\":false}}}");
        return scope;
    }

    private static string RequireT4Connection()
    {
        var connection = P0MySqlAcceptanceEnvironment.RequireConnection("ADMINCORE_T4_MYSQL");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Set ADMINCORE_T4_MYSQL to the dedicated T4 acceptance schema.");
        Assert.Equal(T4Schema, new MySqlConnectionStringBuilder(connection).Database);
        return connection!;
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, Path.Combine(parts));
            if (Directory.Exists(Path.GetDirectoryName(candidate)!)) return candidate;
        }
        throw new DirectoryNotFoundException("Repository database directory was not found.");
    }

    private sealed class T4HostFactory(string configCenterPath) : WebApplicationFactory<Program>
    {
        private string? _originalConfigCenterPath;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // HostApp 在 ConfigureServices 阶段读取该值；WebApplicationFactory 的回调时序晚于该阶段，
            // 因此必须用进程级环境变量，与 DB11 的 --worker 路径保持一致的注入方式。
            _originalConfigCenterPath = Environment.GetEnvironmentVariable("ADMINCORE_CONFIGCENTER_PATH");
            Environment.SetEnvironmentVariable("ADMINCORE_CONFIGCENTER_PATH", configCenterPath);
            builder.UseEnvironment("P0T4HostIsolation");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("ADMINCORE_CONFIGCENTER_PATH", _originalConfigCenterPath);
        }
    }

    private sealed class T4ConfigScope : IDisposable
    {
        private static readonly string[] FileNames = ["dbconfig.P0T4HostIsolation.json", "appconfig.P0T4HostIsolation.json"];

        private T4ConfigScope(string root)
        {
            ConfigCenterPath = Path.Combine(root, "ConfigCenter");
            Directory.CreateDirectory(ConfigCenterPath);
        }

        public string ConfigCenterPath { get; }

        public static T4ConfigScope Create()
        {
            AssertNoBuildArtifacts();
            return new T4ConfigScope(Path.Combine(Path.GetTempPath(), "admincore-p0-t4", Guid.NewGuid().ToString("N")));
        }

        public void Dispose()
        {
            var root = Directory.GetParent(ConfigCenterPath)!.FullName;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            AssertNoBuildArtifacts();
        }

        private static void AssertNoBuildArtifacts()
        {
            var directories = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "ConfigCenter"),
                Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "ConfigCenter")
            }.Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
            {
                foreach (var fileName in FileNames) Assert.False(File.Exists(Path.Combine(directory, fileName)), $"T4 temporary configuration leaked to {directory}.");
            }
        }
    }
}
