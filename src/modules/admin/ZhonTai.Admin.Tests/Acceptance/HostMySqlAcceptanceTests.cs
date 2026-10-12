using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FreeSql;
using FreeSql.DataAnnotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Yitter.IdGenerator;
using ZhonTai.Admin.Domain;
using ZhonTai.Admin.Domain.Org;
using ZhonTai.Admin.Domain.Permission;
using ZhonTai.Admin.Domain.Role;
using ZhonTai.Admin.Domain.RolePermission;
using ZhonTai.Admin.Domain.User;
using ZhonTai.Admin.Domain.UserOrg;
using ZhonTai.Admin.Domain.UserRole;
using ZhonTai.Common.Helpers;
using Xunit;
using ZhonTai.Admin.Core.Consts;
using ZhonTai.Admin.Core.Db.Transaction;
using ZhonTai.Admin.Core.Entities;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>
/// 宿主级 MySQL 8 验收。
/// 与 MySql8ProviderSchemaAndTransactionAreAccepted 的区别：后者只验证 Provider 层，
/// 本测试启动完整 HostApp（结构同步 + 种子数据 + 任务调度表 + 真实 HTTP 管线）并指向 MySQL。
/// </summary>
public sealed class HostMySqlAcceptanceTests
{
    private const string EnvName = "HostMySqlAcceptance";
    private const string MainConnectionVariable = "ADMINCORE_HOST_MYSQL";
    private const string LogConnectionVariable = "ADMINCORE_HOST_MYSQL_LOG";
    private const string BusinessTable = "acc_host_business_record";

    [HostMySqlAcceptanceFact]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public async Task FullHostRunsOnMySql8AndBusinessDataSurvivesRestart()
    {
        var mainConnection = Environment.GetEnvironmentVariable(MainConnectionVariable)!;
        var logConnection = Environment.GetEnvironmentVariable(LogConnectionVariable)!;
        WriteHostConfigs(mainConnection, logConnection);

        var recordCode = "ACC-REC-" + Guid.NewGuid().ToString("N")[..12];
        var apiLabel = "验收接口-" + Guid.NewGuid().ToString("N")[..12];
        var apiPath = "/api/acceptance/" + Guid.NewGuid().ToString("N")[..12];

        // 先删掉验收业务表，以便证明建表动作由宿主自身完成。
        using (var probe = BuildMySql(mainConnection))
        {
            probe.Ado.ExecuteNonQuery($"drop table if exists `{BusinessTable}`");
        }

        // ---------- 阶段 1：首个宿主实例 ----------
        using (var app = CreateHost())
        {
            var cloud = app.Services.GetRequiredService<FreeSqlCloud>();
            var mainDb = cloud.Use(DbKeys.AdminDb);
            var logDb = cloud.Use(DbKeys.LogDb);

            // 1. 宿主确实运行在 MySQL 8 上
            Assert.Equal(DataType.MySql, mainDb.Ado.DataType);
            Assert.Equal(DataType.MySql, logDb.Ado.DataType);
            Assert.StartsWith("8.", mainDb.Ado.QuerySingle<string>("select version()"));

            // 2. 主库与日志库物理分离（验证 FreeSqlCloud 多库键在 MySQL 下生效）
            var mainSchema = mainDb.Ado.QuerySingle<string>("select database()");
            var logSchema = logDb.Ado.QuerySingle<string>("select database()");
            Assert.NotEqual(mainSchema, logSchema);

            // 3. 宿主 CodeFirst 在 MySQL 上建出了真实业务表，且含 P0 §18.1 要求的扩展列
            var columns = mainDb.Ado.Query<string>(
                "select column_name from information_schema.columns where table_schema = @schema and table_name = @table",
                new { schema = mainSchema, table = BusinessTable });
            Assert.NotEmpty(columns);
            foreach (var required in new[] { "Id", "OwnerId", "OwnerOrgId", "SourceSystem", "SourceId", "IsDeleted" })
            {
                Assert.Contains(columns, name => string.Equals(name, required, StringComparison.OrdinalIgnoreCase));
            }

            // 4. 种子数据在 MySQL 上真实落库（登录与权限依赖）
            Assert.True(mainDb.Ado.QuerySingle<int>("select count(*) from base_user") > 0);
            Assert.True(mainDb.Ado.QuerySingle<int>("select count(*) from base_permission") > 0);

            // 5. FreeScheduler 持久化表在 MySQL 上建表成功（调度域随主库）
            Assert.Equal(1, mainDb.Ado.QuerySingle<int>(
                "select count(*) from information_schema.tables where table_schema = @schema and table_name = 'base_task'",
                new { schema = mainSchema }));

            // 6. 真实 HTTP 登录，拿到 JWT
            using var client = app.CreateClient();
            await LoginAsync(client);

            // 7. 经完整 API 管线在 MySQL 上写入真实业务数据（base_api 属主库）
            var addResponse = await PostJsonAsync(client, "/api/admin/api/add", new
            {
                Label = apiLabel,
                Path = apiPath,
                HttpMethods = "post"
            });
            Assert.True(IsSuccess(addResponse), $"新增接口失败：{addResponse}");

            Assert.Equal(1, mainDb.Ado.QuerySingle<int>(
                "select count(*) from base_api where path = @path", new { path = apiPath }));

            // 8. 经完整 API 管线读回（证明写入数据可被 ORM + 权限 + 序列化链路读回）
            var pageResponse = await PostJsonAsync(client, "/api/admin/api/get-page", new
            {
                CurrentPage = 1,
                PageSize = 10,
                Filter = new { Label = apiLabel }
            });
            Assert.True(IsSuccess(pageResponse), $"分页查询失败：{pageResponse}");
            Assert.Contains(apiLabel, pageResponse, StringComparison.Ordinal);

            // 9. 事务提交与回滚在 MySQL 上均生效（真实业务表 + 多库工作单元）
            using (var manager = new UnitOfWorkManagerCloud(cloud))
            {
                using (var unitOfWork = manager.Begin(DbKeys.AdminDb))
                {
                    unitOfWork.Orm.Insert(new AccHostBusinessRecord
                    {
                        RecordCode = recordCode + "-ROLLBACK",
                        RecordName = "回滚用例",
                        SourceSystem = "ETS",
                        SourceId = "SRC-ROLLBACK"
                    }).ExecuteAffrows();
                    unitOfWork.Rollback();
                }

                Assert.Equal(0, CountBusinessRows(mainDb, recordCode + "-ROLLBACK"));

                using (var unitOfWork = manager.Begin(DbKeys.AdminDb))
                {
                    unitOfWork.Orm.Insert(new AccHostBusinessRecord
                    {
                        RecordCode = recordCode,
                        RecordName = "提交用例",
                        SourceSystem = "ETS",
                        SourceId = "SRC-001"
                    }).ExecuteAffrows();
                    unitOfWork.Commit();
                }

                Assert.Equal(1, CountBusinessRows(mainDb, recordCode));
            }
        }

        // ---------- 阶段 2：重启宿主的同一发布物 ----------
        using (var restarted = CreateHost())
        {
            var cloud = restarted.Services.GetRequiredService<FreeSqlCloud>();
            var mainDb = cloud.Use(DbKeys.AdminDb);

            // 10. 重启后结构与数据仍在，且结构同步可重复执行
            Assert.Equal(1, CountBusinessRows(mainDb, recordCode));
            Assert.StartsWith("8.", mainDb.Ado.QuerySingle<string>("select version()"));
            Assert.Equal(1, mainDb.Ado.QuerySingle<int>(
                "select count(*) from base_api where path = @path", new { path = apiPath }));

            // 11. 重启后仍可正常登录（种子与权限数据未被破坏）
            using var client = restarted.CreateClient();
            await LoginAsync(client);
        }
    }

    /// <summary>
    /// P0-1：以非平台管理员的真实 JWT 验证 Material API 的权限点、RoleOrg 数据范围和管理员可见范围。
    /// 测试数据只写入由 ADMINCORE_HOST_MYSQL 明确指定的验收库。
    /// </summary>
    [Fact(Skip = "Legacy smoke fixture: Material is not an active business domain.")]
    public async Task MaterialApiUsesJwtRbacAndRoleOrgDataIsolationOnMySql8()
    {
        var mainConnection = Environment.GetEnvironmentVariable(MainConnectionVariable)!;
        var logConnection = Environment.GetEnvironmentVariable(LogConnectionVariable)!;
        WriteHostConfigs(mainConnection, logConnection);

        using var app = CreateHost();
        var cloud = app.Services.GetRequiredService<FreeSqlCloud>();
        var mainDb = cloud.Use(DbKeys.AdminDb);
        var suffix = Guid.NewGuid().ToString("N")[..12];

        // Host 启动时必须已经把模块 API、权限点与 PermissionApi 关系同步到主库。
        foreach (var permissionCode in new[] { "Material.Query", "Material.Create", "Material.Update", "Material.Delete" })
        {
            Assert.Equal(1, mainDb.Ado.QuerySingle<int>(
                "select count(*) from base_permission where code = @code", new { code = permissionCode }));
        }
        Assert.Equal(6, mainDb.Ado.QuerySingle<int>(
            "select count(*) from base_api where path like '/api/master-data/material/%'"));
        Assert.Equal(6, mainDb.Ado.QuerySingle<int>(
            "select count(*) from base_permission_api pa inner join base_api a on pa.ApiId = a.Id where a.path like '/api/master-data/material/%'"));
        var mainSchema = mainDb.Ado.QuerySingle<string>("select database()");
        Assert.Equal(1, mainDb.Ado.QuerySingle<int>(
            "select count(distinct index_name) from information_schema.statistics where table_schema = @schema and table_name = 'md_material' and index_name = 'uk_md_material_source_system_source_id' and non_unique = 0",
            new { schema = mainSchema }));

        var access = await SeedMaterialAcceptanceAccessAsync(mainDb, suffix);
        var materialCode = "ACC-P0-" + suffix;

        using var writer = app.CreateClient();
        await LoginAsync(writer, access.WriterUserName, access.Password);
        var addResponse = await PostJsonAsync(writer, "/api/master-data/material/add", new
        {
            MaterialCode = materialCode,
            MaterialName = "P0-1 组织隔离验收物料",
            MaterialType = "RAW",
            Status = "Enabled",
            IsPurchase = true,
            IsProduce = false,
            LeadTime = 0,
            SafetyStock = 0
        });
        Assert.True(IsSuccess(addResponse), $"物料新增失败：{addResponse}");

        var material = mainDb.Ado.QuerySingle<MaterialAcceptanceRecord>(
            "select Id, OwnerId, OwnerOrgId, SourceSystem, SourceId, MaterialName, IsDeleted from md_material where MaterialCode = @materialCode",
            new { materialCode });
        Assert.NotNull(material);
        Assert.Equal(access.WriterOrgId, material.OwnerOrgId);
        Assert.Equal(access.WriterUserId, material.OwnerId);
        Assert.Equal("INTERNAL", material.SourceSystem);
        Assert.False(string.IsNullOrWhiteSpace(material.SourceId));

        // 同一角色授权的第二个用户可通过 RoleOrg 看到组织 A 的数据。
        using var sameOrgReader = app.CreateClient();
        await LoginAsync(sameOrgReader, access.SameOrgReaderUserName, access.Password);
        var sameOrgPage = await PostJsonAsync(sameOrgReader, "/api/master-data/material/get-page", new
        {
            CurrentPage = 1,
            PageSize = 20,
            Filter = new { MaterialCode = materialCode }
        });
        Assert.True(IsSuccess(sameOrgPage), $"同组织查询失败：{sameOrgPage}");
        Assert.Contains(materialCode, sameOrgPage, StringComparison.Ordinal);

        // 组织 B 只有查询权限：JWT 可以访问查询 API，但不能看见组织 A 的物料，也不能新增。
        using var otherOrgReader = app.CreateClient();
        await LoginAsync(otherOrgReader, access.OtherOrgReaderUserName, access.Password);
        var otherOrgPage = await PostJsonAsync(otherOrgReader, "/api/master-data/material/get-page", new
        {
            CurrentPage = 1,
            PageSize = 20,
            Filter = new { MaterialCode = materialCode }
        });
        Assert.True(IsSuccess(otherOrgPage), $"跨组织查询请求失败：{otherOrgPage}");
        Assert.DoesNotContain(materialCode, otherOrgPage, StringComparison.Ordinal);

        using (var crossOrgGet = await otherOrgReader.GetAsync($"/api/master-data/material/get?id={material.Id}"))
        {
            var crossOrgGetBody = await crossOrgGet.Content.ReadAsStringAsync();
            Assert.True(crossOrgGet.IsSuccessStatusCode && IsSuccess(crossOrgGetBody), $"跨组织详情请求失败：{crossOrgGetBody}");
            Assert.DoesNotContain(materialCode, crossOrgGetBody, StringComparison.Ordinal);
        }

        using (var deniedContent = new StringContent(JsonSerializer.Serialize(new
        {
            MaterialCode = materialCode + "-DENIED",
            MaterialName = "不应写入",
            MaterialType = "RAW",
            Status = "Enabled"
        }), Encoding.UTF8, "application/json"))
        using (var deniedResponse = await otherOrgReader.PostAsync("/api/master-data/material/add", deniedContent))
        {
            var deniedBody = await deniedResponse.Content.ReadAsStringAsync();
            Assert.False(deniedResponse.IsSuccessStatusCode && IsSuccess(deniedBody),
                $"缺少 Material.Create 权限的用户仍可新增：{deniedBody}");
        }

        // 组织 B 的操作角色拥有更新/删除权限，但 RoleOrg 仍只覆盖组织 B；因此不能越权改删组织 A 数据。
        using var otherOrgOperator = app.CreateClient();
        await LoginAsync(otherOrgOperator, access.OtherOrgOperatorUserName, access.Password);
        using (var crossOrgUpdateContent = new StringContent(JsonSerializer.Serialize(new
        {
            Id = material.Id,
            MaterialCode = materialCode,
            MaterialName = "不应被跨组织修改",
            MaterialType = "RAW",
            Status = "Enabled",
            IsPurchase = true,
            IsProduce = false,
            LeadTime = 0,
            SafetyStock = 0
        }), Encoding.UTF8, "application/json"))
        using (var crossOrgUpdate = await otherOrgOperator.PutAsync("/api/master-data/material/update", crossOrgUpdateContent))
        {
            var crossOrgUpdateBody = await crossOrgUpdate.Content.ReadAsStringAsync();
            Assert.False(crossOrgUpdate.IsSuccessStatusCode && IsSuccess(crossOrgUpdateBody),
                $"组织 B 操作角色越权更新了组织 A 物料：{crossOrgUpdateBody}");
        }
        using (var crossOrgDelete = await otherOrgOperator.DeleteAsync($"/api/master-data/material/delete?id={material.Id}"))
        {
            var crossOrgDeleteBody = await crossOrgDelete.Content.ReadAsStringAsync();
            Assert.True(crossOrgDelete.IsSuccessStatusCode && IsSuccess(crossOrgDeleteBody),
                $"跨组织删除请求未得到受控业务响应：{crossOrgDeleteBody}");
        }
        var unchangedMaterial = mainDb.Ado.QuerySingle<MaterialAcceptanceRecord>(
            "select Id, OwnerId, OwnerOrgId, SourceSystem, SourceId, MaterialName, IsDeleted from md_material where Id = @id",
            new { id = material.Id });
        Assert.NotNull(unchangedMaterial);
        Assert.Equal("P0-1 组织隔离验收物料", unchangedMaterial.MaterialName);
        Assert.False(unchangedMaterial.IsDeleted);

        // 平台管理员走全量数据范围，能够读到组织 A 的物料。
        using var administrator = app.CreateClient();
        await LoginAsync(administrator, access.PlatformAdminUserName, access.Password);
        var adminPage = await PostJsonAsync(administrator, "/api/master-data/material/get-page", new
        {
            CurrentPage = 1,
            PageSize = 20,
            Filter = new { MaterialCode = materialCode }
        });
        Assert.True(IsSuccess(adminPage), $"管理员查询失败：{adminPage}");
        Assert.Contains(materialCode, adminPage, StringComparison.Ordinal);
    }

    private static int CountBusinessRows(IFreeSql db, string recordCode)
        => db.Ado.QuerySingle<int>(
            $"select count(*) from `{BusinessTable}` where RecordCode = @code", new { code = recordCode });

    private static IFreeSql BuildMySql(string connectionString) => new FreeSqlBuilder()
        .UseConnectionString(DataType.MySql, connectionString)
        .UseAutoSyncStructure(false)
        .Build();

    private static WebApplicationFactory<Program> CreateHost() => new HostMySqlAcceptanceFactory();

    /// <summary>
    /// 以 HostMySqlAcceptance 环境启动宿主，使 ConfigCenter 下同后缀的配置文件最后加载并覆盖默认库配置。
    /// </summary>
    private sealed class HostMySqlAcceptanceFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(EnvName);
        }
    }

    private static Task LoginAsync(HttpClient client) => LoginAsync(client, "admin", "123asd");

    private static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        // TestServer 无真实 socket，Connection.RemoteIpAddress 为 null；
        // 生产拓扑中请求来自 SLB / Nginx，故按同样方式提供客户端地址头。
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Real-IP", "127.0.0.1");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AdminCore-HostAcceptance/1.0");

        var response = await PostJsonAsync(client, "/api/admin/auth/login", new
        {
            UserName = userName,
            Password = password
        });

        var token = FindStringProperty(response, "accessToken");
        Assert.False(string.IsNullOrWhiteSpace(token), $"登录未返回访问令牌：{response}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<string> PostJsonAsync(HttpClient client, string path, object input)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(input), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(path, content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path} 返回 {(int)response.StatusCode}：{body}");
        return body;
    }

    private static bool IsSuccess(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            if (TryGetProperty(document.RootElement, "success", out var success))
            {
                return success.ValueKind == JsonValueKind.True;
            }
        }
        catch (JsonException)
        {
            // 非 JSON 响应交由调用方断言内容。
        }

        return true;
    }

    private static string FindStringProperty(string response, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            return Walk(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Walk(JsonElement element, string propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        return property.Value.GetString();
                    }

                    var nested = Walk(property.Value, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = Walk(item, propertyName);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
                break;
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 把宿主配置写到测试输出目录的 ConfigCenter，环境名后缀的文件最后加载，可覆盖 dbconfig.json。
    /// </summary>
    private static void WriteHostConfigs(string mainConnection, string logConnection)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ConfigCenter");
        Directory.CreateDirectory(directory);

        var logDb = new Dictionary<string, object>
        {
            ["key"] = "logdb",
            ["assemblyNames"] = new[] { "ZhonTai.Admin" },
            ["includeEntityDbs"] = new[] { "Log" },
            ["excludeEntityDbs"] = Array.Empty<string>(),
            ["createDb"] = false,
            ["syncStructure"] = true,
            ["syncStructureSql"] = false,
            ["syncData"] = false,
            ["syncUpdateData"] = false,
            ["generateData"] = false,
            ["monitorCommand"] = false,
            ["curd"] = false,
            ["type"] = "MySql",
            ["connectionString"] = logConnection,
            ["idleTime"] = 0
        };

        var dbConfig = new Dictionary<string, object>
        {
            ["key"] = "admindb",
            ["assemblyNames"] = new[] { "ZhonTai.Admin", "ZhonTai.Admin.Tests" },
            ["includeEntityDbs"] = Array.Empty<string>(),
            ["excludeEntityDbs"] = new[] { "Log" },
            ["createDb"] = false,
            ["syncStructure"] = true,
            ["syncStructureSql"] = false,
            ["syncData"] = true,
            ["syncUpdateData"] = false,
            ["generateData"] = false,
            ["monitorCommand"] = false,
            ["curd"] = false,
            ["type"] = "MySql",
            ["connectionString"] = mainConnection,
            ["idleTime"] = 0,
            ["forceUpdate"] = false,
            ["dbs"] = new[] { logDb }
        };

        // 验证码仅在验收进程关闭，不改动生产配置。
        var appConfig = new Dictionary<string, object>
        {
            ["varifyCode"] = new Dictionary<string, object> { ["enable"] = false }
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(directory, $"dbconfig.{EnvName}.json"),
            JsonSerializer.Serialize(new Dictionary<string, object> { ["DbConfig"] = dbConfig }, options));
        File.WriteAllText(
            Path.Combine(directory, $"appconfig.{EnvName}.json"),
            JsonSerializer.Serialize(new Dictionary<string, object> { ["AppConfig"] = appConfig }, options));
    }

    private static async Task<MaterialAcceptanceAccess> SeedMaterialAcceptanceAccessAsync(IFreeSql db, string suffix)
    {
        const string password = "P0-1-MySql-Only";
        var writerOrg = new OrgEntity
        {
            Id = YitIdHelper.NextId(),
            ParentId = 0,
            Name = "验收组织A-" + suffix,
            Code = "ACC-A-" + suffix,
            Value = "ACC-A-" + suffix,
            Enabled = true
        };
        var otherOrg = new OrgEntity
        {
            Id = YitIdHelper.NextId(),
            ParentId = 0,
            Name = "验收组织B-" + suffix,
            Code = "ACC-B-" + suffix,
            Value = "ACC-B-" + suffix,
            Enabled = true
        };
        await db.Insert(writerOrg).ExecuteAffrowsAsync();
        await db.Insert(otherOrg).ExecuteAffrowsAsync();

        var writerRole = new RoleEntity
        {
            Id = YitIdHelper.NextId(),
            ParentId = 0,
            Name = "物料验收写角色-" + suffix,
            Code = "ACC-MD-W-" + suffix,
            Type = RoleType.Role,
            DataScope = DataScope.Custom
        };
        var sameOrgReaderRole = new RoleEntity
        {
            Id = YitIdHelper.NextId(),
            ParentId = 0,
            Name = "物料验收组织A读角色-" + suffix,
            Code = "ACC-MD-RA-" + suffix,
            Type = RoleType.Role,
            DataScope = DataScope.Custom
        };
        var otherOrgReaderRole = new RoleEntity
        {
            Id = YitIdHelper.NextId(),
            ParentId = 0,
            Name = "物料验收组织B读角色-" + suffix,
            Code = "ACC-MD-RB-" + suffix,
            Type = RoleType.Role,
            DataScope = DataScope.Custom
        };
        var otherOrgOperatorRole = new RoleEntity
        {
            Id = YitIdHelper.NextId(),
            ParentId = 0,
            Name = "物料验收组织B操作角色-" + suffix,
            Code = "ACC-MD-OB-" + suffix,
            Type = RoleType.Role,
            DataScope = DataScope.Custom
        };
        await db.Insert(new[] { writerRole, sameOrgReaderRole, otherOrgReaderRole, otherOrgOperatorRole }).ExecuteAffrowsAsync();
        await db.Insert(new[]
        {
            new RoleOrgEntity { RoleId = writerRole.Id, OrgId = writerOrg.Id },
            new RoleOrgEntity { RoleId = sameOrgReaderRole.Id, OrgId = writerOrg.Id },
            new RoleOrgEntity { RoleId = otherOrgReaderRole.Id, OrgId = otherOrg.Id },
            new RoleOrgEntity { RoleId = otherOrgOperatorRole.Id, OrgId = otherOrg.Id }
        }).ExecuteAffrowsAsync();

        var writer = CreateAcceptanceUser("acc-md-w-" + suffix, writerOrg.Id, password);
        var sameOrgReader = CreateAcceptanceUser("acc-md-r-a-" + suffix, writerOrg.Id, password);
        var otherOrgReader = CreateAcceptanceUser("acc-md-r-b-" + suffix, otherOrg.Id, password);
        var otherOrgOperator = CreateAcceptanceUser("acc-md-o-b-" + suffix, otherOrg.Id, password);
        var platformAdmin = CreateAcceptanceUser("acc-md-admin-" + suffix, writerOrg.Id, password, UserType.PlatformAdmin);
        await db.Insert(new[] { writer, sameOrgReader, otherOrgReader, otherOrgOperator, platformAdmin }).ExecuteAffrowsAsync();
        await db.Insert(new[]
        {
            new UserOrgEntity { UserId = writer.Id, OrgId = writerOrg.Id },
            new UserOrgEntity { UserId = sameOrgReader.Id, OrgId = writerOrg.Id },
            new UserOrgEntity { UserId = otherOrgReader.Id, OrgId = otherOrg.Id },
            new UserOrgEntity { UserId = otherOrgOperator.Id, OrgId = otherOrg.Id },
            new UserOrgEntity { UserId = platformAdmin.Id, OrgId = writerOrg.Id }
        }).ExecuteAffrowsAsync();
        await db.Insert(new[]
        {
            new UserRoleEntity { UserId = writer.Id, RoleId = writerRole.Id },
            new UserRoleEntity { UserId = sameOrgReader.Id, RoleId = sameOrgReaderRole.Id },
            new UserRoleEntity { UserId = otherOrgReader.Id, RoleId = otherOrgReaderRole.Id },
            new UserRoleEntity { UserId = otherOrgOperator.Id, RoleId = otherOrgOperatorRole.Id }
        }).ExecuteAffrowsAsync();

        const long materialQueryPermissionId = 880000000000001;
        const long materialCreatePermissionId = 880000000000002;
        const long materialUpdatePermissionId = 880000000000003;
        const long materialDeletePermissionId = 880000000000004;
        await db.Insert(new[]
        {
            new RolePermissionEntity { Platform = "admin", RoleId = writerRole.Id, PermissionId = materialQueryPermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = writerRole.Id, PermissionId = materialCreatePermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = writerRole.Id, PermissionId = materialUpdatePermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = writerRole.Id, PermissionId = materialDeletePermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = sameOrgReaderRole.Id, PermissionId = materialQueryPermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = otherOrgReaderRole.Id, PermissionId = materialQueryPermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = otherOrgOperatorRole.Id, PermissionId = materialQueryPermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = otherOrgOperatorRole.Id, PermissionId = materialUpdatePermissionId },
            new RolePermissionEntity { Platform = "admin", RoleId = otherOrgOperatorRole.Id, PermissionId = materialDeletePermissionId }
        }).ExecuteAffrowsAsync();

        return new MaterialAcceptanceAccess(
            password,
            writer.UserName,
            sameOrgReader.UserName,
            otherOrgReader.UserName,
            otherOrgOperator.UserName,
            platformAdmin.UserName,
            writer.Id,
            writerOrg.Id);
    }

    private static UserEntity CreateAcceptanceUser(string userName, long orgId, string password, UserType type = UserType.DefaultUser) => new()
    {
        Id = YitIdHelper.NextId(),
        UserName = userName,
        Password = MD5Encrypt.Encrypt32(password),
        PasswordEncryptType = PasswordEncryptType.MD5Encrypt32,
        Name = userName,
        NickName = userName,
        OrgId = orgId,
        Type = type,
        Enabled = true
    };

    private sealed record MaterialAcceptanceAccess(
        string Password,
        string WriterUserName,
        string SameOrgReaderUserName,
        string OtherOrgReaderUserName,
        string OtherOrgOperatorUserName,
        string PlatformAdminUserName,
        long WriterUserId,
        long WriterOrgId);

    /// <summary>
    /// 验收测试的数据库观察模型；不是 MasterData 的公共业务实体或跨模块 Contract。
    /// </summary>
    private sealed class MaterialAcceptanceRecord
    {
        public long Id { get; init; }
        public long OwnerId { get; init; }
        public long OwnerOrgId { get; init; }
        public string SourceSystem { get; init; } = string.Empty;
        public string SourceId { get; init; } = string.Empty;
        public string MaterialName { get; init; } = string.Empty;
        public bool IsDeleted { get; init; }
    }
}

/// <summary>
/// 宿主级验收夹具表，按 P0 §18.1 的扩展约束建模：
/// EntityData 提供组织归属与数据权限，SourceSystem / SourceId 提供来源追溯。
/// </summary>
[Table(Name = "acc_host_business_record")]
[Index("idx_acc_host_business_record_code", nameof(RecordCode), true)]
public sealed class AccHostBusinessRecord : EntityData
{
    [Column(StringLength = 50)]
    public string RecordCode { get; set; } = string.Empty;

    [Column(StringLength = 200)]
    public string RecordName { get; set; } = string.Empty;

    [Column(StringLength = 50)]
    public string SourceSystem { get; set; } = string.Empty;

    [Column(StringLength = 100)]
    public string SourceId { get; set; } = string.Empty;
}

public sealed class HostMySqlAcceptanceFactAttribute : FactAttribute
{
    public HostMySqlAcceptanceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMINCORE_HOST_MYSQL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMINCORE_HOST_MYSQL_LOG")))
        {
            Skip = "Set ADMINCORE_HOST_MYSQL / ADMINCORE_HOST_MYSQL_LOG to dedicated MySQL 8 acceptance schemas.";
        }
    }
}
