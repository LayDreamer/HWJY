using System;
using FreeSql.DataAnnotations;
using ZhonTai.Admin.Core.Entities;
using MyApp.Api.Contracts.Core.Consts;

namespace MyApp.Api.Contracts.Domain.Module;

/// <summary>
/// 模块
/// </summary>
/// <remarks>
/// 【基类选择】本示例使用 EntityTenant，即「不需要组织级数据权限」的写法。
/// 若本实体需要按部门/组织做数据权限过滤，请改为 EntityData 或 EntityTenantWithData（二者均实现 IData）；
/// 实体不实现 IData 时，数据权限全局过滤器不会生效——详见项目根目录 README.md 的「业务实体基类选择」一节。
/// </remarks>
[Table(Name = DbConsts.TableNamePrefix + "module")]
[Index("idx_{tablename}_01", nameof(TenantId) + "," + nameof(Name), true)]
public partial class ModuleEntity : EntityTenant
{
    /// <summary>
    /// 名称
    /// </summary>
    [Column(StringLength = 50)]
    public string Name { get; set; }
}