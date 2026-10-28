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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Yitter.IdGenerator;
using ZhonTai.Admin.Core.Consts;
using ZhonTai.Admin.Core.Exceptions;
using ZhonTai.Admin.Core.Middlewares;
using ZhonTai.Admin.Domain;
using ZhonTai.Admin.Domain.Api;
using ZhonTai.Admin.Domain.Org;
using ZhonTai.Admin.Domain.Permission;
using ZhonTai.Admin.Domain.PermissionApi;
using ZhonTai.Admin.Domain.Role;
using ZhonTai.Admin.Domain.RoleOrg;
using ZhonTai.Admin.Domain.RolePermission;
using ZhonTai.Admin.Domain.User;
using ZhonTai.Admin.Domain.UserOrg;
using ZhonTai.Admin.Domain.UserRole;
using ZhonTai.Common.Helpers;
using Xunit;

namespace ZhonTai.Admin.Tests.Acceptance;

/// <summary>
/// T3: 只针对隔离 MySQL 验收库的真实 HTTP/JWT/RBAC/组织数据权限验证。
/// 不复用或恢复 Material 的正式业务入口。
/// </summary>
[Collection("P0T4MySql")]
public sealed class P0DataPermissionAcceptanceTests
{
    private const string EnvName = "P0DataPermissionAcceptance";
    private const string MainConnectionVariable = "ADMINCORE_HOST_MYSQL";
    private const string LogConnectionVariable = "ADMINCORE_HOST_MYSQL_LOG";
    private const string FixtureSwitch = "ADMINCORE_ENABLE_P0_ACCEPTANCE_FIXTURES";
    private const string FixtureTable = "acc_p0_data_permission_fixture";
    private const string ApiPrefix = "/api/admin/p0-data-permission-fixture";

    [P0DataPermissionAcceptanceFact]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public async Task LogoutWithAPreRotationCredentialInvalidatesTheRotatedJwtButPreservesAnotherLogin()
    {
        var mainConnection = P0MySqlAcceptanceEnvironment.RequireConnection(MainConnectionVariable);
        var logConnection = P0MySqlAcceptanceEnvironment.RequireConnection(LogConnectionVariable);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        using var cleanup = AcceptanceFixtureCleanup.ForMySql(mainConnection);
        cleanup.Register(db => CleanupP0Fixtures(db, suffix));
        WriteHostConfigs(mainConnection, logConnection);
        using var app = CreateHost();
        var db = app.Services.GetRequiredService<FreeSqlCloud>().Use(DbKeys.AdminDb);
        var access = await SeedAccessAsync(db, suffix);
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Real-IP", "127.0.0.1");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AdminCore-AuthSessionAcceptance/1.0");
        var login = await PostAsync(client, "/api/admin/auth/login", new { UserName = access.WriterUserName, Password = access.Password });
        var originalRefresh = FindStringProperty(login, "refreshToken");
        Assert.False(string.IsNullOrWhiteSpace(originalRefresh));

        var refresh = await PostAsync(client, "/api/admin/auth/refresh", new { RefreshToken = originalRefresh });
        var rotatedAccess = FindStringProperty(refresh, "accessToken");
        var rotatedRefresh = FindStringProperty(refresh, "refreshToken");
        Assert.False(string.IsNullOrWhiteSpace(rotatedAccess));
        Assert.False(string.IsNullOrWhiteSpace(rotatedRefresh));
        Assert.NotEqual(originalRefresh, rotatedRefresh);
        using var protectedClient = app.CreateClient();
        protectedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccess);
        using (var before = await protectedClient.GetAsync("/api/admin/auth/get-user-profile"))
            Assert.Equal(System.Net.HttpStatusCode.OK, before.StatusCode);

        using var otherLogin = app.CreateClient();
        await LoginAsync(otherLogin, access.WriterUserName, access.Password);

        // This is the credential captured by logout before a concurrent refresh returned.
        Assert.True(IsSuccess(await PostAsync(client, "/api/admin/auth/logout", new { RefreshToken = originalRefresh })));
        Assert.True(IsSuccess(await PostAsync(client, "/api/admin/auth/logout", new { RefreshToken = originalRefresh })));
        using (var after = await protectedClient.GetAsync("/api/admin/auth/get-user-profile"))
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.False(IsSuccess(await PostAsync(client, "/api/admin/auth/refresh", new { RefreshToken = rotatedRefresh })));
        using var unaffected = await otherLogin.GetAsync("/api/admin/auth/get-user-profile");
        Assert.Equal(System.Net.HttpStatusCode.OK, unaffected.StatusCode);
    }

    [P0DataPermissionAcceptanceFact]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public async Task DP01_to_DP06_RealJwtRbacAndOrgDataPermissionAreEnforced()
    {
        var mainConnection = P0MySqlAcceptanceEnvironment.RequireConnection(MainConnectionVariable);
        var logConnection = P0MySqlAcceptanceEnvironment.RequireConnection(LogConnectionVariable);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        using var cleanup = AcceptanceFixtureCleanup.ForMySql(mainConnection);
        cleanup.Register(db => CleanupP0Fixtures(db, suffix));
        WriteHostConfigs(mainConnection, logConnection);

        using var app = CreateHost();
        var cloud = app.Services.GetRequiredService<FreeSqlCloud>();
        var db = cloud.Use(DbKeys.AdminDb);

        // 真实 Host 启动后，技术 API 的路由、实体表和权限关联均在隔离库可见。
        Assert.Equal(1, db.Ado.QuerySingle<int>(
            "select count(*) from information_schema.tables where table_schema = database() and table_name = @table", new { table = FixtureTable }));

        var access = await SeedAccessAsync(db, suffix);
        Assert.True(db.Ado.QuerySingle<int>(
            "select count(*) from base_permission_api pa inner join base_api a on a.Id = pa.ApiId where a.path like @prefix", new { prefix = ApiPrefix + "/%" }) >= 6);
        Assert.Equal(6, db.Ado.QuerySingle<int>(
            "select count(*) from base_role_permission where RoleId = @roleId", new { roleId = access.WriterRoleId }));

        var code = "DP-" + suffix;
        long recordId;
        using (var writer = app.CreateClient())
        {
            await LoginAsync(writer, access.WriterUserName, access.Password);
            var add = await PostAsync(writer, ApiPrefix + "/add", new { RecordCode = code, RecordName = "Org A source" });
            Assert.True(IsSuccess(add), "DP setup add failed: " + add);
            recordId = ReadLongProperty(add, "data");
        }

        var created = db.Ado.QuerySingle<P0FixtureRecord>(
            "select Id, OwnerId, OwnerOrgId, RecordCode, RecordName, IsDeleted from acc_p0_data_permission_fixture where Id = @id", new { id = recordId });
        Assert.NotNull(created);
        Assert.Equal(access.WriterUserId, created.OwnerId);
        Assert.Equal(access.OrgAId, created.OwnerOrgId);

        // 为 DP-04/05 先建立 Org B 数据，证明操作角色的更新权限和 Org B 数据范围本身有效。
        long orgBRecordId;
        using (var orgBWriter = app.CreateClient())
        {
            await LoginAsync(orgBWriter, access.OtherOrgWriterUserName, access.Password);
            var add = await PostAsync(orgBWriter, ApiPrefix + "/add", new { RecordCode = "ORG-B-" + suffix, RecordName = "Org B source" });
            Assert.True(IsSuccess(add), "Org B setup add failed: " + add);
            orgBRecordId = ReadLongProperty(add, "data");
        }

        // DP-01：同一组织、明确 RoleOrg.Custom 授权的另一个普通用户可见。
        using (var sameOrgReader = app.CreateClient())
        {
            await LoginAsync(sameOrgReader, access.SameOrgReaderUserName, access.Password);
            var permission = await GetAsync(sameOrgReader, ApiPrefix + "/get-data-permission");
            Assert.True(IsSuccess(permission), "DP-01 permission lookup failed: " + permission);
            Assert.Contains(access.OrgAId.ToString(), permission, StringComparison.Ordinal);

            var sameOrgList = await PostAsync(sameOrgReader, ApiPrefix + "/get-list", new { });
            Assert.True(IsSuccess(sameOrgList), "DP-01 list failed: " + sameOrgList);
            Assert.Contains(code, sameOrgList, StringComparison.Ordinal);
        }

        // DP-02：具有读取权限、但 Custom RoleOrg 仅为 Org B 的普通用户看不到 Org A 记录。
        using (var otherOrgReader = app.CreateClient())
        {
            await LoginAsync(otherOrgReader, access.OtherOrgReaderUserName, access.Password);
            var permission = await GetAsync(otherOrgReader, ApiPrefix + "/get-data-permission");
            Assert.True(IsSuccess(permission), "DP-02 permission lookup failed: " + permission);
            Assert.Contains(access.OrgBId.ToString(), permission, StringComparison.Ordinal);
            Assert.DoesNotContain(access.OrgAId.ToString(), permission, StringComparison.Ordinal);

            var otherOrgList = await PostAsync(otherOrgReader, ApiPrefix + "/get-list", new { });
            Assert.True(IsSuccess(otherOrgList), "DP-02 list request failed: " + otherOrgList);
            Assert.DoesNotContain(code, otherOrgList, StringComparison.Ordinal);

            // DP-03：按 Id 查询同样不能取得目标实体。
            var crossOrgGet = await GetAsync(otherOrgReader, ApiPrefix + "/get?id=" + recordId);
            Assert.True(IsSuccess(crossOrgGet), "DP-03 get request failed: " + crossOrgGet);
            Assert.DoesNotContain(code, crossOrgGet, StringComparison.Ordinal);
        }

        // DP-04 / DP-05：Org B 操作角色具有对应 API 权限，拦截必须来自数据范围；以数据库状态为准。
        using (var otherOrgOperator = app.CreateClient())
        {
            await LoginAsync(otherOrgOperator, access.OtherOrgOperatorUserName, access.Password);

            var allowedUpdate = await PutAsync(otherOrgOperator, ApiPrefix + "/update", new
            {
                Id = orgBRecordId,
                RecordCode = "ORG-B-" + suffix,
                RecordName = "Org B updated"
            });
            Assert.True(ReadBooleanData(allowedUpdate), "Org B update unexpectedly failed: " + allowedUpdate);
            Assert.Equal("Org B updated", ReadRecord(db, orgBRecordId).RecordName);

            var update = await PutAsync(otherOrgOperator, ApiPrefix + "/update", new
            {
                Id = recordId,
                RecordCode = code,
                RecordName = "cross-org update must fail"
            });
            Assert.False(ReadBooleanData(update), "DP-04 cross-org update unexpectedly succeeded: " + update);
            Assert.Equal("Org A source", ReadRecord(db, recordId).RecordName);

            var delete = await DeleteAsync(otherOrgOperator, ApiPrefix + "/delete?id=" + recordId);
            Assert.False(ReadBooleanData(delete), "DP-05 cross-org delete unexpectedly succeeded: " + delete);
            var afterDelete = ReadRecord(db, recordId);
            Assert.False(afterDelete.IsDeleted);
            Assert.Equal("Org A source", afterDelete.RecordName);
        }

        // DP-06：平台管理员以 All 数据范围可读取 Org A 技术记录。
        using (var platformAdmin = app.CreateClient())
        {
            await LoginAsync(platformAdmin, access.PlatformAdminUserName, access.Password);
            var permission = await GetAsync(platformAdmin, ApiPrefix + "/get-data-permission");
            Assert.True(IsSuccess(permission), "DP-06 permission lookup failed: " + permission);
            Assert.Equal((int)DataScope.All, ReadDataScope(permission));

            var adminList = await PostAsync(platformAdmin, ApiPrefix + "/get-list", new { });
            Assert.True(IsSuccess(adminList), "DP-06 list failed: " + adminList);
            Assert.Contains(code, adminList, StringComparison.Ordinal);
        }
    }

    [P0T5AcceptanceFact]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public async Task LOG01_to_LOG08_RealHttpTraceErrorsAndStructuredLogsAreEnforced()
    {
        var connection = P0MySqlAcceptanceEnvironment.RequireConnection("ADMINCORE_T4_MYSQL");
        var suffix = Guid.NewGuid().ToString("N")[..12];
        using var cleanup = AcceptanceFixtureCleanup.ForMySql(connection);
        cleanup.Register(db => CleanupP0Fixtures(db, suffix));
        WriteHostConfigs(connection, connection);
        var logs = new P0TestLoggerProvider();
        using var app = CreateHost(logs);
        var db = app.Services.GetRequiredService<FreeSqlCloud>().Use(DbKeys.AdminDb);
        var access = await SeedAccessAsync(db, suffix);

        using (var platformProbe = app.CreateClient())
        {
            using var health = await platformProbe.GetAsync("/health");
            Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
            using var swagger = await platformProbe.GetAsync("/doc/admin/swagger/admin/swagger.json");
            Assert.Equal(System.Net.HttpStatusCode.OK, swagger.StatusCode);
            Assert.Contains("openapi", await swagger.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        using var writer = app.CreateClient();
        await LoginAsync(writer, access.WriterUserName, access.Password);

        // LOG-01/03: a supplied valid trace id is stable across header, envelope and log scope.
        var validationTrace = "T5Validation" + suffix;
        using var validation = await SendResponseAsync(writer, HttpMethod.Post, ApiPrefix + "/add", new { }, validationTrace);
        var validationBody = await validation.Content.ReadAsStringAsync();
        Assert.Equal(validationTrace, validation.Headers.GetValues(P0RequestContextMiddleware.TraceIdHeader).Single());
        AssertEnvelope(validationBody, P0ErrorCodes.Validation, validationTrace);
        Assert.Contains(logs.Records, record => record.Properties.TryGetValue("TraceId", out var value) && Equals(value?.ToString(), validationTrace));

        // LOG-04: a real authenticated user without Add permission is rejected by RBAC.
        using var reader = app.CreateClient();
        await LoginAsync(reader, access.SameOrgReaderUserName, access.Password);
        var forbiddenTrace = "T5Forbidden" + suffix;
        using var forbidden = await SendResponseAsync(reader, HttpMethod.Post, ApiPrefix + "/add", new { RecordCode = "DENY-" + suffix, RecordName = "denied" }, forbiddenTrace);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, forbidden.StatusCode);
        AssertEnvelope(await forbidden.Content.ReadAsStringAsync(), P0ErrorCodes.Forbidden, forbiddenTrace);

        // LOG-05: an unmapped route uses the common error envelope.
        var notFoundTrace = "T5NotFound" + suffix;
        using var notFound = await SendResponseAsync(writer, HttpMethod.Get, "/api/admin/p0-route-does-not-exist", null, notFoundTrace);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, notFound.StatusCode);
        AssertEnvelope(await notFound.Content.ReadAsStringAsync(), P0ErrorCodes.NotFound, notFoundTrace);

        // LOG-06: a real MySQL unique-key violation is mapped to Conflict.
        var conflictCode = "T5-CONFLICT-" + suffix;
        using var firstInsert = await SendResponseAsync(writer, HttpMethod.Post, ApiPrefix + "/add", new { RecordCode = conflictCode, RecordName = "first" }, null);
        Assert.True(firstInsert.IsSuccessStatusCode, await firstInsert.Content.ReadAsStringAsync());
        var conflictTrace = "T5Conflict" + suffix;
        using var conflict = await SendResponseAsync(writer, HttpMethod.Post, ApiPrefix + "/add", new { RecordCode = conflictCode, RecordName = "duplicate" }, conflictTrace);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await conflict.Content.ReadAsStringAsync();
        AssertEnvelope(conflictBody, P0ErrorCodes.Conflict, conflictTrace);
        Assert.DoesNotContain("MySql", conflictBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", conflictBody, StringComparison.OrdinalIgnoreCase);

        // LOG-02/07: unexpected failures have a safe response and a correlated server error.
        using var admin = app.CreateClient();
        await LoginAsync(admin, access.PlatformAdminUserName, access.Password);
        var unexpectedTrace = "T5Unexpected" + suffix;
        using var unexpected = await SendResponseAsync(admin, HttpMethod.Post, ApiPrefix + "/throw-unexpected", new { }, unexpectedTrace);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, unexpected.StatusCode);
        var unexpectedBody = await unexpected.Content.ReadAsStringAsync();
        AssertEnvelope(unexpectedBody, P0ErrorCodes.Unexpected, unexpectedTrace);
        Assert.DoesNotContain(nameof(InvalidOperationException), unexpectedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", unexpectedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logs.Records, record => record.Level == LogLevel.Error
            && record.Properties.TryGetValue("TraceId", out var value)
            && Equals(value?.ToString(), unexpectedTrace)
            && record.Exception is InvalidOperationException);

        // LOG-08: a real authenticated request contributes trusted user/org context.
        var contextTrace = "T5Context" + suffix;
        using var contextResponse = await SendResponseAsync(writer, HttpMethod.Get, ApiPrefix + "/get-data-permission", null, contextTrace);
        Assert.True(contextResponse.IsSuccessStatusCode, await contextResponse.Content.ReadAsStringAsync());
        Assert.Contains(logs.Records, record => record.Properties.TryGetValue("TraceId", out var trace) && Equals(trace?.ToString(), contextTrace)
            && record.Properties.TryGetValue("UserId", out var userId) && Equals(userId?.ToString(), access.WriterUserId.ToString())
            && record.Properties.TryGetValue("OrgId", out var orgId) && Equals(orgId?.ToString(), access.OrgAId.ToString()));

        // Invalid external ids are rejected rather than copied into logs/responses.
        using var invalidTrace = await SendResponseAsync(writer, HttpMethod.Get, ApiPrefix + "/get-data-permission", null, "invalid trace id!");
        var generated = invalidTrace.Headers.GetValues(P0RequestContextMiddleware.TraceIdHeader).Single();
        Assert.NotEqual("invalid trace id!", generated);
        Assert.Matches("^[A-Za-z0-9]{1,128}$", generated);

    }

    internal static void CleanupP0Fixtures(IFreeSql db, string suffix)
    {
        if (string.IsNullOrEmpty(suffix) || suffix.Any(character => !char.IsAsciiLetterOrDigit(character)))
            throw new ArgumentException("A literal acceptance run suffix is required.", nameof(suffix));
        // Host initialization may fail before creating the framework tables. Seeding cannot have begun then.
        if (new[] { "base_user", "base_role", "base_org", "base_permission" }.Any(table => !db.DbFirst.ExistsTable(table)))
            return;
        var parameters = new
        {
            userPattern = "p0-dp-%-" + suffix,
            rolePattern = "P0-DP-%-" + suffix,
            permissionPattern = "P0.DataPermission.%." + suffix
        };
        var users = db.Ado.Query<long>("select Id from base_user where UserName like @userPattern", parameters);
        var roles = db.Ado.Query<long>("select Id from base_role where Code like @rolePattern", parameters);
        var orgs = db.Ado.Query<long>("select Id from base_org where Code like @rolePattern", parameters);
        var permissions = db.Ado.Query<long>("select Id from base_permission where Code like @permissionPattern", parameters);
        static string In(string column, List<long> ids) => ids.Count == 0 ? "1=0" : $"{column} in ({string.Join(',', ids)})";
        void Delete(string table, string predicate)
        {
            if (db.DbFirst.ExistsTable(table)) db.Ado.ExecuteNonQuery($"delete from `{table}` where {predicate}");
        }
        void Protect(string table, string predicate)
        {
            if (db.DbFirst.ExistsTable(table) && db.Ado.QuerySingle<int>($"select count(*) from `{table}` where {predicate}") > 0)
                throw new InvalidOperationException($"Normal records in {table} reference this acceptance run; cleanup refused.");
        }
        db.Ado.Transaction(() =>
        {
            Protect("base_user", $"not ({In("Id", users)}) and ({In("OrgId", orgs)} or {In("ManagerUserId", users)})");
            Protect("base_tenant", In("UserId", users) + " or " + In("OrgId", orgs));
            Protect("base_org", $"not ({In("Id", orgs)}) and ({In("ParentId", orgs)})");
            Protect("base_role", $"not ({In("Id", roles)}) and ({In("ParentId", roles)})");
            Protect("base_permission", $"not ({In("Id", permissions)}) and ({In("ParentId", permissions)})");
            Protect("base_user_role", $"not ({In("UserId", users)}) and ({In("RoleId", roles)})");
            Protect("base_user_org", $"not ({In("UserId", users)}) and ({In("OrgId", orgs)})");
            Protect("base_role_org", $"not ({In("RoleId", roles)}) and ({In("OrgId", orgs)})");
            Protect("base_role_permission", $"not ({In("RoleId", roles)}) and ({In("PermissionId", permissions)})");
            Protect("base_pkg_permission", In("PermissionId", permissions));
            Delete("base_role_permission", In("RoleId", roles) + " or " + In("PermissionId", permissions));
            Delete("base_permission_api", In("PermissionId", permissions));
            Delete("base_user_role", In("UserId", users) + " or " + In("RoleId", roles));
            Delete("base_user_org", In("UserId", users) + " or " + In("OrgId", orgs));
            Delete("base_role_org", In("RoleId", roles) + " or " + In("OrgId", orgs));
            Delete("base_user_staff", In("Id", users));
            Delete("base_pkg_permission", In("PermissionId", permissions));
            Delete("base_msg_user", In("UserId", users));
            Delete("acc_p0_data_permission_fixture", In("OwnerId", users));
            Delete("base_user", In("Id", users));
            Delete("base_role", In("Id", roles));
            Delete("base_permission", In("Id", permissions));
            Delete("base_org", In("Id", orgs));
        });
    }

    private static P0FixtureRecord ReadRecord(IFreeSql db, long id) => db.Ado.QuerySingle<P0FixtureRecord>(
        "select Id, OwnerId, OwnerOrgId, RecordCode, RecordName, IsDeleted from acc_p0_data_permission_fixture where Id = @id", new { id });

    private static WebApplicationFactory<Program> CreateHost(P0TestLoggerProvider logger = null) => new P0DataPermissionAcceptanceFactory(logger);

    private sealed class P0DataPermissionAcceptanceFactory : WebApplicationFactory<Program>
    {
        private readonly P0TestLoggerProvider _logger;
        private string _originalSwitch;
        private string _originalConfigCenterPath;

        public P0DataPermissionAcceptanceFactory(P0TestLoggerProvider logger = null) => _logger = logger;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            _originalSwitch = Environment.GetEnvironmentVariable(FixtureSwitch);
            _originalConfigCenterPath = Environment.GetEnvironmentVariable("ADMINCORE_CONFIGCENTER_PATH");
            Environment.SetEnvironmentVariable("ADMINCORE_CONFIGCENTER_PATH", Path.Combine(AppContext.BaseDirectory, "ConfigCenter"));
            Environment.SetEnvironmentVariable(FixtureSwitch, "true");
            builder.UseEnvironment(EnvName);
            if (_logger is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(_logger));
            }
        }

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally
            {
                Environment.SetEnvironmentVariable(FixtureSwitch, _originalSwitch);
                Environment.SetEnvironmentVariable("ADMINCORE_CONFIGCENTER_PATH", _originalConfigCenterPath);
            }
        }
    }

    private static async Task<HttpResponseMessage> SendResponseAsync(HttpClient client, HttpMethod method, string path, object body, string traceId)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(traceId)) request.Headers.TryAddWithoutValidation(P0RequestContextMiddleware.TraceIdHeader, traceId);
        return await client.SendAsync(request);
    }

    private static void AssertEnvelope(string body, string code, string traceId)
    {
        using var document = JsonDocument.Parse(body);
        Assert.True(TryGetProperty(document.RootElement, "code", out var actualCode), body);
        Assert.Equal(code, actualCode.GetString());
        Assert.True(TryGetProperty(document.RootElement, "traceId", out var actualTrace), body);
        Assert.Equal(traceId, actualTrace.GetString());
        Assert.True(TryGetProperty(document.RootElement, "success", out var success), body);
        Assert.False(success.GetBoolean());
    }

    private static async Task LoginAsync(HttpClient client, string userName, string password)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Real-IP", "127.0.0.1");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "AdminCore-P0DataPermissionAcceptance/1.0");
        var response = await PostAsync(client, "/api/admin/auth/login", new { UserName = userName, Password = password });
        var token = FindStringProperty(response, "accessToken");
        Assert.False(string.IsNullOrWhiteSpace(token), "login did not return JWT: " + response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<string> PostAsync(HttpClient client, string path, object body) => await SendAsync(client, HttpMethod.Post, path, body);
    private static async Task<string> PutAsync(HttpClient client, string path, object body) => await SendAsync(client, HttpMethod.Put, path, body);
    private static async Task<string> GetAsync(HttpClient client, string path) => await SendAsync(client, HttpMethod.Get, path, null);
    private static async Task<string> DeleteAsync(HttpClient client, string path) => await SendAsync(client, HttpMethod.Delete, path, null);

    private static async Task<string> SendAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        return responseBody;
    }

    private static bool IsSuccess(string response)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            return !TryGetProperty(document.RootElement, "success", out var success) || success.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static long ReadLongProperty(string response, string propertyName)
    {
        using var document = JsonDocument.Parse(response);
        Assert.True(TryGetProperty(document.RootElement, propertyName, out var value), "response did not contain " + propertyName + ": " + response);
        return value.GetInt64();
    }

    private static bool ReadBooleanData(string response)
    {
        using var document = JsonDocument.Parse(response);
        Assert.True(TryGetProperty(document.RootElement, "data", out var value), "response did not contain data: " + response);
        return value.ValueKind == JsonValueKind.True;
    }

    private static int ReadDataScope(string response)
    {
        using var document = JsonDocument.Parse(response);
        Assert.True(TryGetProperty(document.RootElement, "data", out var data), "response did not contain data: " + response);
        Assert.True(TryGetProperty(data, "dataScope", out var value), "data did not contain dataScope: " + response);
        return value.GetInt32();
    }

    private static string FindStringProperty(string response, string propertyName)
    {
        using var document = JsonDocument.Parse(response);
        return FindStringProperty(document.RootElement, propertyName);
    }

    private static string FindStringProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindStringProperty(property.Value, propertyName);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindStringProperty(item, propertyName);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
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

    private static async Task<P0Access> SeedAccessAsync(IFreeSql db, string suffix)
    {
        const string password = "P0-DP-MySql-Only";
        var orgA = NewOrg("P0-DP-A-" + suffix);
        var orgB = NewOrg("P0-DP-B-" + suffix);
        await db.Insert(new[] { orgA, orgB }).ExecuteAffrowsAsync();

        var writerRole = NewRole("P0-DP-W-" + suffix);
        var sameOrgReaderRole = NewRole("P0-DP-RA-" + suffix);
        var otherOrgReaderRole = NewRole("P0-DP-RB-" + suffix);
        var otherOrgOperatorRole = NewRole("P0-DP-OB-" + suffix);
        await db.Insert(new[] { writerRole, sameOrgReaderRole, otherOrgReaderRole, otherOrgOperatorRole }).ExecuteAffrowsAsync();
        await db.Insert(new[]
        {
            new RoleOrgEntity { RoleId = writerRole.Id, OrgId = orgA.Id },
            new RoleOrgEntity { RoleId = sameOrgReaderRole.Id, OrgId = orgA.Id },
            new RoleOrgEntity { RoleId = otherOrgReaderRole.Id, OrgId = orgB.Id },
            new RoleOrgEntity { RoleId = otherOrgOperatorRole.Id, OrgId = orgB.Id }
        }).ExecuteAffrowsAsync();

        var writer = NewUser("p0-dp-w-" + suffix, orgA.Id, password);
        var sameOrgReader = NewUser("p0-dp-ra-" + suffix, orgA.Id, password);
        var otherOrgReader = NewUser("p0-dp-rb-" + suffix, orgB.Id, password);
        var otherOrgOperator = NewUser("p0-dp-ob-" + suffix, orgB.Id, password);
        var otherOrgWriter = NewUser("p0-dp-wb-" + suffix, orgB.Id, password);
        var platformAdmin = NewUser("p0-dp-admin-" + suffix, orgA.Id, password, UserType.PlatformAdmin);
        await db.Insert(new[] { writer, sameOrgReader, otherOrgReader, otherOrgOperator, otherOrgWriter, platformAdmin }).ExecuteAffrowsAsync();
        await db.Insert(new[]
        {
            new UserOrgEntity { UserId = writer.Id, OrgId = orgA.Id },
            new UserOrgEntity { UserId = sameOrgReader.Id, OrgId = orgA.Id },
            new UserOrgEntity { UserId = otherOrgReader.Id, OrgId = orgB.Id },
            new UserOrgEntity { UserId = otherOrgOperator.Id, OrgId = orgB.Id },
            new UserOrgEntity { UserId = otherOrgWriter.Id, OrgId = orgB.Id },
            new UserOrgEntity { UserId = platformAdmin.Id, OrgId = orgA.Id }
        }).ExecuteAffrowsAsync();
        await db.Insert(new[]
        {
            new UserRoleEntity { UserId = writer.Id, RoleId = writerRole.Id },
            new UserRoleEntity { UserId = sameOrgReader.Id, RoleId = sameOrgReaderRole.Id },
            new UserRoleEntity { UserId = otherOrgReader.Id, RoleId = otherOrgReaderRole.Id },
            new UserRoleEntity { UserId = otherOrgOperator.Id, RoleId = otherOrgOperatorRole.Id }
            ,new UserRoleEntity { UserId = otherOrgWriter.Id, RoleId = writerRole.Id }
        }).ExecuteAffrowsAsync();

        var paths = new[] { "add", "get", "get-list", "get-data-permission", "update", "delete" };
        var permissions = new List<PermissionEntity>();
        var apis = new List<ApiEntity>();
        foreach (var action in paths)
        {
            var id = YitIdHelper.NextId();
            permissions.Add(new PermissionEntity
            {
                Id = id,
                Platform = "admin",
                ParentId = 0,
                Label = "P0 DP " + action + " " + suffix,
                Code = "P0.DataPermission." + action + "." + suffix,
                Type = PermissionType.Dot,
                Enabled = true
            });
            apis.Add(new ApiEntity
            {
                Id = YitIdHelper.NextId(),
                ParentId = 0,
                Name = "p0-dp-" + apis.Count + "-" + suffix,
                Label = "P0 data permission " + action,
                Path = ApiPrefix + "/" + action,
                HttpMethods = action switch { "add" => "POST", "get-list" => "POST", "update" => "PUT", "delete" => "DELETE", _ => "GET" },
                Enabled = true
            });
        }
        await db.Insert(permissions).ExecuteAffrowsAsync();
        var missingApis = new List<ApiEntity>();
        foreach (var api in apis)
        {
            var existing = db.Ado.QuerySingle<ApiEntity>(
                "select Id, ParentId, Name, Label, Path, HttpMethods, Enabled from base_api where ParentId = 0 and Path = @path",
                new { path = api.Path });
            if (existing == null)
            {
                missingApis.Add(api);
            }
            else
            {
                api.Id = existing.Id;
            }
        }
        if (missingApis.Count > 0)
        {
            await db.Insert(missingApis).ExecuteAffrowsAsync();
        }
        var permissionApis = new List<PermissionApiEntity>();
        for (var index = 0; index < paths.Length; index++)
        {
            permissionApis.Add(new PermissionApiEntity { PermissionId = permissions[index].Id, ApiId = apis[index].Id });
        }
        await db.Insert(permissionApis).ExecuteAffrowsAsync();

        var rolePermissions = new List<RolePermissionEntity>();
        AddAll(writerRole.Id);
        AddFor(sameOrgReaderRole.Id, "get", "get-list", "get-data-permission");
        AddFor(otherOrgReaderRole.Id, "get", "get-list", "get-data-permission");
        AddFor(otherOrgOperatorRole.Id, "get", "update", "delete", "get-data-permission");
        await db.Insert(rolePermissions).ExecuteAffrowsAsync();

        return new P0Access(password, writer.UserName, sameOrgReader.UserName, otherOrgReader.UserName, otherOrgOperator.UserName, otherOrgWriter.UserName,
            platformAdmin.UserName, writer.Id, orgA.Id, orgB.Id, writerRole.Id);

        void AddAll(long roleId) => AddFor(roleId, paths);
        void AddFor(long roleId, params string[] allowedActions)
        {
            foreach (var action in allowedActions)
            {
                var index = Array.IndexOf(paths, action);
                rolePermissions.Add(new RolePermissionEntity { Platform = "admin", RoleId = roleId, PermissionId = permissions[index].Id });
            }
        }
    }

    private static OrgEntity NewOrg(string code) => new()
    {
        Id = YitIdHelper.NextId(), ParentId = 0, Name = code, Code = code, Value = code, Enabled = true
    };

    private static RoleEntity NewRole(string code) => new()
    {
        Id = YitIdHelper.NextId(), ParentId = 0, Name = code, Code = code, Type = RoleType.Role, DataScope = DataScope.Custom
    };

    private static UserEntity NewUser(string userName, long orgId, string password, UserType userType = UserType.DefaultUser) => new()
    {
        Id = YitIdHelper.NextId(), UserName = userName, Password = MD5Encrypt.Encrypt32(password),
        PasswordEncryptType = PasswordEncryptType.MD5Encrypt32, Name = userName, NickName = userName,
        OrgId = orgId, Type = userType, Enabled = true
    };

    private static void WriteHostConfigs(string mainConnection, string logConnection)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ConfigCenter");
        Directory.CreateDirectory(directory);
        var logDb = new Dictionary<string, object>
        {
            ["key"] = "logdb", ["assemblyNames"] = new[] { "ZhonTai.Admin" }, ["includeEntityDbs"] = new[] { "Log" },
            ["excludeEntityDbs"] = Array.Empty<string>(), ["createDb"] = false, ["syncStructure"] = true, ["syncData"] = false,
            ["syncUpdateData"] = false, ["generateData"] = false, ["monitorCommand"] = false, ["curd"] = false,
            ["type"] = "MySql", ["connectionString"] = logConnection, ["idleTime"] = 0
        };
        var dbConfig = new Dictionary<string, object>
        {
            ["key"] = "admindb", ["assemblyNames"] = new[] { "ZhonTai.Admin", "ZhonTai.Admin.Tests" }, ["includeEntityDbs"] = Array.Empty<string>(),
            ["excludeEntityDbs"] = new[] { "Log" }, ["createDb"] = false, ["syncStructure"] = true, ["syncData"] = true,
            ["syncUpdateData"] = false, ["generateData"] = false, ["monitorCommand"] = false, ["curd"] = false,
            ["type"] = "MySql", ["connectionString"] = mainConnection, ["idleTime"] = 0, ["forceUpdate"] = false, ["dbs"] = new[] { logDb }
        };
        var appConfig = new Dictionary<string, object>
        {
            ["varifyCode"] = new Dictionary<string, object> { ["enable"] = false },
            ["validate"] = new Dictionary<string, object> { ["permission"] = true, ["dataPermission"] = true, ["apiDataPermission"] = true }
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, "dbconfig." + EnvName + ".json"), JsonSerializer.Serialize(new Dictionary<string, object> { ["DbConfig"] = dbConfig }, options));
        File.WriteAllText(Path.Combine(directory, "appconfig." + EnvName + ".json"), JsonSerializer.Serialize(new Dictionary<string, object> { ["AppConfig"] = appConfig }, options));
    }

    private sealed record P0Access(string Password, string WriterUserName, string SameOrgReaderUserName, string OtherOrgReaderUserName,
        string OtherOrgOperatorUserName, string OtherOrgWriterUserName, string PlatformAdminUserName, long WriterUserId, long OrgAId, long OrgBId, long WriterRoleId);

    private sealed class P0FixtureRecord
    {
        public long Id { get; init; }
        public long? OwnerId { get; init; }
        public long? OwnerOrgId { get; init; }
        public string RecordCode { get; init; } = string.Empty;
        public string RecordName { get; init; } = string.Empty;
        public bool IsDeleted { get; init; }
    }
}

public sealed class P0T5AcceptanceFactAttribute : FactAttribute
{
    public P0T5AcceptanceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMINCORE_T4_MYSQL")))
            Skip = "Set ADMINCORE_T4_MYSQL to the dedicated P0 T4 acceptance schema.";
    }
}

internal sealed record P0TestLogRecord(LogLevel Level, Exception Exception, IReadOnlyDictionary<string, object> Properties);

internal sealed class P0TestLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    public ConcurrentQueue<P0TestLogRecord> Records { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Logger(this);
    public void Dispose() { }
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

    private sealed class Logger(P0TestLoggerProvider owner) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal);
            owner._scopeProvider.ForEachScope((scope, values) => Add(scope, values), properties);
            Add(state, properties);
            owner.Records.Enqueue(new P0TestLogRecord(logLevel, exception, properties));
        }

        private static void Add(object state, IDictionary<string, object> values)
        {
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
                foreach (var pair in pairs) values[pair.Key] = pair.Value;
        }
    }
}

public sealed class P0DataPermissionAcceptanceFactAttribute : FactAttribute
{
    public P0DataPermissionAcceptanceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMINCORE_HOST_MYSQL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMINCORE_HOST_MYSQL_LOG")))
        {
            Skip = "Set ADMINCORE_HOST_MYSQL / ADMINCORE_HOST_MYSQL_LOG to dedicated MySQL 8 acceptance schemas.";
        }
    }
}
