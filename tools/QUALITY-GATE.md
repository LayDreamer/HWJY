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

以下环境变量必须分别指向可写的隔离 MySQL 8 验收库。测试会创建结构、初始化数据，并执行迁移、回滚和隔离验证，不应使用业务数据库。

| 环境变量 | 用途 |
| --- | --- |
| `ADMINCORE_T4_MYSQL` | T2/T4、迁移与 DP/LOG 验收；库名必须为 `admincore_p0_t4_acceptance`，测试会检查目标库名 |
| `ADMINCORE_HOST_MYSQL` | 完整 Host 主库 |
| `ADMINCORE_HOST_MYSQL_LOG` | 完整 Host 与 DP/LOG 日志库 |
| `ADMINCORE_ACCEPTANCE_MYSQL` | 基座 MySQL 准入验收 |

缺少配置时完整 Gate 退出码为 `2`；构建、检查、测试或报告验证失败为 `1`；通过为 `0`。TRX 必须有执行结果、全部通过且无跳过；缺少指定套件、组织权限验收或日志验收也会失败。验收标签覆盖测试会发现新测试未纳入 Gate 的情况。

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

`.github/workflows/quality-gate.yml` 在 PR、主分支推送和手动触发时，在 `windows-latest` 运行同一完整入口。`tools/ci-mysql.ps1` 从官方 CDN 下载 MySQL 8.4.11 Windows ZIP，在 runner 临时目录初始化独立实例、绑定本机端口 33306 并创建四个验收库。数据库启动、子进程 Gate 和 `finally` 清理位于同一 PowerShell 步骤，保持 Windows 父进程存活，避免跨步骤丢失数据库进程；另有 `always()` 清理步骤兜底。随机密码仅用于本次实例，经过 GitHub 日志遮罩后传入验收；始终停止本次启动的进程。此流程不注册系统服务、不连接开发者现有数据库。使用锁文件安装前端依赖，保留检查日志/TRX/MySQL 启动日志 14 天以及通过检查的前端构建产物 7 天。

若要强制阻止不合格 PR 合并，仓库管理员需在 GitHub 分支保护中将 **Full Quality Gate** 设为必需检查；仅添加 workflow 不会自动启用分支保护。

## 依赖漏洞审计

恢复依赖后运行 `pwsh -NoProfile -File tools/run-dependency-audit.ps1`。脚本向官方 NuGet/npm 源查询直接及传递依赖，报告写入 `artifacts/quality/nuget-audit.json` 和 `npm-audit.json`。高危、严重漏洞或无法获得完整报告均阻断；中低危结果保留在报告中。Windows CI 同样执行该步骤，不使用漏洞忽略清单。

安全版本固定在后端 Core 的 PackageReference 和前端 `pnpm-workspace.yaml` 的定向 overrides 中。旧 SVG 插件依赖无修复版本的 braces 与旧解析器，已替换为本仓库的 SVGO 图标构建入口，保留 `virtual:svg-icons-register`、`icon-*` 名称与嵌套目录命名。升级后的 jsPDF 由打印组件真实解析路径测试其图片、分页和 PDF 输出 API。

Core 直接固定 `log4net` 2.0.17，覆盖对象存储依赖 `BceSdkDotNetCore` 引入的 2.0.8，修复其严重 XML 外部实体漏洞并保持 2.x 兼容。此版本仍有中危 XML 日志事件丢失公告；与前端中危结果一同保留，按本次高危/严重修复范围不忽略、不宣称全部漏洞清零。

Excel 引擎采用 MIT 许可的 ClosedXML 0.105.1，移除 Magicodes.IE.Excel/EPPlus 与 ImageSharp，不需要 Six Labors 图片许可证。保留 Magicodes.IE.Core 的列注解、导入结果和业务错误回调；字典 DTO 改用其通用 Importer/Exporter 注解。`ExcelWorkbook` 覆盖当前仓库使用的模板、中文布尔映射、必填/类型/重复校验、错误注释、导出列顺序、Light9 样式、日期格式和内嵌图片，`IEHelper` 保留下载文件名、XLSX MIME、缓存期限和回调契约。此适配不宣称实现原 Magicodes Excel 的所有高级扩展功能；新增导入导出场景应补充对应回归。
