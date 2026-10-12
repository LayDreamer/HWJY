using System;
using System.Collections.Generic;
using System.Linq;
using FreeSql;
using FreeSql.DataAnnotations;
using Xunit;
using ZhonTai.Admin.Core;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Db;
using ZhonTai.Admin.Core.Db.Transaction;
using ZhonTai.Admin.Core.Startup;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>
/// T2 专用夹具：不属于正式业务模块，也不会由 Production Host 装配。
/// 事务断言始终使用独立连接读取专用验收库。
/// </summary>
[Collection("P0T4MySql")]
public sealed class P0T2ModuleAndTransactionAcceptanceTests
{
    internal const string DbKey = "p0-t2-acceptance";

    [Fact]
    [Trait("P0Gate", "Required")]
    public void MOD01_ContractDependency_IsAccepted()
    {
        var result = P0ModuleBoundaryChecker.Check(new ModuleDependency(
            "ModuleA",
            "ModuleB",
            ["IModuleBContract", "ModuleBReadDto", "ModuleBWriteRequest", "ModuleBWriteResult"]));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Violations);
    }

    [Fact]
    [Trait("P0Gate", "Required")]
    public void MOD02_DirectOwnerInternals_AreRejected()
    {
        var result = P0ModuleBoundaryChecker.Check(new ModuleDependency(
            "ModuleA",
            "ModuleB",
            ["ModuleBEntity", "ModuleBRepository", "IFreeSql"]));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(result.Violations, violation => violation.Contains("ModuleBEntity", StringComparison.Ordinal));
        Assert.Contains(result.Violations, violation => violation.Contains("ModuleBRepository", StringComparison.Ordinal));
        Assert.Contains(result.Violations, violation => violation.Contains("IFreeSql", StringComparison.Ordinal));
    }

    [P0T4MySqlFact]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public void TX01_TX02_TX03_OwnerTransaction_IsAtomicOnMySql8()
    {
        var connectionString = Environment.GetEnvironmentVariable("ADMINCORE_T4_MYSQL")!;
        var runId = Guid.NewGuid().ToString("N");
        using var fixture = T2MySqlFixture.Create(connectionString);

        try
        {
            fixture.EnsureSchema();

            // TX-01: 同一 Owner 的两张表均提交。
            var commitHeader = "T2-TX01-H-" + runId;
            var commitDetail = "T2-TX01-D-" + runId;
            using (var unitOfWork = fixture.Manager.Begin(DbKey))
            {
                unitOfWork.Orm.Insert(new P0T2OwnerHeader { CorrelationId = commitHeader }).ExecuteAffrows();
                unitOfWork.Orm.Insert(new P0T2OwnerDetail { CorrelationId = commitDetail, UniqueCode = "T2-TX01-U-" + runId }).ExecuteAffrows();
                unitOfWork.Commit();
            }
            Assert.Equal(1, fixture.IndependentHeaderCount(commitHeader));
            Assert.Equal(1, fixture.IndependentDetailCount(commitDetail));

            // TX-02: 第二步业务异常后两张表均回滚。
            var exceptionHeader = "T2-TX02-H-" + runId;
            var exceptionDetail = "T2-TX02-D-" + runId;
            using (var unitOfWork = fixture.Manager.Begin(DbKey))
            {
                try
                {
                    unitOfWork.Orm.Insert(new P0T2OwnerHeader { CorrelationId = exceptionHeader }).ExecuteAffrows();
                    unitOfWork.Orm.Insert(new P0T2OwnerDetail { CorrelationId = exceptionDetail, UniqueCode = "T2-TX02-U-" + runId }).ExecuteAffrows();
                    throw new InvalidOperationException("T2 injected second-step exception");
                }
                catch (InvalidOperationException)
                {
                    unitOfWork.Rollback();
                }
            }
            Assert.Equal(0, fixture.IndependentHeaderCount(exceptionHeader));
            Assert.Equal(0, fixture.IndependentDetailCount(exceptionDetail));

            // TX-03: 第二步唯一键冲突后，第一张表的写入也必须回滚。
            var conflictCode = "T2-TX03-U-" + runId;
            fixture.IndependentDb.Insert(new P0T2OwnerDetail
            {
                CorrelationId = "T2-TX03-EXISTING-" + runId,
                UniqueCode = conflictCode
            }).ExecuteAffrows();
            var conflictHeader = "T2-TX03-H-" + runId;
            using (var unitOfWork = fixture.Manager.Begin(DbKey))
            {
                try
                {
                    unitOfWork.Orm.Insert(new P0T2OwnerHeader { CorrelationId = conflictHeader }).ExecuteAffrows();
                    unitOfWork.Orm.Insert(new P0T2OwnerDetail
                    {
                        CorrelationId = "T2-TX03-D-" + runId,
                        UniqueCode = conflictCode
                    }).ExecuteAffrows();
                    throw new Xunit.Sdk.XunitException("Expected a unique-key conflict.");
                }
                catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
                {
                    unitOfWork.Rollback();
                }
            }
            Assert.Equal(0, fixture.IndependentHeaderCount(conflictHeader));
            Assert.Equal(1, fixture.IndependentCountByUniqueCode(conflictCode));
        }
        finally
        {
            fixture.DeleteRunRows(runId);
        }
    }
}

internal sealed record ModuleDependency(string ConsumerModule, string OwnerModule, IReadOnlyCollection<string> ReferencedTypes);

internal sealed record ModuleBoundaryResult(int ExitCode, IReadOnlyCollection<string> Violations);

/// <summary>
/// 最小可执行的 P0 边界检查器。生产依赖的采集器接入后应复用本规则，不能放宽禁止类型。
/// </summary>
internal static class P0ModuleBoundaryChecker
{
    private static readonly string[] ForbiddenFragments = ["Entity", "Repository", "IFreeSql", "FreeSql"];

    public static ModuleBoundaryResult Check(ModuleDependency dependency)
    {
        var violations = dependency.ReferencedTypes
            .Where(type => ForbiddenFragments.Any(fragment => type.Contains(fragment, StringComparison.Ordinal)))
            .Select(type => $"{dependency.ConsumerModule} directly references forbidden {dependency.OwnerModule} internal: {type}")
            .ToArray();

        return new ModuleBoundaryResult(violations.Length == 0 ? 0 : 1, violations);
    }
}

[Table(Name = "p0_t2_owner_header")]
internal sealed class P0T2OwnerHeader
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(StringLength = 80)]
    public string CorrelationId { get; set; } = string.Empty;
}

[Table(Name = "p0_t2_owner_detail")]
[Index("ux_p0_t2_owner_detail_unique_code", nameof(UniqueCode), true)]
internal sealed class P0T2OwnerDetail
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    [Column(StringLength = 80)]
    public string CorrelationId { get; set; } = string.Empty;

    [Column(StringLength = 100)]
    public string UniqueCode { get; set; } = string.Empty;
}

internal sealed class T2MySqlFixture : IDisposable
{
    private readonly FreeSqlCloud _cloud = new();

    private T2MySqlFixture(string connectionString)
    {
        var config = new DbConfig
        {
            Key = P0T2ModuleAndTransactionAcceptanceTests.DbKey,
            Type = DataType.MySql,
            ConnectionString = connectionString,
            CreateDb = false,
            SyncStructure = true,
            SyncData = false,
            AssemblyNames = [typeof(P0T2OwnerHeader).Assembly.GetName().Name!]
        };
        DbHelper.RegisterDb(_cloud, MutableUser.System, config, new AppConfig { Tenant = false }, new HostAppOptions());
        IndependentDb = new FreeSqlBuilder().UseConnectionString(DataType.MySql, connectionString).Build();
        Manager = new UnitOfWorkManagerCloud(_cloud);
    }

    public IFreeSql IndependentDb { get; }

    public UnitOfWorkManagerCloud Manager { get; }

    public static T2MySqlFixture Create(string connectionString) => new(connectionString);

    public void EnsureSchema()
    {
        _cloud.Use(P0T2ModuleAndTransactionAcceptanceTests.DbKey).CodeFirst.SyncStructure(typeof(P0T2OwnerHeader), typeof(P0T2OwnerDetail));
    }

    public long IndependentHeaderCount(string correlationId)
        => IndependentDb.Select<P0T2OwnerHeader>().Where(item => item.CorrelationId == correlationId).Count();

    public long IndependentDetailCount(string correlationId)
        => IndependentDb.Select<P0T2OwnerDetail>().Where(item => item.CorrelationId == correlationId).Count();

    public long IndependentCountByUniqueCode(string uniqueCode)
        => IndependentDb.Select<P0T2OwnerDetail>().Where(item => item.UniqueCode == uniqueCode).Count();

    public void DeleteRunRows(string runId)
    {
        IndependentDb.Delete<P0T2OwnerHeader>().Where(item => item.CorrelationId.Contains(runId)).ExecuteAffrows();
        IndependentDb.Delete<P0T2OwnerDetail>().Where(item => item.CorrelationId.Contains(runId)).ExecuteAffrows();
    }

    public void Dispose()
    {
        IndependentDb.Dispose();
        _cloud.Use(P0T2ModuleAndTransactionAcceptanceTests.DbKey).Dispose();
    }
}
