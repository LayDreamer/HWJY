# Company 企业模块

此目录承载基于 Admin.Core 的企业业务扩展。各业务模块保持独立，复用既有 Admin 身份、权限、文件、消息和日志能力，不修改 `ZhonTai.Admin.Core`。

当前目录内容：

- `Company.Platform`：企业公共能力边界规划（Identity / Permission / File / Message / Logging），仅建立目录规划，复用既有 Admin.Core 能力，不重复实现。
- `Company.MasterData`：历史冒烟夹具，只读追溯，不接入正式运行面。

不预建业务模块目录：PMC、APS、MES 等模块在业务范围确认（P0-1）并通过 P0-2/P0-3 后，按真实对象创建，不提前占位。
