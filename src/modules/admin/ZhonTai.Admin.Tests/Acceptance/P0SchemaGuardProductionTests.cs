using System;
using FreeSql;
using Xunit;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Db;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class P0SchemaGuardProductionTests
{
    [Fact]
    public void DevelopmentSqliteWithoutGuardRemainsAvailable()
    {
        P0SchemaGuard.ValidateConfiguration(null!, new DbConfig { Type = DataType.Sqlite }, isProduction: false);
    }

    [Fact]
    public void ProductionRejectsMissingOrDisabledGuardBeforeDatabaseAccess()
    {
        var db = SafeMySqlConfig();
        var missing = Assert.Throws<InvalidOperationException>(() => P0SchemaGuard.ValidateConfiguration(null!, db, isProduction: true));
        Assert.Contains("Enabled=true", missing.Message);

        var disabled = Assert.Throws<InvalidOperationException>(() => P0SchemaGuard.ValidateConfiguration(new P0SchemaGuardOptions { Enabled = false }, db, isProduction: true));
        Assert.Contains("Enabled=true", disabled.Message);
    }

    [Fact]
    public void ProductionRejectsAutomaticDatabaseAndSeedWritesBeforeDatabaseAccess()
    {
        var db = SafeMySqlConfig();
        db.SyncData = true;
        var exception = Assert.Throws<InvalidOperationException>(() => P0SchemaGuard.ValidateConfiguration(EnabledGuard(), db, isProduction: true));
        Assert.Contains("syncData", exception.Message);
    }

    [Fact]
    public void ProductionRejectsWritableNestedLogOrTaskDatabaseBeforeDatabaseAccess()
    {
        var db = SafeMySqlConfig();
        db.Dbs = [new DbConfig { Key = "logdb", Type = DataType.MySql, ConnectionString = "Server=localhost;Database=isolated_log;", CreateDb = true }];
        var exception = Assert.Throws<InvalidOperationException>(() => P0SchemaGuard.ValidateConfiguration(EnabledGuard(), db, isProduction: true));
        Assert.Contains("logdb", exception.Message);
    }

    [Fact]
    public void ProductionAllowsAClosedMySqlConfigurationUntilReadOnlySchemaVerification()
    {
        P0SchemaGuard.ValidateConfiguration(EnabledGuard(), SafeMySqlConfig(), isProduction: true);
    }

    private static P0SchemaGuardOptions EnabledGuard() => new() { Enabled = true, ExpectedVersion = "20260916.002", ExpectedChecksum = "test-checksum" };

    private static DbConfig SafeMySqlConfig() => new()
    {
        Key = "admindb",
        Type = DataType.MySql,
        ConnectionString = "Server=localhost;Database=isolated_admin;",
        CreateDb = false,
        SyncStructure = false,
        SyncData = false,
        SyncUpdateData = false,
        GenerateData = false
    };
}
