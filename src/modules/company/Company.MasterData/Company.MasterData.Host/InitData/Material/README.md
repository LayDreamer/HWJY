# Material 权限与菜单初始化映射

独立 MasterData Host 的权限主数据由后续与 Admin 权限库的部署集成阶段导入。本文件固定 Material 模块的权限契约，避免在本阶段修改 `ZhonTai.Admin` 的 `InitData` 或权限逻辑。

| 类型 | 名称/编码 | 对应资源 |
|---|---|---|
| 菜单目录 | 基础资料 | `/company/master-data` |
| 菜单 | 物料管理 | `/company/master-data/material`，视图 `company/master-data/material/index` |
| 权限 | `Material.Query` | `GET material/get`、`POST material/get-page` |
| 权限 | `Material.Create` | `POST material/add` |
| 权限 | `Material.Update` | `PUT material/update`、`PUT material/set-status` |
| 权限 | `Material.Delete` | `DELETE material/delete` |

动态 API 区域为 `master-data`，路由前缀为 `/api/master-data/material`。
