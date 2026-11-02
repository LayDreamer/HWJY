# 本地图标资源

资源随前端发布，运行时不请求字体 CDN。保持原有 iconfont 和 Font Awesome 4.7.0 类名，避免影响菜单及图标选择器。

- `iconfont/`：来自原项目已使用的 iconfont 项目 2298093，固定资源 `font_2298093_rnp72ifj3ba`，原地址 `https://at.alicdn.com/t/c/font_2298093_rnp72ifj3ba.css`。保留原 CSS 的项目标识和字形定义。
- `font-awesome/`：Font Awesome 4.7.0，来自 FortAwesome 官方仓库对应标签及原 CDN 的同版本 CSS，作者 Dave Gandy。字体采用 SIL OFL 1.1，CSS 采用 MIT；保留原 CSS 许可头和 `UPSTREAM-README.md` 的原始许可说明。

仅将两套 CSS 的字体地址替换为同目录 WOFF2，不修改字形。本系统支持的现代浏览器无需旧 EOT、TTF 或 SVG 字体回退。
