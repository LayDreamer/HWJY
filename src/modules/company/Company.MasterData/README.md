# Company.MasterData - Legacy Smoke Fixture

本目录保留初始仓库中的 Material 冒烟样例：实体、DTO、仓储、动态 API、权限契约和独立 Host。它不是汉武酒业当前已确认或已启动的业务模块。

## 隔离规则

- `ZhonTai.Admin.Host`、正式前端路由、菜单、权限种子和 `ZhonTai.sln` 不再组合本目录；
- `Company.MasterData.Host` 仅可显式携带 `--smoke-fixture` 启动，用于复核历史技术链路；
- `md_material`、`MaterialEntity`、`IMaterialService` 和 `MaterialCode` 不得被用作真实业务对象、表归属或数据模型依据；
- 不得向任何开发、验收或生产业务库写入该样例数据；历史隔离验收库中的 `ACC-` 数据按环境清理策略处理；
- 当 PRD 确认首个真实业务对象后，按 P0 规范新建模块、实体、Contract 与验收，不复用本样例的字段或规则。

保留本源码只为追溯既有 MySQL、JWT/RBAC、组织隔离和基类规范的冒烟验证；它不构成业务功能、上线范围或主数据设计。
