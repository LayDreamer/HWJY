# 质量 Gate

验收平台为 Windows。前置环境：.NET 10 SDK、pnpm 11.25.0，推荐使用与 CI 一致的 Node.js 24；其他受支持的 Node 版本见前端 `package.json` 的 `engines`。前端依赖通过 `pnpm install --frozen-lockfile` 安装，后端通过 `dotnet restore src/ZhonTai.sln` 恢复。命令从仓库根目录运行。

```powershell
# 完整检查：默认模式，发布前使用
pwsh -NoProfile -File tools/run-quality-gate.ps1 -Mode Full

# 无 MySQL 的本地检查：不能作为发布验收结果
pwsh -NoProfile -File tools/run-quality-gate.ps1 -Mode Local

# 已恢复依赖时可追加 -NoRestore
```

统一入口执行 Gate 报告解析自测、后端 Release 构建与验收、前端 Lint、Vue 类型检查、Node 回归测试和 Vite 构建。日志与每次独立的 TRX 报告保存在 `artifacts/quality/`。单独运行后端使用 `tools/run-p0-gate.ps1`，参数相同。

前端构建入口只清理经路径校验的本项目 `dist` 生成目录，并拒绝符号链接，防止旧依赖的历史 chunk 或压缩文件混入发布产物。

## 完整验收依赖

### 默认开发宿主与 MySQL

`ZhonTai.Admin.Host/ConfigCenter/dbconfig.json` 保留 SQLite 基础默认值和原有初始化行为。仅 Development 环境加载 `dbconfig.Development.json` 后使用 MySQL：主库为 `127.0.0.1:3306/admincore_host`，日志库为 `admincore_host_log`。开发覆盖中自动建库、结构同步、种子同步、种子更新和造数均关闭，只连接现有数据库；原 SQLite 文件保留，不自动迁移数据。

Development 连接示例没有密码。启动前，应从本机凭据管理器或开发终端向**当前进程**注入以下完整连接串，分别对应主库和日志库：

- `DbConfig__ConnectionString`
- `DbConfig__Dbs__0__ConnectionString`

ConfigCenter 先加载基础 `名称.json`，再加载当前环境的 `名称.环境名.json`，同层按文件名排序，排除其他环境后缀文件。Development 宿主最后应用这两个连接覆盖值，凭据不需要写入源码或 `launchSettings.json`。启动命令为 `dotnet run --project src/modules/admin/ZhonTai.Admin.Host/ZhonTai.Admin.Host.csproj`；当前 Kestrel HTTP 端口为 18010，可通过 `/health/ready` 检查两个数据库，`/health/live` 检查进程存活。

开发连接示例中的 `root` 仅是本地连接占位；实际账号随完整连接串注入，日常运行应使用已配置的业务账号。上述修改不等于完成 Production 部署，也不注册或持久化本机密码。其他验收环境继续使用自己的显式隔离配置，不读取这两个 Development 覆盖项。

### 自动化验收连接

`Full` 入口现在自动创建和管理 **`127.0.0.1:33306` 的独立 MySQL 8 实例**。本地和 CI 使用相同入口，不再手工提供验收连接串。每次从全新数据目录初始化四个库，测试会创建结构、初始化种子并执行迁移、回滚和隔离验证；不从 3306 复制业务数据，也不复用其历史验收库。

| 启动器生成的进程环境变量 | 33306 上的库及用途 |
| --- | --- |
| `ADMINCORE_T4_MYSQL` | `admincore_p0_t4_acceptance`：T2/T4、迁移及 LOG 验收 |
| `ADMINCORE_HOST_MYSQL` | `admincore_host`：完整 Host 主库及 DP 验收 |
| `ADMINCORE_HOST_MYSQL_LOG` | `admincore_log`：完整 Host 与 DP 日志库 |
| `ADMINCORE_ACCEPTANCE_MYSQL` | `admincore_acceptance`：基座 MySQL 准入验收 |

验收实例与本机可能已有的 3306 实例使用不同的进程和数据目录；相同 schema 名称不表示同一个数据库。库的归属必须结合地址、端口、实例 UUID 和数据目录判断，不能只看库名。

```powershell
# 单独运行完整后端验收，自动启动和停止 33306
pwsh -NoProfile -File tools/run-p0-gate.ps1 -Mode Full -NoRestore

# 可指定已有的 MySQL 二进制目录，无需安装 Windows 服务
pwsh -NoProfile -File tools/run-p0-gate.ps1 -Mode Full -NoRestore -MySqlBinaryDirectory 'D:/tools/mysql/mysql-8.4.11-winx64/bin'
```

`run-quality-gate.ps1 -Mode Full` 同样自动管理实例。二进制优先取自 `-MySqlBinaryDirectory`，其次取自进程变量 `ADMINCORE_MYSQL_BIN`；均未设置时从官方 CDN 下载固定的 8.4.11 ZIP。显式指定无效目录时直接报错。默认工作目录为 `RUNNER_TEMP` 或系统临时目录下的新 GUID 目录；可用 `-MySqlWorkingDirectory` 指定**尚不存在**的目录，推荐使用不含中文的路径。

防误连检查在启动器和测试入口两层执行：

1. 启动脚本端口只接受 33306；若端口已被占用或工作目录已存在，立即拒绝，不复用现有数据库。初始化后、首次修改账号或建库前，核对服务端真实端口和本次数据目录，并记录实例 UUID。
2. 验收仅使用随机密码的 `admincore_acceptance` 专用账号，授权严格限定四个 schema，无全局数据权限、无授权转授权限；连接必须使用 TCP、`127.0.0.1:33306` 和 `SslMode=Required`。
3. 每个 MySQL 验收入口及共用清理入口在打开连接前校验地址、端口、账号、库白名单及连接角色；3306 在连接前直接拒绝。打开连接后只读核对 `@@port`、`@@server_uuid`、`@@datadir`，与本轮证明不符则拒绝测试写入。不能通过端口转发把 3306 冒充成验收实例。
4. 启动器清除继承的四个验收连接、日常 `DbConfig__*` 主/日志连接、ConfigCenter 路径和宿主环境变量，再注入本轮配置；Host 验收也明确使用自己的 ConfigCenter。环境变量仅在子进程有效，结束后恢复，不写入用户/系统环境变量或源码。
5. `finally` 根据本轮状态中的 PID、可执行文件和进程启动时间停止实例；不会按端口或进程名停止其他 MySQL。失败时也执行该清理。数据目录和日志保留用于排查，下轮使用新目录，不复用旧数据。

恢复后端依赖后，可执行独立的生命周期回归（需提供已安装的 MySQL 8.4 二进制目录）：

```powershell
pwsh -NoProfile -File tools/test-mysql-isolation.ps1 -MySqlBinaryDirectory 'D:/tools/mysql/mysql-8.4.11-winx64/bin'
```

该回归覆盖危险端口、占用端口、已有目录、实例身份、成功及失败清理、未设置与空值环境变量恢复，以及继承错误 3306 配置时的完整后端验收。结束后核对原有 MySQL 监听和进程身份未变；不会认证或查询日常数据库。验证结果和日志写入新的临时证据目录，可用 `-EvidenceDirectory` 指定尚不存在的目录。

`-ManagedMySql` 是启动器内部参数，要求连接及实例证明齐全；直接调用该内部入口缺少配置时退出码为 `2`。实例启动、构建、测试或报告验证失败为 `1`，通过为 `0`。TRX 必须有执行结果、全部通过且无跳过；缺少防误连套件、组织权限验收或日志验收也会失败。验收标签覆盖测试会发现新测试未纳入 Gate 的情况。单独 `dotnet test` 不能通过自行设置一个库名绕过实例检查，完整验收应使用上述入口。

迁移清单校验 SQL 文件的原始字节。`.gitattributes` 对 `database/mysql/**/*.sql` 禁用换行转换，保留已批准制品的字节与校验和；编辑器也不得自动转换这些文件的换行。Windows 的 `core.autocrlf` 不应改变迁移制品，清单及数据库迁移历史中的校验和保持一致。

`Local` 显式排除 `P0Dependency=MySql` 的验收。旧 Material 冒烟夹具和使用机器默认 Host 配置的旧 `SchedulerIsolationTest` 不属于此 Gate；调度持久化和 API/Worker 隔离分别由隔离 SQLite 准入测试与 T4 Host 验收覆盖。

## 模块与运行时依赖检查

`RepositoryBoundaryTests` 读取真实 `.csproj` 引用，禁止 Core 反向依赖业务实现、平台依赖业务模块、Contracts 依赖实现、跨模块直接引用实现，以及正式项目依赖旧 MasterData 夹具。正式模块必须进入 Release solution 并生成程序集；检查编译后的类型引用，拒绝访问其他模块的 Entity、Repository、FreeSql 等内部类型。平台 Core 的公共基类和同一模块内部访问保留。旧 MasterData 按其 README 的历史夹具规则排除。

健康检查启用后：

| 默认路径 | 行为 |
| --- | --- |
| `/health`、`/health/ready` | 查询主库及注册的子库；Redis 缓存启用时执行 PING。任一依赖失败返回 503。每项检查超时 5 秒。 |
| `/health/live` | 检查 HTTP 进程存活，独立于外部依赖。 |

路径可通过 `AppConfig.HealthChecks.Path/ReadinessPath/LivenessPath` 配置。检查只执行连接探测，不初始化或迁移数据库；响应不携带连接串和底层异常。

## CI

`.github/workflows/quality-gate.yml` 始终响应 PR、`main/master/dev` 推送和手动触发，在 `windows-latest` 报告现有必需检查 **Full Quality Gate**。该名称为兼容已有分支保护而保留，实际执行范围以运行摘要为准：

| 模式 | 适用条件 | 实际检查 |
| --- | --- | --- |
| `docs` | 变更仅涉及下述文档白名单 | 对完整变更范围执行 `git diff --check`；不恢复开发依赖、不启动 MySQL、不运行后端或前端构建及依赖审计 |
| `reuse` | 主分支收到普通 PR 合并，且合并内容和成功验收证据均可严格核实 | 复用原 PR 已完成的 Full 与依赖审计；摘要提供来源运行链接，不声称本轮重新运行了 Full |
| `full` | 其他代码或配置变更、手动触发，或无法可靠判断轻量条件 | 完整质量入口及高危/严重依赖审计；分流脚本或本工作流变更、手动运行或范围无法判断时再执行分流回归 |

文档白名单为根目录 `README*.md`、`CHANGELOG.md`、`CONTRIBUTING.md`、`SECURITY.md`，`docs/` 下的 Markdown，`ui/` 下的 `README*.md`，以及 `tools/QUALITY-GATE.md`。`AGENTS.md`、`SKILL.md`、工作流、脚本、认证、权限、数据库、配置、依赖和未知路径仍走 Full；混合变更也走 Full。比较范围包括删除及重命名前的路径，不只检查最后一次提交。新增文档位置不在白名单时保守执行 Full。

主分支去重仅接受可核实的普通双父合并：推送前提交必须为第一父，第二父须为本仓已合并 PR 的 head，第一父已包含在该 head 中，且最终文件树与 head 文件树一致。还需核实该 head 在本仓本工作流上的最新 PR 运行成功，**Run full quality gate** 和 **Audit high and critical dependencies** 两步均实际成功，证据在 24 小时内，且成功步骤记录的已验证源码树与合并树完全一致。该源码树只在 Full 和审计通过、工作树无未提交源码变更时记录，兼容 GitHub 在 PR 合并后返回空关联列表的情况。文档轻量运行的绿色状态不能作为 Full 证明。旧运行没有源码树记录时，以及 Squash/rebase、批量推送、未覆盖的主线变化、过期证明、API 错误或证据不足时均回退 Full；手动触发始终 Full。工作流仅增加读取 PR 和 Actions 运行的权限，不需要写权限。

这些分流只减少重复工作，不改变 `run-quality-gate.ps1 -Mode Full` 的验收内容。Full 由 `run-mysql-acceptance.ps1` 在 runner 临时目录初始化上述独立实例；数据库启动、子进程 Gate 和 `finally` 清理位于同一 PowerShell 生命周期，另有 `always()` 清理兜底。随机密码经过日志遮罩，始终停止本次进程，不注册系统服务、不连接开发者现有数据库。Full 仍使用锁文件安装前端依赖，保留日志/TRX/MySQL 启动日志 14 天及通过检查的前端构建产物 7 天。

分流回归可单独运行：`pwsh -NoProfile -File tools/test-quality-gate-plan.ps1`。日常开发先运行与改动相关的检查；只有代码、配置、环境或验收范围变化使已有证据失效时，才补充相应验证。

若要强制阻止不合格 PR 合并，仓库管理员需在 GitHub 分支保护中将 **Full Quality Gate** 设为必需检查；仅添加 workflow 不会自动启用分支保护。

### NuGet 缓存与 PR 收尾

Full 模式使用 `actions/cache` 缓存 `~/.nuget/packages` 下载包，缓存键包含操作系统、架构和项目/依赖配置文件的内容哈希；依赖变化时可从同平台旧缓存补充恢复。命中缓存后仍执行 `dotnet restore`、完整验收和依赖审计，不缓存配置凭据、数据库、测试结果或构建产物。`docs` 和 `reuse` 模式不恢复或保存 NuGet 缓存。

MySQL 清理在核实所属进程退出且 33306 无监听后，将活动证明 `process.json` 归档为 `process.stopped.json`；重复 Stop 不再读取已完成证明中的旧 PID，避免 PID 被其他进程复用时误报失败。活动实例仍严格校验端口、数据目录、可执行文件和进程启动时间，校验或清理失败时保留活动证明并报错。

GitHub 的 PR 缓存作用域不能供其他 PR 直接复用。首次启用后，在默认分支通过现有 `workflow_dispatch` 手动执行一次 Full，建立后续 PR 可读取的默认分支缓存；之后默认分支实际执行 Full 时会按需更新缓存。缓存未命中或被淘汰时按正常恢复流程下载依赖。

仓库设置启用 **Allow auto-merge** 和 **Automatically delete head branches**。只有已完成审查、获准合并的 PR 才单独启用自动合并，选择普通 **Merge**，待现有分支保护要求全部满足后合并；仓库允许自动合并并不等于所有 PR 都会自动合并，也不绕过 **Full Quality Gate**。普通合并继续按上述严格条件复用已验证文件树。

合并后由 GitHub 自动清理源分支。本地同步先保留未提交改动，再获取远端更新并快进 `main`；确认已合并且没有额外提交或其他工作树占用后，使用 `git branch -d <分支名>` 清理本地分支。未合并分支继续保留。

## 依赖漏洞审计

恢复依赖后运行 `pwsh -NoProfile -File tools/run-dependency-audit.ps1`。脚本向官方 NuGet/npm 源查询直接及传递依赖，报告写入 `artifacts/quality/nuget-audit.json` 和 `npm-audit.json`。高危、严重漏洞或无法获得完整报告均阻断；中低危结果保留在报告中。Windows CI 同样执行该步骤，不使用漏洞忽略清单。

安全版本固定在后端 Core 的 PackageReference 和前端 `pnpm-workspace.yaml` 的定向 overrides 中。旧 SVG 插件依赖无修复版本的 braces 与旧解析器，已替换为本仓库的 SVGO 图标构建入口，保留 `virtual:svg-icons-register`、`icon-*` 名称与嵌套目录命名。升级后的 jsPDF 由打印组件真实解析路径测试其图片、分页和 PDF 输出 API。

Core 直接固定 `log4net` 2.0.17，覆盖对象存储依赖 `BceSdkDotNetCore` 引入的 2.0.8，修复其严重 XML 外部实体漏洞并保持 2.x 兼容。此版本仍有中危 XML 日志事件丢失公告；与前端中危结果一同保留，按本次高危/严重修复范围不忽略、不宣称全部漏洞清零。

Excel 引擎采用 MIT 许可的 ClosedXML 0.105.1，移除 Magicodes.IE.Excel/EPPlus 与 ImageSharp，不需要 Six Labors 图片许可证。保留 Magicodes.IE.Core 的列注解、导入结果和业务错误回调；字典 DTO 改用其通用 Importer/Exporter 注解。`ExcelWorkbook` 覆盖当前仓库使用的模板、中文布尔映射、必填/类型/重复校验、错误注释、导出列顺序、Light9 样式、日期格式和内嵌图片，`IEHelper` 保留下载文件名、XLSX MIME、缓存期限和回调契约。此适配不宣称实现原 Magicodes Excel 的所有高级扩展功能；新增导入导出场景应补充对应回归。
