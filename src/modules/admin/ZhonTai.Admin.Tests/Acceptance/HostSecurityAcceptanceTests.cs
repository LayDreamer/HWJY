using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Consts;
using ZhonTai.Admin.Domain;
using ZhonTai.Admin.Domain.User;
using ZhonTai.Admin.Domain.UserOrg;
using ZhonTai.Admin.Domain.UserRole;
using ZhonTai.Admin.Domain.UserStaff;
using ZhonTai.Admin.Tools.Cache;
using ZhonTai.Common.Helpers;

namespace ZhonTai.Admin.Tests.Acceptance;

public sealed partial class HostMySqlAcceptanceTests
{
    [HostMySqlAcceptanceFact]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public async Task UploadEndpointValidatesBeforeWritingOrReusingALegacyFile()
    {
        var main = P0MySqlAcceptanceEnvironment.RequireConnection(MainConnectionVariable);
        WriteHostConfigs(main, P0MySqlAcceptanceEnvironment.RequireConnection(LogConnectionVariable));
        using var app = CreateHost();
        var db = app.Services.GetRequiredService<FreeSqlCloud>().Use(DbKeys.AdminDb);
        var suffix = "acc-upload-" + Guid.NewGuid().ToString("N");
        using var cleanup = AcceptanceFixtureCleanup.ForMySql(main);
        cleanup.Register(d => d.Delete<FileEntity>().Where(f => f.FileName == suffix).ExecuteAffrows());
        var temp = Path.Combine(Path.GetTempPath(), "HWJY-upload-host-" + Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(temp, "wwwroot");
        Directory.CreateDirectory(webRoot);
        app.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath = webRoot;
        var storage = app.Services.GetRequiredService<IOptions<OSSConfig>>().Value;
        storage.OSSConfigs = [];
        storage.LocalUploadConfig.Md5 = true;
        using var client = app.CreateClient();
        await LoginAsync(client);
        async Task<string> Upload(string directory)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(directory), "fileDirectory");
            form.Add(new StringContent("false"), "fileReName");
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(suffix)), "file", suffix + ".txt");
            using var response = await client.PostAsync("/api/admin/file/upload-file?fileReName=false&fileDirectory=" + Uri.EscapeDataString(directory), form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
        try
        {
            Assert.True(IsSecuritySuccess(await Upload("upload/nested")));
            var saved = await db.Select<FileEntity>().Where(f => f.FileName == suffix).ToOneAsync();
            Assert.NotNull(saved);
            Assert.Equal(suffix, await File.ReadAllTextAsync(Path.Combine(webRoot, "upload", "nested", suffix + ".txt")));
            foreach (var directory in new[] { "../outside", "upload/../../outside", "upload-other", temp, "C:\\outside" })
                AssertSecurityRejected(await Upload(directory));
            Assert.Single(Directory.GetFiles(temp, "*.txt", SearchOption.AllDirectories));
            Assert.Equal(1, await db.Select<FileEntity>().Where(f => f.FileName == suffix).CountAsync());
            await db.Update<FileEntity>().Set(f => f.FileDirectory, "../outside").Where(f => f.Id == saved.Id).ExecuteAffrowsAsync();
            AssertSecurityRejected(await Upload("upload/nested"));
            Assert.Equal(1, await db.Select<FileEntity>().Where(f => f.FileName == suffix).CountAsync());
            using (var denied = await client.PostAsJsonAsync("/api/admin/file/delete", new { Id = saved.Id }))
            {
                Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
                AssertSecurityRejected(await denied.Content.ReadAsStringAsync());
            }
            Assert.True(File.Exists(Path.Combine(webRoot, "upload", "nested", suffix + ".txt")));
            await db.Update<FileEntity>().Set(f => f.FileDirectory, "upload/nested").Where(f => f.Id == saved.Id).ExecuteAffrowsAsync();
            Assert.True(IsSecuritySuccess(await PostJsonAsync(client, "/api/admin/file/delete", new { Id = saved.Id })));
            Assert.Empty(Directory.GetFiles(temp, "*.txt", SearchOption.AllDirectories));
            Assert.Equal(0, await db.Select<FileEntity>().Where(f => f.FileName == suffix).CountAsync());
        }
        finally
        {
            var owned = Path.GetFullPath(temp);
            if (!Path.GetFileName(owned).StartsWith("HWJY-upload-host-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected upload fixture path.");
            Directory.Delete(owned, true);
        }
    }

    [HostMySqlAcceptanceTheory]
    [InlineData(null)]
    [InlineData(PasswordEncryptType.MD5Encrypt32)]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public async Task DefaultPasswordPolicyKeepsLegacyLoginAndUpgradesPasswordWrites(PasswordEncryptType? legacyType)
    {
        var main = P0MySqlAcceptanceEnvironment.RequireConnection(MainConnectionVariable);
        WriteHostConfigs(main, P0MySqlAcceptanceEnvironment.RequireConnection(LogConnectionVariable));
        using var app = CreateHost();
        var db = app.Services.GetRequiredService<FreeSqlCloud>().Use(DbKeys.AdminDb);
        var prefix = "acc-password-" + Guid.NewGuid().ToString("N")[..12];
        using var cleanup = AcceptanceFixtureCleanup.ForMySql(main);
        cleanup.Register(d => CleanupSecurityUsers(d, prefix));
        using var admin = app.CreateClient();
        await LoginAsync(admin);
        const string oldPassword = "AccOld123!";
        const string newPassword = "AccNew123!";
        var id = await CreateSecurityUser(admin, db, prefix, oldPassword);
        var created = await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync();
        Assert.True(app.Services.GetRequiredService<IOptions<AppConfig>>().Value.PasswordHasher);
        AssertHashed(app.Services, created, oldPassword);
        await db.Update<UserEntity>().Set(u => u.Password, MD5Encrypt.Encrypt32(oldPassword)).Set(u => u.PasswordEncryptType, legacyType).Where(u => u.Id == id).ExecuteAffrowsAsync();
        using var legacy = app.CreateClient();
        await LoginAsync(legacy, prefix, oldPassword);
        Assert.Equal(legacyType, (await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync()).PasswordEncryptType);
        using (var bad = await legacy.PutAsJsonAsync("/api/admin/user/change-password", new { OldPassword = "Wrong123!", NewPassword = newPassword, ConfirmPassword = newPassword }))
            AssertSecurityRejected(await bad.Content.ReadAsStringAsync());
        Assert.Equal(MD5Encrypt.Encrypt32(oldPassword), (await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync()).Password);
        using (var changed = await legacy.PutAsJsonAsync("/api/admin/user/change-password", new { OldPassword = oldPassword, NewPassword = newPassword, ConfirmPassword = newPassword }))
            Assert.True(IsSecuritySuccess(await changed.Content.ReadAsStringAsync()));
        AssertHashed(app.Services, await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync(), newPassword);
        using var fresh = app.CreateClient();
        await LoginAsync(fresh, prefix, newPassword);
        using (var wrong = await fresh.PostAsJsonAsync("/api/admin/auth/login", new { UserName = prefix, Password = oldPassword }))
            AssertSecurityRejected(await wrong.Content.ReadAsStringAsync());

        // Resetting a legacy account also upgrades its password and type together.
        await db.Update<UserEntity>().Set(u => u.Password, MD5Encrypt.Encrypt32(oldPassword)).Set(u => u.PasswordEncryptType, legacyType).Where(u => u.Id == id).ExecuteAffrowsAsync();
        Assert.True(IsSecuritySuccess(await PostJsonAsync(admin, "/api/admin/user/reset-password", new { Id = id, Password = newPassword })));
        AssertHashed(app.Services, await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync(), newPassword);
        var config = app.Services.GetRequiredService<IOptions<AppConfig>>().Value;
        config.PasswordHasher = false;
        try
        {
            Assert.True(IsSecuritySuccess(await PostJsonAsync(admin, "/api/admin/user/reset-password", new { Id = id, Password = oldPassword })));
            AssertHashed(app.Services, await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync(), oldPassword);
            using var strong = app.CreateClient();
            await LoginAsync(strong, prefix, oldPassword);
            using var changed = await strong.PutAsJsonAsync("/api/admin/user/change-password", new { OldPassword = oldPassword, NewPassword = newPassword, ConfirmPassword = newPassword });
            Assert.True(IsSecuritySuccess(await changed.Content.ReadAsStringAsync()));
            AssertHashed(app.Services, await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync(), newPassword);
        }
        finally { config.PasswordHasher = true; }
    }

    [HostMySqlAcceptanceTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("P0Dependency", "MySql")]
    [Trait("P0Gate", "Required")]
    public async Task VerifiedEmailAndMobilePasswordChangesUpgradeLegacyAndNeverDowngradeHasher(bool mobile, bool alreadyHashed)
    {
        var main = P0MySqlAcceptanceEnvironment.RequireConnection(MainConnectionVariable);
        WriteHostConfigs(main, P0MySqlAcceptanceEnvironment.RequireConnection(LogConnectionVariable));
        using var app = CreateHost();
        var db = app.Services.GetRequiredService<FreeSqlCloud>().Use(DbKeys.AdminDb);
        var prefix = "acc-password-" + Guid.NewGuid().ToString("N")[..12];
        using var cleanup = AcceptanceFixtureCleanup.ForMySql(main);
        cleanup.Register(d => CleanupSecurityUsers(d, prefix));
        using var admin = app.CreateClient();
        await LoginAsync(admin);
        var id = await CreateSecurityUser(admin, db, prefix, "AccOld123!");
        var email = prefix + "@example.invalid";
        var phone = "180" + Random.Shared.Next(10000000, 99999999);
        await db.Update<UserEntity>().Set(u => u.Email, email).Set(u => u.Mobile, phone).Where(u => u.Id == id).ExecuteAffrowsAsync();
        if (!alreadyHashed)
            await db.Update<UserEntity>().Set(u => u.Password, MD5Encrypt.Encrypt32("AccOld123!")).Set(u => u.PasswordEncryptType, (PasswordEncryptType?)null).Where(u => u.Id == id).ExecuteAffrowsAsync();
        var config = app.Services.GetRequiredService<IOptions<AppConfig>>().Value;
        config.PasswordHasher = !alreadyHashed;
        var codeId = Guid.NewGuid().ToString("N");
        var key = mobile ? CacheKeys.GetSmsCodeKey(phone, codeId) : CacheKeys.GetEmailCodeKey(email, codeId);
        var cache = app.Services.GetRequiredService<ICacheTool>();
        await cache.SetAsync(key, "123456", TimeSpan.FromMinutes(1));
        try
        {
            using var anonymous = app.CreateClient();
            var body = new { Email = email, Mobile = phone, CodeId = codeId, Code = "123456", NewPassword = "AccNew123!", ConfirmPassword = "AccNew123!" };
            Assert.True(IsSecuritySuccess(await PostJsonAsync(anonymous, mobile ? "/api/admin/auth/change-password-by-mobile" : "/api/admin/auth/change-password-by-email", body)));
            AssertHashed(app.Services, await db.Select<UserEntity>().Where(u => u.Id == id).ToOneAsync(), "AccNew123!");
            using var fresh = app.CreateClient();
            await LoginAsync(fresh, prefix, "AccNew123!");
        }
        finally { config.PasswordHasher = true; await cache.DelAsync(key); }
    }

    private static bool IsSecuritySuccess(string response)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(response);
            return TryGetProperty(document.RootElement, "success", out var success) && success.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    private static void AssertSecurityRejected(string response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(response);
        Assert.True(TryGetProperty(document.RootElement, "success", out var success));
        Assert.Equal(System.Text.Json.JsonValueKind.False, success.ValueKind);
    }

    private static async Task<long> CreateSecurityUser(HttpClient admin, IFreeSql db, string name, string password)
    {
        Assert.True(IsSecuritySuccess(await PostJsonAsync(admin, "/api/admin/user/add", new { UserName = name, Name = name, Password = password, Enabled = true, Staff = new { }, OrgIds = Array.Empty<long>(), RoleIds = Array.Empty<long>() })));
        return await db.Select<UserEntity>().Where(u => u.UserName == name).ToOneAsync(u => u.Id);
    }

    private static void AssertHashed(IServiceProvider services, UserEntity user, string password)
    {
        Assert.Equal(PasswordEncryptType.PasswordHasher, user.PasswordEncryptType);
        Assert.NotEqual(password, user.Password);
        Assert.NotEqual(MD5Encrypt.Encrypt32(password), user.Password);
        Assert.NotEqual(PasswordVerificationResult.Failed, services.GetRequiredService<IPasswordHasher<UserEntity>>().VerifyHashedPassword(user, user.Password, password));
    }

    private static void CleanupSecurityUsers(IFreeSql db, string prefix)
    {
        var ids = db.Select<UserEntity>().Where(u => u.UserName == prefix).ToList(u => u.Id);
        if (ids.Count == 0) return;
        db.Delete<UserRoleEntity>().Where(r => ids.Contains(r.UserId)).ExecuteAffrows();
        db.Delete<UserOrgEntity>().Where(r => ids.Contains(r.UserId)).ExecuteAffrows();
        db.Delete<UserStaffEntity>().Where(r => ids.Contains(r.Id)).ExecuteAffrows();
        db.Delete<UserEntity>().Where(u => ids.Contains(u.Id)).ExecuteAffrows();
    }
}

public sealed class HostMySqlAcceptanceTheoryAttribute : TheoryAttribute
{
    public HostMySqlAcceptanceTheoryAttribute()
    {
        if (P0MySqlAcceptanceEnvironment.IsMissing("ADMINCORE_HOST_MYSQL") || P0MySqlAcceptanceEnvironment.IsMissing("ADMINCORE_HOST_MYSQL_LOG"))
            Skip = "Dedicated MySQL acceptance schemas are required.";
    }
}
