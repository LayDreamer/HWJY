using System;
using System.IO;
using FreeSql;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

public sealed class AcceptanceFixtureCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("P0Gate", "Required")]
    public void RunCleanupRemovesOnlyItsOwnRowsOnSuccessOrFailure(bool fail)
    {
        using var fixture = new CleanupDatabase();
        var db = fixture.Db;
        fixture.Seed("thisrun", 2);
        fixture.Seed("otherrun", 3);
        db.Ado.ExecuteNonQuery("insert into base_api (Id,Path) values (41, '/api/admin/p0-data-permission-fixture/existing')");
        db.Ado.ExecuteNonQuery("create table acc_p0_data_permission_fixture (Id integer primary key, OwnerId bigint)");
        db.Ado.ExecuteNonQuery("insert into acc_p0_data_permission_fixture values (1, 1), (2, 2), (3, 3)");

        void Run()
        {
            using var cleanup = new AcceptanceFixtureCleanup(db);
            cleanup.Register(orm => P0DataPermissionAcceptanceTests.CleanupP0Fixtures(orm, "thisrun"));
            db.Ado.ExecuteNonQuery("create table acc_host_business_record (Id integer primary key)");
            db.Ado.ExecuteNonQuery("insert into base_api (Id,Path) values (42, '/api/admin/p0-data-permission-fixture/thisrun')");
            db.Ado.ExecuteNonQuery("insert into base_permission_api values (2, 2, 42)");
            db.Ado.ExecuteNonQuery("insert into base_permission_api values (3, 3, 41)");
            if (fail) throw new InvalidOperationException("Injected acceptance assertion failure");
        }

        if (fail) Assert.Throws<InvalidOperationException>(Run);
        else Run();

        foreach (var table in new[] { "base_user", "base_role", "base_org", "base_permission" })
        {
            Assert.Equal(0, db.Ado.QuerySingle<int>($"select count(*) from {table} where Id = 2"));
            Assert.Equal(2, db.Ado.QuerySingle<int>($"select count(*) from {table} where Id in (1, 3)"));
        }
        foreach (var table in new[] { "base_role_permission", "base_role_org", "base_user_role", "base_user_org" })
            Assert.Equal(2, db.Ado.QuerySingle<int>($"select count(*) from {table}"));
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from base_api where Id = 41"));
        Assert.Equal(0, db.Ado.QuerySingle<int>("select count(*) from base_api where Id = 42"));
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from base_permission_api where PermissionId = 3 and ApiId = 41"));
        Assert.Equal(0, db.Ado.QuerySingle<int>("select count(*) from base_permission_api where PermissionId = 2"));
        Assert.True(db.DbFirst.ExistsTable("acc_p0_data_permission_fixture"));
        Assert.Equal(2, db.Ado.QuerySingle<int>("select count(*) from acc_p0_data_permission_fixture"));
        Assert.False(db.DbFirst.ExistsTable("acc_host_business_record"));
    }

    [Fact]
    [Trait("P0Gate", "Required")]
    public void PartialSeedingIsCleanedBeforeAnyUserWasCreated()
    {
        using var fixture = new CleanupDatabase();
        var db = fixture.Db;
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var cleanup = new AcceptanceFixtureCleanup(db);
            cleanup.Register(orm => P0DataPermissionAcceptanceTests.CleanupP0Fixtures(orm, "partial"));
            db.Ado.ExecuteNonQuery("insert into base_org (Id,Code) values (2, 'P0-DP-A-partial')");
            db.Ado.ExecuteNonQuery("insert into base_role (Id,Code) values (2, 'P0-DP-W-partial')");
            throw new InvalidOperationException("Injected seeding failure");
        }));
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from base_user"));
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from base_role"));
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from base_org"));
    }

    [Fact]
    [Trait("P0Gate", "Required")]
    public void CleanupFailureIsReportedAndRemainingCleanupStillRuns()
    {
        using var fixture = new CleanupDatabase();
        var db = fixture.Db;
        var cleanup = new AcceptanceFixtureCleanup(db);
        db.Ado.ExecuteNonQuery("create table acceptance_data_scope (Id integer primary key)");
        var secondCleanupRan = false;
        cleanup.Register(_ => throw new InvalidOperationException("Injected cleanup failure"));
        cleanup.Register(_ => secondCleanupRan = true);
        var error = Assert.Throws<AggregateException>(cleanup.Dispose);
        Assert.Single(error.InnerExceptions);
        Assert.True(secondCleanupRan);
        Assert.False(db.DbFirst.ExistsTable("acceptance_data_scope"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%")]
    [InlineData("_")]
    [Trait("P0Gate", "Required")]
    public void WildcardOrEmptyRunSuffixCannotWidenDeletion(string suffix)
    {
        using var fixture = new CleanupDatabase();
        fixture.Seed("otherrun", 3);
        Assert.Throws<ArgumentException>(() => P0DataPermissionAcceptanceTests.CleanupP0Fixtures(fixture.Db, suffix));
        Assert.Equal(2, fixture.Db.Ado.QuerySingle<int>("select count(*) from base_user"));
    }

    [Theory]
    [InlineData("update base_user set OrgId=2 where Id=1")]
    [InlineData("update base_user set ManagerUserId=2 where Id=1")]
    [InlineData("insert into base_tenant values (2,2)")]
    [InlineData("update base_org set ParentId=2 where Id=1")]
    [InlineData("update base_role set ParentId=2 where Id=1")]
    [InlineData("update base_permission set ParentId=2 where Id=1")]
    [InlineData("insert into base_user_role values (1,2)")]
    [InlineData("insert into base_user_org values (1,2)")]
    [InlineData("insert into base_role_org values (1,2)")]
    [InlineData("insert into base_role_permission values (1,2)")]
    [InlineData("insert into base_pkg_permission values (2)")]
    [Trait("P0Gate", "Required")]
    public void NormalReferenceToTestDataRefusesCleanupBeforeDeletingRows(string referenceSql)
    {
        using var fixture = new CleanupDatabase();
        fixture.Seed("thisrun", 2);
        fixture.Db.Ado.ExecuteNonQuery(referenceSql);
        Assert.Throws<InvalidOperationException>(() => P0DataPermissionAcceptanceTests.CleanupP0Fixtures(fixture.Db, "thisrun"));
        foreach (var table in new[] { "base_user", "base_role", "base_org", "base_permission" })
            Assert.Equal(2, fixture.Db.Ado.QuerySingle<int>($"select count(*) from {table}"));
        Assert.Equal(2, fixture.Db.Ado.QuerySingle<int>("select count(*) from base_role_permission where RoleId=PermissionId"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("P0Gate", "Required")]
    public void HostIdentifiersCannotDeletePreExistingApiOrRecord(bool apiCollision)
    {
        using var fixture = new CleanupDatabase();
        var db = fixture.Db;
        db.Ado.ExecuteNonQuery("create table acc_host_business_record (Id bigint primary key, RecordCode text)");
        if (apiCollision)
            db.Ado.ExecuteNonQuery("insert into base_api (Id,Path,Label) values (42,'/api/acceptance/collision','normal')");
        else
            db.Ado.ExecuteNonQuery("insert into acc_host_business_record values (42,'existing-code')");
        using (var cleanup = new AcceptanceFixtureCleanup(db))
            Assert.Throws<InvalidOperationException>(() => cleanup.RegisterHostRun("existing-code", "/api/acceptance/collision", "test"));
        var table = apiCollision ? "base_api" : "acc_host_business_record";
        Assert.Equal(1, db.Ado.QuerySingle<int>($"select count(*) from {table} where Id=42"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("P0Gate", "Required")]
    public void HostRunRecordsAreRemovedOnSuccessOrFailure(bool fail)
    {
        using var fixture = new CleanupDatabase();
        var db = fixture.Db;
        void Run()
        {
            using var cleanup = new AcceptanceFixtureCleanup(db);
            cleanup.RegisterHostRun("run-code", "/api/acceptance/run", "test");
            db.Ado.ExecuteNonQuery("create table acc_host_business_record (Id bigint primary key, RecordCode text)");
            db.Ado.ExecuteNonQuery("insert into acc_host_business_record values (42,'run-code')");
            db.Ado.ExecuteNonQuery("insert into base_api (Id,Path,Label) values (42,'/api/acceptance/run','test')");
            if (fail) throw new InvalidOperationException("Injected Host failure");
        }
        if (fail) Assert.Throws<InvalidOperationException>(Run);
        else Run();
        Assert.Equal(0, db.Ado.QuerySingle<int>("select count(*) from base_api where Id=42"));
        Assert.False(db.DbFirst.ExistsTable("acc_host_business_record"));
        Assert.Equal(1, db.Ado.QuerySingle<int>("select count(*) from base_user where Id=1"));
    }

    [Fact]
    [Trait("P0Gate", "Required")]
    public void HostCleanupRequiresItsLabelAsWellAsItsPath()
    {
        using var fixture = new CleanupDatabase();
        using (var cleanup = new AcceptanceFixtureCleanup(fixture.Db))
        {
            cleanup.RegisterHostRun("run-code", "/api/acceptance/run", "test");
            fixture.Db.Ado.ExecuteNonQuery("insert into base_api (Id,Path,Label) values (42,'/api/acceptance/run','normal')");
        }
        Assert.Equal(1, fixture.Db.Ado.QuerySingle<int>("select count(*) from base_api where Id=42 and Label='normal'"));
    }

    [Theory]
    [InlineData("insert into base_permission_api values (1,1,42)")]
    [InlineData("insert into base_api (Id,Path,ParentId) values (43,'/normal/api',42)")]
    [Trait("P0Gate", "Required")]
    public void FixtureApiReferencesArePreservedAndCleanupFailureIsReported(string referenceSql)
    {
        using var fixture = new CleanupDatabase();
        var cleanup = new AcceptanceFixtureCleanup(fixture.Db);
        fixture.Db.Ado.ExecuteNonQuery("insert into base_api (Id,Path) values (42,'/api/admin/p0-data-permission-fixture/new')");
        fixture.Db.Ado.ExecuteNonQuery(referenceSql);
        Assert.Throws<AggregateException>(cleanup.Dispose);
        Assert.Equal(1, fixture.Db.Ado.QuerySingle<int>("select count(*) from base_api where Id=42"));
    }

    private sealed class CleanupDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"admincore-cleanup-{Guid.NewGuid():N}.db");
        public IFreeSql Db { get; }

        public CleanupDatabase()
        {
            Db = new FreeSqlBuilder().UseConnectionString(DataType.Sqlite, $"Data Source={_path};Pooling=false").Build();
            foreach (var ddl in new[]
            {
                "create table base_user (Id bigint primary key, UserName text, OrgId bigint default 0, ManagerUserId bigint)",
                "create table base_role (Id bigint primary key, Code text, ParentId bigint default 0)",
                "create table base_org (Id bigint primary key, Code text, ParentId bigint default 0)",
                "create table base_permission (Id bigint primary key, Code text, ParentId bigint default 0)",
                "create table base_api (Id bigint primary key, Path text, Label text, ParentId bigint default 0)",
                "create table base_role_permission (RoleId bigint, PermissionId bigint)",
                "create table base_role_org (RoleId bigint, OrgId bigint)",
                "create table base_user_role (UserId bigint, RoleId bigint)",
                "create table base_user_org (UserId bigint, OrgId bigint)",
                "create table base_permission_api (Id bigint, PermissionId bigint, ApiId bigint)",
                "create table base_tenant (UserId bigint, OrgId bigint)",
                "create table base_pkg_permission (PermissionId bigint)"
            }) Db.Ado.ExecuteNonQuery(ddl);
            Db.Ado.ExecuteNonQuery("insert into base_user (Id,UserName) values (1, 'admin')");
            Db.Ado.ExecuteNonQuery("insert into base_role (Id,Code) values (1, null)");
            Db.Ado.ExecuteNonQuery("insert into base_org (Id,Code) values (1, 'zhontai')");
            Db.Ado.ExecuteNonQuery("insert into base_permission (Id,Code) values (1, null)");
            Db.Ado.ExecuteNonQuery("insert into base_role_permission values (1,1)");
            Db.Ado.ExecuteNonQuery("insert into base_role_org values (1,1)");
            Db.Ado.ExecuteNonQuery("insert into base_user_role values (1,1)");
            Db.Ado.ExecuteNonQuery("insert into base_user_org values (1,1)");
        }

        public void Seed(string suffix, long id)
        {
            var parameters = new { id, user = "p0-dp-w-" + suffix, role = "P0-DP-W-" + suffix, org = "P0-DP-A-" + suffix, permission = "P0.DataPermission.Add." + suffix };
            Db.Ado.ExecuteNonQuery("insert into base_user (Id,UserName) values (@id,@user)", parameters);
            Db.Ado.ExecuteNonQuery("insert into base_role (Id,Code) values (@id,@role)", parameters);
            Db.Ado.ExecuteNonQuery("insert into base_org (Id,Code) values (@id,@org)", parameters);
            Db.Ado.ExecuteNonQuery("insert into base_permission (Id,Code) values (@id,@permission)", parameters);
            Db.Ado.ExecuteNonQuery("insert into base_role_permission values (@id,@id)", parameters);
            Db.Ado.ExecuteNonQuery("insert into base_role_org values (@id,@id)", parameters);
            Db.Ado.ExecuteNonQuery("insert into base_user_role values (@id,@id)", parameters);
            Db.Ado.ExecuteNonQuery("insert into base_user_org values (@id,@id)", parameters);
        }

        public void Dispose()
        {
            Db.Dispose();
            File.Delete(_path);
        }
    }
}
