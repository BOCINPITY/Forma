# ADR 0001：采用 Windows 原生桌面技术栈

- 日期：2026-10-24
- 状态：已采纳，开发预览版完成构建与模块验证

## 背景

产品只面向 Windows，主要能力是本地图标合成与桌面快捷方式管理，需要调用 Windows Shell 接口，同时提供直观的图标预览和批量操作界面。

## 决策

采用 C# / .NET 10、WPF / XAML。图像引擎采用 SkiaSharp 4.153.1，Shell 通过 COM 和必要的 Win32 互操作访问。配置及操作记录使用 JSON，不引入数据库或服务端。

.NET 10 采用稳定 SDK，global.json 允许在 10.0 系列中使用较新的 feature band。项目本地 SDK 为 10.0.402。第三方包精确固定版本，同时提交 packages.lock.json。

## 理由与代价

- C# 适合将文件操作、图像处理和 Windows 互操作放在同一应用中。
- WPF 可自定义界面样式；现代视觉效果需要自行设计和实现。
- SkiaSharp 适合图像合成，但会增加原生依赖及发布验证工作。
- COM 线程模型、资源释放及图标缓存仍需要专项验证。

## 考虑过的方案

- WinUI 3：可提供另一种 Windows UI 路径，目前优先保持第一版工程与部署方式简单。
- Web UI 桌面封装：界面开发可行，但仍需额外承接 Windows Shell 互操作，目前不作为首选。
- C++：可直接使用系统接口，目前无需为整个应用承担其工程复杂度。

跨平台不是当前目标；出现明确需求时重新评估。

参考：[.NET 支持政策](https://dotnet.microsoft.com/en-us/platform/support/policy)。
