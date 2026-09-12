
MyApp接口项目模板说明

*********************************************************

### nuget下载地址
```
https://www.nuget.org/downloads
```
> 将`nuget.exe`放到 `F:\zhontai\Admin.Core\templates` 目录下

### 查看模板列表
```
dotnet new list
```

### 生成nuget包
在 `F:\zhontai\Admin.Core\templates` 目录下 cmd 执行以下命令生成nuget包
```
nuget pack F:\zhontai\Admin.Core\templates\admin\templates.nuspec -NoDefaultExcludes
```
### 安装模板
```
dotnet new install ZhonTai.Template
```
安装本地
```
dotnet new install F:\zhontai\Admin.Core\templates\ZhonTai.Template.8.6.0.nupkg
```

### 创建项目
```
dotnet new MyApp -n MyCompanyName.MyProjectName
```

### 卸载模板
```
dotnet new uninstall ZhonTai.Template
```

*********************************************************

## 业务实体基类选择（重要，新增实体前必读）

框架的数据权限全局过滤器**只作用于实现了 `IData` 的实体**（`DbHelper.RegisterDb` 中两条过滤器均为
`ApplyOnlyIf<IData>`）。因此**实体不接 `IData` 时，组织级数据权限不会生效**。

请按下表选择业务实体基类：

| 该实体是否需要组织级数据权限 | 应选择的基类 | 是否实现 `IData` |
|---|---|---|
| **需要**（主数据、关键业务事实，需按部门/组织过滤） | `EntityData` 或 `EntityTenantWithData` | 是 |
| 不需要（平台级配置、内部技术表） | `EntityBase` 或 `EntityTenant` | 否 |

说明：

- `EntityTenantWithData` = `EntityTenant` + `IData`（含 `TenantId`），是当前全局 `tenant = true` 下的**业务实体默认选择**；
- `EntityData` = `EntityBase` + `IData`（无 `TenantId`），仅在全局关闭多租户时使用；
- 审计字段（`CreatedUserId` / `CreatedTime` 等）与软删除（`IsDeleted`）来自 `EntityBase` 体系，
  **不是 `EntityTenant` 提供的**，因此 `EntityTenant` → `EntityData` 的切换不会丢能力；
- **不得**手工重复声明 `OwnerId` / `OwnerOrgId` / `OwnerOrgName`（`IData` 已提供）；
- **不得**用 `TenantId` 代替组织归属（租户是隔离维度，组织是归属维度）。

> ⚠️ 常见陷阱：只做「实体实现 `IData`」还不够——当前请求还必须能取到 `DataPermissionOutput`（含 `OrgIds`）。
> 若 `OrgIds` 为空，过滤器会退化为「仅本人数据」（`OwnerId == 当前用户`），**看起来像隔离生效、实际不是组织级过滤**。
> 新模块接入前请确认：模块 API 已注册进 `base_api` 并绑定 `base_permission` / `base_permission_api`，且目标角色已绑定。

新增业务实体时请一并确认：`Owner Module` / `Table Ownership`、`SourceSystem` / `SourceId`（主数据与关键业务事实必须有）、
`OwnerOrgId` 的赋值来源、以及唯一键 / 索引（参与唯一键的列必须 `NOT NULL`——MySQL 唯一索引对 `NULL` 不去重）。

详细条款见《P0 业务实体基类规范》。

