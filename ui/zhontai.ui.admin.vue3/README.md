# HWJY 管理前端

本目录是 HWJY 的 Vue 3 管理前端，基于 TypeScript、Vite、Element Plus、Vue Router 和 Pinia，配合系统后端提供后台管理界面。

项目范围及后端启动方式见 [根 README](../../README.md)，完整验收要求见 [质量 Gate 说明](../../tools/QUALITY-GATE.md)。

## 开发环境

- Node.js：`package.json` 声明 `^20.19.0 || ^22.13.0 || >=24.0.0`。推荐使用与 CI 一致的 **24.x**；其他声明允许的版本不代表已经过当前 CI 验证。
- pnpm：固定使用 **11.25.0**，与 `packageManager` 和 CI 一致。
- Windows 开发与质量门禁环境使用 **PowerShell 7**，确保 `pwsh` 位于 PATH。`pnpm build` 实际调用 `pwsh -NoProfile -File tools/build.ps1`，Windows PowerShell 5.1 的 `powershell.exe` 不能替代此入口。

安装 Node.js 24.x 和 PowerShell 7 后，在 PowerShell 7 中检查工具版本；已有正确版本 pnpm 时跳过安装命令：

```powershell
npm install --global pnpm@11.25.0
node --version
pnpm --version
pwsh -NoProfile -Command '$PSVersionTable.PSVersion'
```

## 获取与启动

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

开发服务器默认端口为 `9011`（由 `.env` 的 `VITE_PORT` 配置），请以终端实际输出的访问地址为准。开发 API 地址由 `.env.development` 的 `VITE_API_URL` 配置，当前为 `http://localhost:18010`；使用系统功能前需按根 README 启动后端。

## 构建与验证

以下命令均在 `ui/zhontai.ui.admin.vue3` 目录执行。开发服务持续占用当前终端，可另开终端或先停止服务。

生产构建：

```powershell
pnpm build
```

构建入口会校验路径、清理并重建本目录下的 `dist/`，不要在其中存放需要保留的文件。

与现有质量门禁一致的前端检查顺序：

```powershell
pnpm lint
pnpm typecheck
pnpm test
pnpm build
```

前端检查通过不等于完整质量门禁通过；后端和隔离 MySQL 验收按 [质量 Gate 说明](../../tools/QUALITY-GATE.md) 执行。

按需维护：`pnpm lint-fix`、`pnpm format` 会修改文件；`pnpm gen:api` 根据接口文档生成 API 代码。运行后检查变更范围，不将这些命令作为常规验证步骤。
