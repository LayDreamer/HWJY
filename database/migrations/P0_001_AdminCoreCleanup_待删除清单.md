# P0_001 Admin.Core 历史数据待删除清单

扫描库：`admindb.db`（SQLite）  
扫描时间：2026-09-15  
清理前备份：`database/backups/admincore_before_p0_cleanup_20260915_105851.db`

| 类型 | 范围 | 预计数量 | 处理方式 |
|---|---|---:|---|
| Permission | 开发、帮助、地区、消息、消息分类及其子权限 | 97 | 删除 |
| Permission | 租户、套餐、打印模板及其入口权限 | 22 | 仅移除入口权限 |
| View | 开发、站内信、地区、消息、消息分类 | 5 | 删除 |
| View | 租户、套餐、打印模板 | 3 | 仅移除入口 |
| API | Dev、地区、消息、消息分类、站内信 | 118 | 删除 |
| RolePermission | 指向上述 Permission | 0 | 删除关联（当前为空） |
| PermissionApi | 指向 A 类权限/API | 101 | 删除 |
| PermissionApi | 指向 B 类入口权限 | 38 | 删除 |
| Region 数据 | `base_region` | 3,197 | 删除（实体已移除） |
| Dev/消息/帮助数据 | 对应表 | 0 | 无业务数据；保留空表结构 |

不在删除范围：用户、角色、组织、字典、文件、认证、日志、任务、CAP、gRPC，以及租户/套餐/打印模板的后端 API 与业务表。
