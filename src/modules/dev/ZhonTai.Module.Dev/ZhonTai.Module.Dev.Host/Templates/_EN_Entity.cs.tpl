@{
    var gen = Model as ZhonTai.Module.Dev.Api.Contracts.Domain.CodeGen.CodeGenEntity;
    if (gen?.Fields?.Any() != true) return;
    
    var entityNamePc = gen.EntityName.NamingPascalCase();
    var entityClassName = entityNamePc.PadEndIfNot("Entity").PadEndIfNotEmpty(gen.BaseEntity, ": " + gen.BaseEntity);

    // 定义需要排除的公共字段
    var commonFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "OwnerId", "OwnerOrgId", "IsDeleted", "TenantId",
        "CreatedUserId", "CreatedUserName", "CreatedUserRealName", "CreatedTime", 
        "ModifiedUserId", "ModifiedUserName", "ModifiedUserRealName", "ModifiedTime"
    };
    
    // 过滤有效字段
    var validFields = gen.Fields
        .Where(col => col != null)
        .Where(col => !commonFields.Contains(col.ColumnName))
        .Where(col => !col.IsIgnoreColumn())
        .ToList();
}
// ─────────────────────────────────────────────────────────────────────────────
// 生成物自检清单（生成后请逐项确认；完整规则见《P0 业务实体基类规范》）
//   1. 基类选择 —— 需要组织级数据权限时，基类必须是 EntityData 或 EntityTenantWithData（实现 IData）；
//                  不需要时可用 EntityBase / EntityTenant。
//      注意：实体不实现 IData 时，数据权限全局过滤器不生效，组织范围过滤会退化为「仅本人数据」。
//   2. Owner Module / Table Ownership —— 登记本实体唯一的归属模块（架构元数据，不得做成数据库字段）。
//   3. SourceSystem / SourceId —— 主数据与关键业务事实必须保留来源追溯，两列 NOT NULL。
//   4. OwnerOrgId —— 由服务端赋值，不得进入普通 Input DTO；无请求上下文的写入方必须显式提供组织归属。
//   5. 唯一键 / 索引 —— 参与唯一键的列必须 NOT NULL（MySQL 唯一索引对 NULL 不去重，可空列构成的是假保证）。
// ─────────────────────────────────────────────────────────────────────────────
using FreeSql.DataAnnotations;
using System;
using ZhonTai.Admin.Core.Entities;
@foreach(var ns in gen.GetUsings())
{
@:using @ns;    
}

namespace @(gen.Namespace).Api.Contracts.Domain.@(entityNamePc);

/// <summary>
/// @(gen.BusName)实体
/// </summary>
/// <remarks>@gen.Comment</remarks>
@(gen.GetTableIndexAttributes())
public partial class @entityClassName
{
@foreach (var col in validFields)
{
    // 属性注释
    @:/// <summary>
    @:/// @col.Title
    @:/// </summary>
    @if (!string.IsNullOrWhiteSpace(col.Comment))
    {
    @:/// <remarks>@col.Comment</remarks>
    }
    
    // 列属性
    @:@col.FreeSqlColumnAttribute()
    
    // 属性定义
    @:@col.PropCs()

    // 如果需要包含导航属性（当前被注释）
    @*@if (col.IsIncludeColumn())
    {
        @:@col.FreeSqlNavigaetAttribute()
        @:@col.PropIncludeCs()
    }*@
    
    // 在属性之间添加空行（最后一个属性后不添加）
    @if (col != validFields.Last())
    {
@:
    }
}
}