using FreeSql;
using ZhonTai.Admin.Core.Configs;
using ZhonTai.Admin.Core.Db.Data;
using ZhonTai.Admin.Domain.Api;
using ZhonTai.Admin.Domain.Permission;
using ZhonTai.Admin.Domain.PermissionApi;

namespace Company.MasterData.Repositories;

/// <summary>
/// 为统一宿主登记 Material 动态 API 及其最小权限关系。
/// 角色与组织归属由部署环境按人员职责配置，不在模块种子中隐式授权。
/// </summary>
public sealed class CompanyMasterDataPermissionSyncData : ISyncData
{
    private const string AdminPlatform = "admin";
    private const long QueryPermissionId = 880000000000001;
    private const long CreatePermissionId = 880000000000002;
    private const long UpdatePermissionId = 880000000000003;
    private const long DeletePermissionId = 880000000000004;

    private static readonly ApiSeed[] ApiSeeds =
    [
        new(890000000000001, "Material.Get", "物料详情", "/api/master-data/material/get", "get", QueryPermissionId),
        new(890000000000002, "Material.GetPage", "物料分页", "/api/master-data/material/get-page", "post", QueryPermissionId),
        new(890000000000003, "Material.Add", "新增物料", "/api/master-data/material/add", "post", CreatePermissionId),
        new(890000000000004, "Material.Update", "更新物料", "/api/master-data/material/update", "put", UpdatePermissionId),
        new(890000000000005, "Material.SetStatus", "设置物料状态", "/api/master-data/material/set-status", "put", UpdatePermissionId),
        new(890000000000006, "Material.Delete", "删除物料", "/api/master-data/material/delete", "delete", DeletePermissionId)
    ];

    private static readonly PermissionSeed[] PermissionSeeds =
    [
        new(QueryPermissionId, "Material.Query", "物料查询"),
        new(CreatePermissionId, "Material.Create", "物料新增"),
        new(UpdatePermissionId, "Material.Update", "物料更新"),
        new(DeletePermissionId, "Material.Delete", "物料删除")
    ];

    /// <summary>
    /// 同步模块 API 与权限点；不创建角色、组织或用户关联，避免部署时扩大授权范围。
    /// </summary>
    public async Task SyncDataAsync(IFreeSql db, DbConfig? dbConfig = null, AppConfig? appConfig = null)
    {
        if (dbConfig?.SyncData != true)
        {
            return;
        }

        foreach (var permission in PermissionSeeds)
        {
            await db.InsertOrUpdate<PermissionEntity>()
                .SetSource(new PermissionEntity
                {
                    Id = permission.Id,
                    Platform = AdminPlatform,
                    ParentId = 0,
                    Label = permission.Label,
                    Code = permission.Code,
                    Type = PermissionType.Dot,
                    Path = string.Empty,
                    Enabled = true,
                    Sort = 0,
                    Description = "Company MasterData API 权限"
                })
                .IfExistsDoNothing()
                .ExecuteAffrowsAsync();
        }

        foreach (var api in ApiSeeds)
        {
            await db.InsertOrUpdate<ApiEntity>()
                .SetSource(new ApiEntity
                {
                    Id = api.Id,
                    ParentId = 0,
                    Name = api.Name,
                    Label = api.Label,
                    Path = api.Path,
                    HttpMethods = api.HttpMethod,
                    Enabled = true,
                    EnabledLog = true,
                    Description = "Company MasterData 动态 API"
                })
                .IfExistsDoNothing()
                .ExecuteAffrowsAsync();

            await db.InsertOrUpdate<PermissionApiEntity>()
                .SetSource(new PermissionApiEntity
                {
                    PermissionId = api.PermissionId,
                    ApiId = api.Id
                })
                .IfExistsDoNothing()
                .ExecuteAffrowsAsync();
        }
    }

    private sealed record ApiSeed(long Id, string Name, string Label, string Path, string HttpMethod, long PermissionId);

    private sealed record PermissionSeed(long Id, string Code, string Label);
}
