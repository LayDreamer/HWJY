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
- `src/gateways/ZhonTai.Gateway.Yarp.Host` 是未纳入主解决方案的独立网关工程，主系统启动不依赖它。

## 目录说明

```text
src/
  ZhonTai.sln                         主解决方案
  modules/admin/ZhonTai.Admin.Host/   API 宿主与初始化数据
  modules/admin/ZhonTai.Admin.Tests/  xUnit 测试
  platform/                           公共平台能力
  gateways/                           独立网关工程
  modules/company/                    业务扩展约定
ui/zhontai.ui.admin.vue3/             Vue 3 管理前端
database/                              MySQL 发布物及迁移清单
```

## 开发环境

- .NET SDK 10；
- MySQL 8：Development 配置使用 `127.0.0.1:3306`，主库 `admincore_host`，日志库 `admincore_host_log`；
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

后端基础数据库配置位于 `src/modules/admin/ZhonTai.Admin.Host/ConfigCenter/dbconfig.json`，保留 SQLite 默认值及原有初始化行为；不要将该默认初始化流程直接用于共享或生产数据库。日常开发使用 `dbconfig.Development.json` 覆盖为上述 MySQL 主库和日志库，关闭自动建库、表结构同步、初始化数据同步、初始化数据更新和造数，启动前需要准备好两库及表结构。已有 SQLite 文件保留，不自动迁移数据。

`ConfigCenter` 按“基础 `名称.json` → 当前环境的 `名称.环境名.json`”顺序加载，不读取其他环境后缀文件；同一层按文件名排序，当前环境值优先。Development 宿主随后读取当前进程的 `DbConfig__ConnectionString` 和 `DbConfig__Dbs__0__ConnectionString`，分别覆盖主库和日志库连接串。这两条额外覆盖仅在 Development 生效；Production 和隔离验收环境应提供各自的显式配置。开发连接示例没有密码，不能代替实际开发凭据；不要把真实凭据写入源码、README 或 `launchSettings.json`。

## 启动后端

在 `HWJY` 仓库根目录的 PowerShell 7 终端使用统一的日常启动入口：

```powershell
./tools/start-dev.ps1
```

入口先恢复依赖并构建当前工程的 Release，构建成功后才启动同一份输出，使用 `Development` 环境及工程目录下的配置和 WebRoot。不依赖临时验收发布目录，也不提供跳过构建的选项。按 `Ctrl+C` 停止，再次执行同一命令即可运行最新代码。

已在当前终端注入 `DbConfig__ConnectionString` 和 `DbConfig__Dbs__0__ConnectionString` 时直接复用；两项均未设置时，在构建成功后交互输入 MySQL 开发账号和密码，自动连接本机 3306 的 `admincore_host` 和 `admincore_host_log`。连接串构造器处理密码中的特殊字符，凭据仅保留在当前终端的进程环境中，同一终端重新启动无需再输入，不写文件或用户／系统环境变量。新终端需要重新注入或输入；更换账号时先清空这两项进程变量。

入口不会自动停止已有服务；18010 被占用时先退出，由开发者确认原进程后手动停止。不接受临时验收的 `ADMINCORE_CONFIGCENTER_PATH` 覆盖，也不执行数据库初始化或迁移。常规调试仍可使用项目的 Development 启动配置；手工使用 `--no-build` 前必须确保同一配置已构建成功，不能用旧输出代替最新源码。

浏览器访问 `http://localhost:18010`，接口文档地址为：

```text
http://localhost:18010/doc/admin
```

另开终端验证服务状态：

```powershell
Invoke-RestMethod http://localhost:18010/health/live
Invoke-RestMethod http://localhost:18010/health/ready | ConvertTo-Json -Depth 5
```

`/health/live` 检查进程存活；`/health/ready` 会实际执行数据库查询，主库和日志库条目 `database-admindb`、`database-logdb` 均应为 `Healthy`。MySQL 端口能够连接不代表这两个库已经可用。

API 进程只管理任务定义；要执行持久化任务，需要另起一个 worker 进程：

```powershell
./tools/start-dev.ps1 -Worker
```

worker 终端同样复用两条连接变量，缺少时交互输入凭据；仍使用既有的本机随机监听端口。

## 启动前端

首次获取仓库：

```powershell
git clone https://github.com/LayDreamer/HWJY.git
cd HWJY
```

在另一个终端从 `HWJY` 仓库根目录执行（已有本地仓库时直接从此步开始）。首次安装或锁文件变化时执行安装命令，依赖已就绪时直接执行 `pnpm dev`：

```powershell
cd ui/zhontai.ui.admin.vue3
pnpm install --frozen-lockfile --registry=https://registry.npmjs.org
pnpm dev
```

前端访问地址为 `http://localhost:9011`。开发环境 API 地址由 `.env.development` 中的 `VITE_API_URL` 配置，当前为 `http://localhost:18010`；本机覆盖写在不纳入 Git 的 `.env.development.local`，修改后重启开发服务。启动和构建不再改写 `public/env.config.js`；生产构建根据当前构建环境生成 `dist/env.config.js`，部署后仍可直接调整该产物中的 API 地址。前端终端不需要数据库凭据。

生产构建（在前端目录另开终端执行，或先停止开发服务）：

```powershell
pnpm build
```

构建会清理并重建前端目录下的 `dist/`。更多前端说明见 [前端 README](ui/zhontai.ui.admin.vue3/README.md)。

## 上传与密码存储

本地上传的 `fileDirectory` 保持相对 WebRoot 的约定，必须位于 `LocalUploadConfig.Directory` 指定的上传根目录内；默认根目录为 `upload`。留空时沿用配置的日期子目录，手动填写可使用 `upload/images` 等子目录。绝对路径、父目录跳转和符号链接／目录联接不允许用于上传。历史 MD5 复用与本地文件删除也执行相同校验；越界的历史记录需人工核实，不自动操作根目录外的文件。

`AppConfig.PasswordHasher` 及宿主配置默认启用标准密码哈希。旧 MD5 账号（包括加密类型为空的历史记录）仍可使用原密码登录，登录时不批量或自动改写密码；新建账号及修改、重置密码时采用默认哈希策略，并同步记录加密类型。已使用标准哈希的账号不会因兼容配置关闭而降级。此改动不需要数据库结构迁移，不改变密码强度校验规则。

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

`database/mysql/manifest.json` 是 MySQL 正向发布清单；`database/README.md` 说明了校验、锁定和回滚约束。该目录中的发布物面向显式指定的隔离数据库；Development 日常启动只连接已准备好的开发库，不自动执行发布或迁移。

MySQL 发布使用 `tools/AdminCore.P0.Release`。默认显示帮助，`plan` 和 `verify` 只读；`apply`、`rollback`、`recover` 必须带 `--confirm`，连接串仅从 `ADMINCORE_RELEASE_MYSQL` 或显式 `--connection` 读取且不会被输出：

```powershell
dotnet run --project tools/AdminCore.P0.Release -- plan
dotnet run --project tools/AdminCore.P0.Release -- apply --confirm
```

完整隔离 MySQL 验收使用 `pwsh -NoProfile -File tools/run-p0-gate.ps1 -Mode Full`，入口自动创建并停止独立的 `127.0.0.1:33306` 实例，使用专用账号和四个全新验收库，不需要手工设置连接串。测试在连接前拒绝 3306，并核对服务端端口、实例 UUID 和数据目录，避免连接或修改本机已有数据库。运行参数见 [质量 Gate 说明](tools/QUALITY-GATE.md)。
