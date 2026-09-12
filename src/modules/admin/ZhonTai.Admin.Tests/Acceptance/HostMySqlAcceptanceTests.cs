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
    private const string BusinessTable = "acc_host_material";

    [HostMySqlAcceptanceFact]
    public async Task FullHostRunsOnMySql8AndBusinessDataSurvivesRestart()
    {
        var mainConnection = Environment.GetEnvironmentVariable(MainConnectionVariable)!;
        var logConnection = Environment.GetEnvironmentVariable(LogConnectionVariable)!;
        WriteHostConfigs(mainConnection, logConnection);

        var materialCode = "ACC-MAT-" + Guid.NewGuid().ToString("N")[..12];
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
                    unitOfWork.Orm.Insert(new AccHostMaterial
                    {
                        MaterialCode = materialCode + "-ROLLBACK",
                        MaterialName = "回滚用例",
                        SourceSystem = "ETS",
                        SourceId = "SRC-ROLLBACK"
                    }).ExecuteAffrows();
                    unitOfWork.Rollback();
                }

                Assert.Equal(0, CountBusinessRows(mainDb, materialCode + "-ROLLBACK"));

                using (var unitOfWork = manager.Begin(DbKeys.AdminDb))
                {
                    unitOfWork.Orm.Insert(new AccHostMaterial
                    {
                        MaterialCode = materialCode,
                        MaterialName = "提交用例",
                        SourceSystem = "ETS",
                        SourceId = "SRC-001"
                    }).ExecuteAffrows();
                    unitOfWork.Commit();
                }

                Assert.Equal(1, CountBusinessRows(mainDb, materialCode));
            }
        }

        // ---------- 阶段 2：重启宿主的同一发布物 ----------
        using (var restarted = CreateHost())
        {
            var cloud = restarted.Services.GetRequiredService<FreeSqlCloud>();
            var mainDb = cloud.Use(DbKeys.AdminDb);

            // 10. 重启后结构与数据仍在，且结构同步可重复执行
            Assert.Equal(1, CountBusinessRows(mainDb, materialCode));
            Assert.StartsWith("8.", mainDb.Ado.QuerySingle<string>("select version()"));
            Assert.Equal(1, mainDb.Ado.QuerySingle<int>(
                "select count(*) from base_api where path = @path", new { path = apiPath }));

            // 11. 重启后仍可正常登录（种子与权限数据未被破坏）
            using var client = restarted.CreateClient();
            await LoginAsync(client);
        }
    }

    private static int CountBusinessRows(IFreeSql db, string materialCode)
        => db.Ado.QuerySingle<int>(
            $"select count(*) from `{BusinessTable}` where MaterialCode = @code", new { code = materialCode });

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

    private static async Task LoginAsync(HttpClient client)
    {
        // TestServer 无真实 socket，Connection.RemoteIpAddress 为 null；
        // 生产拓扑中请求来自 SLB / Nginx，故按同样方式提供客户端地址头。
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Real-IP", "127.0.0.1");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AdminCore-HostAcceptance/1.0");

        var response = await PostJsonAsync(client, "/api/admin/auth/login", new
        {
            UserName = "admin",
            Password = "123asd"
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
}

/// <summary>
/// 宿主级验收业务表，按 P0 §18.1 的扩展约束建模：
/// EntityData 提供组织归属与数据权限，SourceSystem / SourceId 提供来源追溯。
/// </summary>
[Table(Name = "acc_host_material")]
[Index("idx_acc_host_material_code", nameof(MaterialCode), true)]
public sealed class AccHostMaterial : EntityData
{
    [Column(StringLength = 50)]
    public string MaterialCode { get; set; } = string.Empty;

    [Column(StringLength = 200)]
    public string MaterialName { get; set; } = string.Empty;

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
