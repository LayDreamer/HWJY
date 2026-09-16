using FreeSql;
using Microsoft.Extensions.Configuration;
using ZhonTai.Admin.Core.Configs;

namespace ZhonTai.Admin.Core.Db;

/// <summary>
/// P0 发布库启动校验。关闭时不改变既有开发 CodeFirst 行为；开启时在任何结构同步或 Seed 前校验迁移记录。
/// </summary>
public sealed class P0SchemaGuardOptions
{
    public bool Enabled { get; set; }
    public string ManifestPath { get; set; }
    public string ExpectedVersion { get; set; }
    public string ExpectedChecksum { get; set; }
    /// <summary>Release-critical tables that must already exist; the guard never creates or repairs them.</summary>
    public string[] RequiredTables { get; set; } = ["base_user", "base_permission", "base_api", "base_task", "base_login_log"];
}

public static class P0SchemaGuard
{
    public static void VerifyBeforeDatabaseInitialization(IConfiguration configuration)
    {
        var options = configuration.GetSection("P0SchemaGuard").Get<P0SchemaGuardOptions>();
        if (options?.Enabled != true)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.ManifestPath))
        {
            var manifestPath = Path.GetFullPath(options.ManifestPath);
            var target = P0MigrationManifest.Load(manifestPath).Forward(Path.GetDirectoryName(manifestPath)!)[^1];
            options.ExpectedVersion = target.Version;
            options.ExpectedChecksum = target.Checksum;
        }

        if (string.IsNullOrWhiteSpace(options.ExpectedVersion) || string.IsNullOrWhiteSpace(options.ExpectedChecksum))
        {
            throw new InvalidOperationException("P0 Schema Guard requires a release ManifestPath or explicit expected version and checksum when enabled.");
        }

        var dbConfig = configuration.GetSection("DbConfig").Get<DbConfig>();
        if (dbConfig == null || dbConfig.Type != DataType.MySql || string.IsNullOrWhiteSpace(dbConfig.ConnectionString))
        {
            throw new InvalidOperationException("P0 Schema Guard requires an explicit MySQL DbConfig.");
        }

        using var db = new FreeSqlBuilder()
            .UseConnectionString(DataType.MySql, dbConfig.ConnectionString)
            .UseAutoSyncStructure(false)
            .Build();

        try
        {
            var record = db.Ado.QuerySingle<P0SchemaMigrationRecord>(
                "select Version, Checksum, Status from p0_schema_migration where Version = @version order by Id desc limit 1",
                new { version = options.ExpectedVersion });
            if (record == null
                || !string.Equals(record.Checksum, options.ExpectedChecksum, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(record.Status, "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"P0 Schema Guard rejected database: expected {options.ExpectedVersion} / checksum {options.ExpectedChecksum} / Succeeded.");
            }

            foreach (var table in options.RequiredTables.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var exists = db.Ado.QuerySingle<long>(
                    "select count(*) from information_schema.tables where table_schema = database() and table_name = @table",
                    new { table });
                if (exists != 1)
                {
                    throw new InvalidOperationException($"P0 Schema Guard rejected database: required release table {table} is missing.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("P0 Schema Guard could not verify p0_schema_migration before database initialization.", exception);
        }
    }

    private sealed class P0SchemaMigrationRecord
    {
        public string Version { get; init; }
        public string Checksum { get; init; }
        public string Status { get; init; }
    }
}
