using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FreeSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Xunit;
using ZhonTai.Admin.Core.Consts;
using ZhonTai.Admin.Core.Db;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>Prevent a T4 host fixture from silently falling back to a T2/T3 or production schema.</summary>
[Collection("P0T4MySql")]
public sealed class P0T4HostIsolationTests
{
    private const string T4Schema = "admincore_p0_t4_acceptance";

    [Fact]
    [Trait("P0Gate", "Remaining")]
    public void DB10_NormalT4HostStartsWithAllWritableDatabasesIsolated()
    {
        var connection = Environment.GetEnvironmentVariable("ADMINCORE_T4_MYSQL");
        Assert.False(string.IsNullOrWhiteSpace(connection));
        Assert.Equal(T4Schema, new MySqlConnectionStringBuilder(connection).Database);
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        using (var releaseDb = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connection).UseAutoSyncStructure(false).Build())
        {
            ResetP0ReleaseSchema(releaseDb);
            var executor = new P0MigrationExecutor(releaseDb, Path.GetDirectoryName(manifestPath)!);
            executor.Apply(manifestPath, "p0-t4-host");
        }
        var beforeStart = GetSchemaSignature(connection);
        WriteT4Configs(connection);

        using var host = new T4HostFactory();
        var cloud = host.Services.GetRequiredService<FreeSqlCloud>();
        Assert.Equal(T4Schema, cloud.Use(DbKeys.AdminDb).Ado.QuerySingle<string>("select database()"));
        Assert.Equal(T4Schema, cloud.Use(DbKeys.LogDb).Ado.QuerySingle<string>("select database()"));
        Assert.True(cloud.Use(DbKeys.AdminDb).Ado.QuerySingle<int>("select count(*) from base_user") > 0);
        Assert.Equal(1, cloud.Use(DbKeys.AdminDb).Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_task'"));
        Assert.Equal(beforeStart, GetSchemaSignature(connection));
    }

    [Fact]
    [Trait("P0Gate", "Remaining")]
    public void DB13_ProductionGuardRejectsMissingPlatformStructureWithoutRepairingIt()
    {
        var connection = RequireT4Connection();
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        using var releaseDb = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connection).UseAutoSyncStructure(false).Build();
        ResetP0ReleaseSchema(releaseDb);
        new P0MigrationExecutor(connection, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-db13");
        var beforeFailure = GetSchemaSignature(connection);
        releaseDb.Ado.ExecuteNonQuery("drop table `base_user`");
        WriteT4Configs(connection);

        using var host = new T4HostFactory();
        var exception = Assert.ThrowsAny<Exception>(() => _ = host.Services);
        Assert.Contains("required release table base_user is missing", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, releaseDb.Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));
        Assert.NotEqual(beforeFailure, GetSchemaSignature(connection));

        ResetP0ReleaseSchema(releaseDb);
        new P0MigrationExecutor(connection, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-db13-cleanup");
    }

    [Fact]
    [Trait("P0Gate", "Remaining")]
    public void DB11_WorkerFailsBeforeStartingPersistentTasksWhenSchemaIsInvalid()
    {
        var connection = RequireT4Connection();
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        using var releaseDb = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connection).UseAutoSyncStructure(false).Build();
        ResetP0ReleaseSchema(releaseDb);
        new P0MigrationExecutor(connection, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-db11");
        releaseDb.Ado.ExecuteNonQuery("drop table `base_user`");
        WriteT4Configs(connection);

        var hostAssembly = typeof(Program).Assembly.Location;
        using var worker = Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            Arguments = $"\"{hostAssembly}\" --worker",
            WorkingDirectory = Path.GetDirectoryName(hostAssembly)!,
            UseShellExecute = false
        })!;
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

    private static void WriteT4Configs(string connection)
    {
        var directories = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ConfigCenter"),
            Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location)!, "ConfigCenter")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
        var manifestPath = FindRepositoryFile("database", "mysql", "manifest.json");
        var guard = new Dictionary<string, object> { ["enabled"] = true, ["manifestPath"] = manifestPath };
        var logDb = new Dictionary<string, object> { ["key"] = "logdb", ["assemblyNames"] = new[] { "ZhonTai.Admin" }, ["includeEntityDbs"] = new[] { "Log" }, ["excludeEntityDbs"] = Array.Empty<string>(), ["createDb"] = false, ["syncStructure"] = false, ["syncData"] = false, ["type"] = "MySql", ["connectionString"] = connection };
        var db = new Dictionary<string, object> { ["key"] = "admindb", ["assemblyNames"] = new[] { "ZhonTai.Admin" }, ["includeEntityDbs"] = Array.Empty<string>(), ["excludeEntityDbs"] = new[] { "Log" }, ["createDb"] = false, ["syncStructure"] = false, ["syncData"] = true, ["type"] = "MySql", ["connectionString"] = connection, ["dbs"] = new[] { logDb } };
        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "dbconfig.P0T4HostIsolation.json"), JsonSerializer.Serialize(new Dictionary<string, object> { ["DbConfig"] = db, ["P0SchemaGuard"] = guard }));
            File.WriteAllText(Path.Combine(directory, "appconfig.P0T4HostIsolation.json"), "{\"AppConfig\":{\"varifyCode\":{\"enable\":false}}}");
        }
    }

    private static string RequireT4Connection()
    {
        var connection = Environment.GetEnvironmentVariable("ADMINCORE_T4_MYSQL");
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

    private sealed class T4HostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("P0T4HostIsolation");
    }
}
