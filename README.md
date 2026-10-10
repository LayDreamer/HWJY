# ZhonTai Admin

本仓库当前提供一套基于 .NET 10 和 Vue 3 的后台管理运行基线。主运行面由 `ZhonTai.Admin.Host` 提供 API、认证授权、数据初始化和接口文档；前端位于 `ui/zhontai.ui.admin.vue3`。

## 当前范围

主解决方案 `src/ZhonTai.sln` 包含的后台能力包括：

- 用户、组织、角色、权限、接口、视图、租户与套餐管理；
- 字典、缓存、文件、验证码、搜索模板与打印模板；
- 登录日志、操作日志、任务及任务日志；
- Swagger/API UI、任务调度、WebSocket 和内存 CAP 消息队列。

以下内容不属于当前业务运行范围：

- PMC、APS、MES 等企业业务模块已删除，不应作为现有功能或启动目标；
- `src/modules/company/Company.Platform` 仅保留公共能力边界说明，未实现独立业务功能；
- `src/modules/company/Company.MasterData` 是历史 Material 冒烟夹具，不在 `ZhonTai.sln`、正式前端路由或权限种子中；
- `src/gateways/ZhonTai.Gateway.Yarp.Host` 是未纳入主解决方案的独立网关工程，主系统启动不依赖它。

## 目录说明

```text
src/
  ZhonTai.sln                         主解决方案
  modules/admin/ZhonTai.Admin.Host/   API 宿主与初始化数据
  modules/admin/ZhonTai.Admin.Tests/  xUnit 测试
  platform/                           公共平台能力
  gateways/                           独立网关工程
  modules/company/                    企业模块边界与历史夹具
ui/zhontai.ui.admin.vue3/             Vue 3 管理前端
database/                              MySQL 发布物及迁移清单
```

## 开发环境

- .NET SDK 10；
- Node.js：前端 `package.json` 声明 `>=16`，建议使用当前 LTS；
- pnpm（可通过 Corepack 启用）。

后端默认数据库配置位于 `src/modules/admin/ZhonTai.Admin.Host/ConfigCenter/dbconfig.json`：当前为 SQLite，且首次启动会创建数据库、同步表结构并导入初始化数据。请勿将该默认启动流程直接指向共享或生产数据库。

`ConfigCenter` 中的基础 JSON 配置会先加载，对应的 `*.Development.json` 会在开发环境覆盖基础值。部署前请按目标环境审查数据库、JWT、缓存、对象存储和跨域配置，避免提交真实凭据。

## 启动后端

在仓库根目录执行：

```bash
dotnet restore src/ZhonTai.sln
dotnet run --project src/modules/admin/ZhonTai.Admin.Host
```

默认监听地址为 `http://localhost:18010`，接口文档地址为：

```text
http://localhost:18010/doc/admin
```

API 进程只管理任务定义；要执行持久化任务，需要另起一个 worker 进程：

```bash
dotnet run --project src/modules/admin/ZhonTai.Admin.Host -- --worker
```

## 启动前端

```bash
cd ui/zhontai.ui.admin.vue3
corepack enable
pnpm install --frozen-lockfile
pnpm dev
```

开发服务器默认使用 `9011` 端口，开发环境 API 地址由 `.env.development` 中的 `VITE_API_URL` 配置，当前对齐后端的 `http://localhost:18010`。

生产构建：

```bash
pnpm build
```

## 验证

```bash
dotnet test src/ZhonTai.sln --no-restore
```

前端可使用下列项目脚本：

```bash
pnpm lint-fix
pnpm format
pnpm gen:api
```

`gen:api` 会根据当前接口文档重新生成前端 API 代码；提交前请检查生成内容是否符合本次接口改动。

## 数据库发布物

`database/mysql/manifest.json` 是 MySQL 正向发布清单；`database/README.md` 说明了校验、锁定和回滚约束。该目录中的发布物面向显式指定的隔离数据库，不替代本地 SQLite 的默认启动流程。

MySQL 发布使用 `tools/AdminCore.P0.Release`。默认显示帮助，`plan` 和 `verify` 只读；`apply`、`rollback`、`recover` 必须带 `--confirm`，连接串仅从 `ADMINCORE_RELEASE_MYSQL` 或显式 `--connection` 读取且不会被输出：

```powershell
dotnet run --project tools/AdminCore.P0.Release -- plan
dotnet run --project tools/AdminCore.P0.Release -- apply --confirm
```

完整隔离 MySQL 验收使用 `tools/run-p0-gate.ps1`；缺少 `ADMINCORE_T4_MYSQL`、`ADMINCORE_HOST_MYSQL`、`ADMINCORE_HOST_MYSQL_LOG` 或 `ADMINCORE_ACCEPTANCE_MYSQL` 时会明确阻断。四个变量均指向专用隔离 schema，不得指向生产库。
