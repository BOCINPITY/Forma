# 贡献指南

[English](#english) · [项目首页](https://github.com/BOCINPITY/Forma)

欢迎提交问题报告、设计改进和修复。请先看 [支持范围](docs/product.md) 与 [开发指南](docs/development.md)，大的功能建议先开 Issue 说明使用场景。

## 提交问题

说明 Windows 版本、Forma 版本、预览尺寸、图标风格、复现步骤及预期结果。画质问题请附实际 32 / 48px 对比，而不只放一张大图。可用自绘测试素材复现；截图与日志请去除个人路径、启动参数和私人应用信息，不上传真实快捷方式、备份或操作记录。

## 开发与 PR

1. Fork 后创建一个聚焦单一问题的分支，在 Windows / PowerShell 中开发。
2. 固定依赖版本，保留 lock 文件；修改视觉或用户行为时同步中英文文档与 CHANGELOG。
3. 运行与变更相关的检查。发布前使用 `scripts\check.ps1`；文档小改不必重复运行全部图像 / COM 测试。
4. 界面改动附合成数据截图，说明验证结果和未验证的环境。不要提交 `.local`、`artifacts` 或任何用户数据。

预览应只读；写入前必须备份，不改变快捷方式启动属性。保持冲突检测和可恢复记录，不自动提权、重启 Explorer 或清理全局缓存。应用标志及新增演示图标使用代码绘制，不分发未经授权的图标素材。提交贡献即表示同意按本项目 MIT 协议授权你的贡献；第三方材料仍需保留原有许可。

## English

Bug reports, design improvements and focused fixes are welcome. Read the [scope](docs/product.md) and [development guide](docs/en/development.md); open an issue to discuss substantial features first.

Include your Windows and Forma versions, preview size, style, reproduction steps and expected behavior. For quality reports, include actual 32 / 48px comparisons. Prefer synthetic reproductions and remove personal paths, launch arguments and private application details from screenshots or logs. Do not upload real shortcuts, backups or operation records.

Create a focused branch from your fork and use Windows / PowerShell. Keep dependency versions and lock files synchronized. Update both language guides and the changelog when behavior changes. Run checks relevant to the change, using `scripts\check.ps1` before releases; small documentation edits do not require rerunning all image and COM tests. UI changes should include synthetic-data screenshots and clear validation limits. Do not commit `.local`, `artifacts` or user data.

Preview must remain read-only. Back up before writes, preserve shortcut launch properties, detect conflicts and retain recoverable records. Do not introduce automatic elevation, Explorer restarts or global cache deletion. Draw the app mark and new demo icons in code; do not redistribute unauthorized artwork. Contributions are licensed under the project's MIT License; third-party materials retain their own terms.
