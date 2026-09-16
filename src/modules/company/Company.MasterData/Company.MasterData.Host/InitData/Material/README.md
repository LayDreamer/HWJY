# Legacy Material 冒烟夹具映射

> 已隔离：以下内容仅保留历史冒烟验证参考，禁止导入正式 Host、菜单、权限主数据或任何真实业务库。

独立 MasterData Host 只有显式携带 `--smoke-fixture` 时才可用于本样例的技术复核；不代表部署集成或业务权限契约。

| 类型 | 名称/编码 | 对应资源 |
|---|---|---|
| 菜单目录 | 基础资料 | `/company/master-data` |
| 菜单 | 物料管理 | `/company/master-data/material`，视图 `company/master-data/material/index` |
| 权限 | `Material.Query` | `GET material/get`、`POST material/get-page` |
| 权限 | `Material.Create` | `POST material/add` |
| 权限 | `Material.Update` | `PUT material/update`、`PUT material/set-status` |
| 权限 | `Material.Delete` | `DELETE material/delete` |

动态 API 区域为 `master-data`，路由前缀为 `/api/master-data/material`。
