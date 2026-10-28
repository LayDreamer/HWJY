# HWJY 管理系统

HWJY 是基于 .NET 10 和 Vue 3 的后台管理系统，提供 API、认证授权、数据初始化和接口文档。后端入口和前端目录见下方目录说明。

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
- Node.js：前端 `package.json` 声明 `^20.19.0 || ^22.13.0 || >=24.0.0`，推荐使用与 CI 一致的 **24.x**；其他声明允许的版本不代表已经过当前 CI 验证；
- pnpm **11.25.0**，与前端 `packageManager` 和 CI 固定版本一致；
- Windows 开发与质量门禁环境使用 **PowerShell 7**，确保 `pwsh` 位于 PATH。`pnpm build` 调用 `tools/build.ps1`，Windows PowerShell 5.1 的 `powershell.exe` 不能替代此入口。

安装 Node.js 24.x 和 PowerShell 7 后，在 PowerShell 7 中检查工具版本；已有正确版本 pnpm 时跳过安装命令：

```powershell
npm install --global pnpm@11.25.0
node --version
pnpm --version
pwsh -NoProfile -Command '$PSVersionTable.PSVersion'
```

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

首次获取仓库：

```powershell
git clone https://github.com/LayDreamer/HWJY.git
cd HWJY
```

从仓库根目录执行（已有本地仓库时直接从此步开始）：

```powershell
cd ui/zhontai.ui.admin.vue3
pnpm install --frozen-lockfile --registry=https://registry.npmjs.org
pnpm dev
```

开发服务器默认使用 `9011` 端口，开发环境 API 地址由 `.env.development` 中的 `VITE_API_URL` 配置，当前对齐后端的 `http://localhost:18010`。

生产构建（在前端目录另开终端执行，或先停止开发服务）：

```powershell
pnpm build
```

构建会清理并重建前端目录下的 `dist/`。更多前端说明见 [前端 README](ui/zhontai.ui.admin.vue3/README.md)。

## 验证

```bash
dotnet test src/ZhonTai.sln --no-restore
```

在 `ui/zhontai.ui.admin.vue3` 目录执行与质量门禁一致的前端检查：

```powershell
pnpm lint
pnpm typecheck
pnpm test
pnpm build
```

这些命令只覆盖前端检查，完整质量门禁和隔离 MySQL 验收要求见 [质量 Gate 说明](tools/QUALITY-GATE.md)。

按需使用的维护脚本：`pnpm lint-fix` 和 `pnpm format` 会修改文件；`pnpm gen:api` 会根据当前接口文档重新生成前端 API 代码。它们不属于上述常规验证步骤，运行后请检查变更范围。

## 数据库发布物

`database/mysql/manifest.json` 是 MySQL 正向发布清单；`database/README.md` 说明了校验、锁定和回滚约束。该目录中的发布物面向显式指定的隔离数据库，不替代本地 SQLite 的默认启动流程。

MySQL 发布使用 `tools/AdminCore.P0.Release`。默认显示帮助，`plan` 和 `verify` 只读；`apply`、`rollback`、`recover` 必须带 `--confirm`，连接串仅从 `ADMINCORE_RELEASE_MYSQL` 或显式 `--connection` 读取且不会被输出：

```powershell
dotnet run --project tools/AdminCore.P0.Release -- plan
dotnet run --project tools/AdminCore.P0.Release -- apply --confirm
```

完整隔离 MySQL 验收使用 `tools/run-p0-gate.ps1`；缺少 `ADMINCORE_T4_MYSQL`、`ADMINCORE_HOST_MYSQL`、`ADMINCORE_HOST_MYSQL_LOG` 或 `ADMINCORE_ACCEPTANCE_MYSQL` 时会明确阻断。四个变量均指向专用隔离 schema，不得指向生产库。
