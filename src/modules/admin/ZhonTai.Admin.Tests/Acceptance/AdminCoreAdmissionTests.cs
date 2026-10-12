using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FreeScheduler;
using FreeSql;
using FreeSql.DataAnnotations;
using Xunit;
using ZhonTai.Admin.Core;
using ZhonTai.Admin.Core.Auth;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Db;
using ZhonTai.Admin.Core.Db.Transaction;
using ZhonTai.Admin.Core.Entities;
using ZhonTai.Admin.Core.Extensions;
using ZhonTai.Admin.Core.Startup;
using ZhonTai.Admin.Domain.Role;
using ZhonTai.Admin.Domain.Tenant;
using ZhonTai.Admin.Domain.User;
using ZhonTai.Admin.Domain.User.Dto;
using ZhonTai.Admin.Services.User.Dto;

namespace ZhonTai.Admin.Tests.Acceptance;

[Trait("P0Gate", "Required")]
public sealed class AdminCoreAdmissionTests
{
    [Fact]
    public void TwoAccountsAreIsolatedByTheExistingFreeSqlDataFilter()
    {
        var user = new MutableUser
        {
            IdValue = 101,
            Permission = new DataPermissionOutput { DataScope = DataScope.Custom, OrgIds = [10] }
        };
        var database = CreateDatabase(user);
        try
        {
            database.Db.Insert(new[]
            {
                new AdmissionRecord { Id = 1, OwnerId = 101, OwnerOrgId = 10, Name = "user-a-own" },
                new AdmissionRecord { Id = 2, OwnerId = 999, OwnerOrgId = 10, Name = "org-a" },
                new AdmissionRecord { Id = 3, OwnerId = 202, OwnerOrgId = 20, Name = "user-b-own" }
            }).ExecuteAffrows();

            Assert.Equal(new long[] { 1, 2 }, database.Db.Select<AdmissionRecord>().OrderBy(x => x.Id).ToList(x => x.Id));

            user.IdValue = 202;
            user.Permission = new DataPermissionOutput { DataScope = DataScope.Custom, OrgIds = [20] };
            Assert.Equal(new long[] { 3 }, database.Db.Select<AdmissionRecord>().OrderBy(x => x.Id).ToList(x => x.Id));
        }
        finally
        {
            database.Dispose();
        }
    }

    [Fact]
    public void ExistingFreeSqlUnitOfWorkCommitsAndRollsBackAtomically()
    {
        var database = CreateDatabase(MutableUser.System);
        try
        {
            using var manager = new UnitOfWorkManagerCloud(database.Cloud);
            using (var unitOfWork = manager.Begin(database.Key))
            {
                unitOfWork.Orm.Insert(new AdmissionRecord { Id = 10, Name = "rollback" }).ExecuteAffrows();
                unitOfWork.Rollback();
            }
            Assert.False(database.Db.Select<AdmissionRecord>().Any(x => x.Id == 10));

            using (var unitOfWork = manager.Begin(database.Key))
            {
                unitOfWork.Orm.Insert(new AdmissionRecord { Id = 11, Name = "commit" }).ExecuteAffrows();
                unitOfWork.Commit();
            }
            Assert.True(database.Db.Select<AdmissionRecord>().Any(x => x.Id == 11));
        }
        finally
        {
            database.Dispose();
        }
    }

    [Fact]
    public async Task FreeSchedulerReloadsAndExecutesAPersistedTaskAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"admincore-scheduler-{Guid.NewGuid():N}.db");
        try
        {
            string taskId;
            using (var firstDb = BuildSqlite(path))
            {
                SyncScheduler(firstDb);
                var firstScheduler = new FreeSchedulerBuilder().UseStorage(firstDb).Build();
                taskId = firstScheduler.AddTask("acceptance-restart", "{}", 1, 3600);
                (firstScheduler as IDisposable)?.Dispose();
            }

            var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var restartedDb = BuildSqlite(path))
            {
                SyncScheduler(restartedDb);
                var restartedScheduler = new FreeSchedulerBuilder()
                    .UseStorage(restartedDb)
                    .OnExecuting(task =>
                    {
                        if (task.Id == taskId) executed.TrySetResult(true);
                    })
                    .Build();

                Assert.True(restartedScheduler.ExistsTask(taskId));
                restartedScheduler.RunNowTask(taskId);
                await executed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                (restartedScheduler as IDisposable)?.Dispose();
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    [MySqlAdmissionFact]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public void MySql8ProviderSchemaAndTransactionAreAccepted()
    {
        var connectionString = Environment.GetEnvironmentVariable("ADMINCORE_ACCEPTANCE_MYSQL")!;
        var key = $"mysql-acceptance-{Guid.NewGuid():N}";
        var config = NewDbConfig(key, FreeSql.DataType.MySql, connectionString);
        var cloud = new FreeSqlCloud();
        DbHelper.RegisterDb(cloud, MutableUser.System, config, new AppConfig { Tenant = false }, new HostAppOptions());
        var db = cloud.Use(key);
        var id = DateTime.UtcNow.Ticks;
        using var manager = new UnitOfWorkManagerCloud(cloud);
        using (var unitOfWork = manager.Begin(key))
        {
            unitOfWork.Orm.Insert(new AdmissionRecord { Id = id, Name = "mysql-rollback" }).ExecuteAffrows();
            unitOfWork.Rollback();
        }
        Assert.False(db.Select<AdmissionRecord>().Any(x => x.Id == id));
        db.Dispose();
    }

    private static AdmissionDatabase CreateDatabase(MutableUser user)
    {
        var path = Path.Combine(Path.GetTempPath(), $"admincore-freesql-{Guid.NewGuid():N}.db");
        var key = $"acceptance-{Guid.NewGuid():N}";
        var config = NewDbConfig(key, FreeSql.DataType.Sqlite, $"Data Source={path};Pooling=false");
        var cloud = new FreeSqlCloud();
        DbHelper.RegisterDb(cloud, user, config, new AppConfig { Tenant = false }, new HostAppOptions());
        return new AdmissionDatabase(key, path, cloud, cloud.Use(key));
    }

    private static DbConfig NewDbConfig(string key, FreeSql.DataType type, string connectionString) => new()
    {
        Key = key,
        Type = type,
        ConnectionString = connectionString,
        CreateDb = false,
        SyncStructure = true,
        SyncData = false,
        AssemblyNames = [typeof(AdmissionRecord).Assembly.GetName().Name!]
    };

    private static IFreeSql BuildSqlite(string path) => new FreeSqlBuilder()
        .UseConnectionString(FreeSql.DataType.Sqlite, $"Data Source={path};Pooling=false")
        .Build();

    private static void SyncScheduler(IFreeSql db) => db.SyncSchedulerStructure(
        new DbConfig { Type = FreeSql.DataType.Sqlite, SyncStructure = true },
        TaskSchedulerServiceExtensions.ConfigureScheduler);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // FreeSql 的 SQLite 连接池可能晚于测试对象释放；文件位于系统临时目录。
        }
    }

    private sealed record AdmissionDatabase(string Key, string Path, FreeSqlCloud Cloud, IFreeSql Db) : IDisposable
    {
        public void Dispose()
        {
            Db.Dispose();
            TryDelete(Path);
        }
    }
}

[Table(Name = "acceptance_data_scope")]
public sealed class AdmissionRecord : EntityData
{
    public string Name { get; set; } = string.Empty;
}

public sealed class MutableUser : IUser
{
    public static MutableUser System { get; } = new()
    {
        IdValue = 1,
        Permission = new DataPermissionOutput { DataScope = DataScope.All, OrgIds = [] }
    };

    public long IdValue { get; set; }
    public DataPermissionOutput Permission { get; set; } = new() { OrgIds = [] };
    public long Id => IdValue;
    public string UserName => "acceptance";
    public string Name => "acceptance";
    public UserType Type => UserType.DefaultUser;
    public bool DefaultUser => false;
    public bool PlatformAdmin => false;
    public bool TenantAdmin => false;
    public long? TenantId => null;
    public TenantType? TenantType => null;
    public string DbKey => string.Empty;
    public DataPermissionOutput DataPermission => Permission;
    public UserGetPermissionOutput UserPermission => new();
    public bool HasPermission(string permissionCode) => false;
    public bool HasPermissions(string[] permissionCodes, bool all = false) => false;
}

public sealed class MySqlAdmissionFactAttribute : FactAttribute
{
    public MySqlAdmissionFactAttribute()
    {
        if (P0MySqlAcceptanceEnvironment.IsMissing("ADMINCORE_ACCEPTANCE_MYSQL"))
            Skip = P0MySqlAcceptanceEnvironment.Missing("ADMINCORE_ACCEPTANCE_MYSQL");
    }
}

public sealed class SchedulerIsolationTest : BaseTest
{
    [Fact]
    public void ApiHostDoesNotAcceptPersistentTaskTriggers()
    {
        var scheduler = GetRequiredService<Scheduler>();
        Assert.False(scheduler.GetPersistentTaskRuntimeState().AcceptingNewTriggers);
    }
}
