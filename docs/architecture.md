# 技术架构

本文件描述目标架构。当前仅实现 `IconAdapt.App` 的启动窗口，下面的服务尚未实现。

## 分层

| 模块 | 职责 | 约束 |
| --- | --- | --- |
| App / WPF | 页面、预览、选择状态和操作进度 | 后续采用 MVVM，不在窗口事件中堆放 Shell 操作 |
| Core | 适配参数、渲染规则、批次状态和恢复策略 | 后续独立为类库，避免绑定 UI |
| Windows | 桌面发现、图标提取、Shell COM 和更新通知 | 隔离平台调用及原生资源生命周期 |
| Storage | 配置、输出图标、备份与操作记录 | 持久化失败时不得开始桌面修改 |

待相关功能开始实施时再拆分工程，避免建立无实际用途的空模块。

## 处理流程

桌面扫描 → 快捷方式解析 → 图标提取 → 可见边界分析 → 缩放与合成 → 生成预览及多尺寸 ICO → 备份及记录 → 更新快捷方式图标引用 → 通知 Shell → 展示结果。

渲染在后台执行。Shell COM 调用须遵循对应接口的线程模型，计划用专门的 STA 工作线程处理。UI 更新通过 Dispatcher 返回主线程。原生图标句柄与图像对象必须按来源正确释放。

## 输出与保存

计划根目录：`%LOCALAPPDATA%\IconAdapt\`，使用系统目录 API 获取，不硬编码用户路径。

- `settings.json`：用户配置和主题参数。
- `icons\`：持久图标资源，按源内容与适配参数生成稳定标识。
- `backups\<batch-id>\`：原始快捷方式副本与恢复资料。
- `operations\<batch-id>.json`：带 schemaVersion 的操作记录。
- `logs\`：必要的本地诊断信息。

不把最终 ICO 放在临时目录。缓存清理和卸载设计必须考虑快捷方式仍引用图标文件的情况。

## 修改与恢复

1. 解析真实桌面位置并去重；检查文件存在、格式及写权限。
2. 保存原始图标路径、索引、快捷方式备份及修改前文件指纹；先持久化批次记录。
3. 生成最终 ICO，确认输出可读取，再写入图标引用。
4. 每项分别记录 pending / applied / failed / restored 等状态，失败不掩盖部分成功。
5. 恢复前检查快捷方式是否被其他程序或用户改过。通常只恢复原图标字段；冲突须展示给用户，不静默用整份旧快捷方式覆盖。
6. 保留可用于异常恢复的原始副本；记录文件通过临时文件加替换方式更新，减少截断风险。

更新使用 `IShellLink` 和 `IPersistFile`，再通过 `SHChangeNotify` 通知 Shell。通知不等于图标一定即时刷新，需实机验证，不能承诺所有入口同步变化。不以重启 Explorer 或删除全局图标缓存作为常规流程。

## 待验证风险

- 图标缓存、多 DPI 和不同显示尺寸下的显示结果。
- 桌面重定向、OneDrive 桌面及公共桌面访问权限。
- ICO 编码、多尺寸图像与透明边缘质量。
- 程序升级后快捷方式被重建、目标失效和用户手动编辑。
- 大量扫描时的 UI 响应、取消操作及 COM 错误处理。

## 参考

- [WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [SkiaSharp](https://mono.github.io/SkiaSharp/)
- [IShellLinkW::SetIconLocation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-seticonlocation)
- [修改快捷方式图标后的刷新](https://devblogs.microsoft.com/oldnewthing/20150903-00/?p=91671)
