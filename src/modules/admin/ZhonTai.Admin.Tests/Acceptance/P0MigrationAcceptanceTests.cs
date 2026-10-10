using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using MySqlConnector;
using Xunit;
using ZhonTai.Admin.Core.Db;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>
/// T4 的连接与 DDL 权限前置校验。使用与 T2 相同的已还原 FreeSql/MySQL Provider，
/// 不从 PowerShell 手动加载数据库驱动。
/// </summary>
[Collection("P0T4MySql")]
public sealed class P0MigrationAcceptanceTests
{
    private const string T4Schema = "admincore_p0_t4_acceptance";

    [P0T4MySqlFact]
    [Trait("P0Gate", "Required")]
    public void DB01_DB02_DB08_DB09_DB12_ManifestApplyIsIdempotentAndExcludesRollback()
    {
        var connectionString = RequireT4Connection();
        var manifestPath = FindFile("database", "mysql", "manifest.json");
        using var db = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connectionString).UseAutoSyncStructure(false).Build();
        ResetP0ReleaseSchema(db);

        var executor = new P0MigrationExecutor(db, Path.GetDirectoryName(manifestPath)!);
        var first = executor.Apply(manifestPath, "p0-t4-acceptance");
        Assert.Equal(new[] { "20260916.001", "20260916.002" }, first.AppliedVersions);
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));
        Assert.Equal(2, db.Ado.QuerySingle<int>("select count(*) from p0_schema_migration where Status = 'Succeeded'"));

        var second = executor.Apply(manifestPath, "p0-t4-acceptance");
        Assert.Empty(second.AppliedVersions);
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));
    }

    [P0T4MySqlFact]
    [Trait("P0Gate", "Required")]
    public void DB04_DB05_DB06_ChecksumFailedAndRunningStatesBlockApply()
    {
        var connectionString = RequireT4Connection();
        var manifestPath = FindFile("database", "mysql", "manifest.json");
        using var db = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connectionString).UseAutoSyncStructure(false).Build();
        var executor = new P0MigrationExecutor(db, Path.GetDirectoryName(manifestPath)!);
        executor.Apply(manifestPath, "p0-t4-acceptance");

        db.Ado.ExecuteNonQuery("update p0_schema_migration set Checksum = 'tampered' where Version = '20260916.001'");
        Assert.Throws<InvalidOperationException>(() => executor.Apply(manifestPath, "p0-t4-acceptance"));
        var bootstrapChecksum = P0MigrationManifest.Load(manifestPath).Forward(Path.GetDirectoryName(manifestPath)!)[0].Checksum;
        db.Ado.ExecuteNonQuery("update p0_schema_migration set Checksum = @checksum where Version = '20260916.001'", new { checksum = bootstrapChecksum });

        db.Ado.ExecuteNonQuery("update p0_schema_migration set Status = 'Failed' where Version = '20260916.002'");
        Assert.Throws<InvalidOperationException>(() => executor.Apply(manifestPath, "p0-t4-acceptance"));
        db.Ado.ExecuteNonQuery("update p0_schema_migration set Status = 'Succeeded' where Version = '20260916.002'");

        db.Ado.ExecuteNonQuery("insert into p0_schema_migration (Version, Name, Checksum, Status, StartedAt) values ('test.running', 'T4 acceptance running state', 'test', 'Running', utc_timestamp())");
        try
        {
            Assert.Throws<InvalidOperationException>(() => executor.Apply(manifestPath, "p0-t4-acceptance"));
        }
        finally
        {
            db.Ado.ExecuteNonQuery("delete from p0_schema_migration where Version = 'test.running'");
        }
    }

    [P0T4MySqlFact]
    [Trait("P0Gate", "Required")]
    public void DB03_ApprovedRollbackCanBeRecoveredUsingTheApprovedForwardArtifact()
    {
        var connectionString = RequireT4Connection();
        var manifestPath = FindFile("database", "mysql", "manifest.json");
        using var db = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connectionString).UseAutoSyncStructure(false).Build();
        ResetP0ReleaseSchema(db);
        var executor = new P0MigrationExecutor(db, Path.GetDirectoryName(manifestPath)!);
        executor.Apply(manifestPath, "p0-t4-acceptance");
        executor.Rollback(manifestPath, "20260916.002", "p0-t4-acceptance");

        Assert.Equal(0, db.Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));
        Assert.Equal("RolledBack", db.Ado.QuerySingle<string>("select Status from p0_schema_migration where Version = '20260916.002'"));
        Assert.Throws<InvalidOperationException>(() => executor.Apply(manifestPath, "p0-t4-acceptance"));
        executor.Recover(manifestPath, "20260916.002", "p0-t4-recovery");
        executor.Verify(manifestPath);
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from information_schema.tables where table_schema = database() and table_name = 'base_user'"));
        Assert.Equal("Succeeded", db.Ado.QuerySingle<string>("select Status from p0_schema_migration where Version = '20260916.002'"));
    }

    [P0T4MySqlFact]
    [Trait("P0Gate", "Required")]
    public async Task DB07_TwoRealExecutorsCompeteForTheSameMigrationLock()
    {
        var connectionString = RequireT4Connection();
        var manifestPath = FindFile("database", "mysql", "manifest.json");
        using var db = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connectionString).UseAutoSyncStructure(false).Build();
        ResetP0ReleaseSchema(db);
        using var barrier = new Barrier(2);
        var attempts = Enumerable.Range(0, 2).Select(index => Task.Run(() =>
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            void Apply() => _ = new P0MigrationExecutor(connectionString, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-acceptance-" + index);
            return Record.Exception(Apply);
        })).ToArray();
        var results = await Task.WhenAll(attempts);
        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is InvalidOperationException);
        Assert.Empty(new P0MigrationExecutor(connectionString, Path.GetDirectoryName(manifestPath)!).Apply(manifestPath, "p0-t4-acceptance-after-lock").AppliedVersions);
    }

    private static void ResetP0ReleaseSchema(IFreeSql db)
    {
        var tables = db.Ado.Query<string>("select table_name from information_schema.tables where table_schema = database() and (table_name like 'base_%' or table_name like 'p0_%')");
        foreach (var table in tables)
        {
            db.Ado.ExecuteNonQuery($"drop table if exists `{table}`");
        }
    }

    private static string FindFile(params string[] path)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, Path.Combine(path));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("The P0 migration manifest was not found.");
    }

    private static string RequireT4Connection()
    {
        var connection = Environment.GetEnvironmentVariable("ADMINCORE_T4_MYSQL");
        Assert.False(string.IsNullOrWhiteSpace(connection), "Set ADMINCORE_T4_MYSQL to the dedicated T4 acceptance schema.");
        Assert.Equal(T4Schema, new MySqlConnectionStringBuilder(connection).Database);
        return connection!;
    }
}

[CollectionDefinition("P0T4MySql", DisableParallelization = true)]
public sealed class P0T4MySqlCollection;
