# 技术架构

当前开发预览版已实现以下模块，采用共享接口连接。桌面真实显示效果仍需人工验证。

## 分层

| 模块 | 职责 | 约束 |
| --- | --- | --- |
| IconAdapt.App | WPF 页面、MVVM、前后预览、设置和操作进度 | 后台渲染、防抖、取消及关闭等待 |
| IconAdapt.Core | 快捷方式、风格、批次模型与服务接口 | 不依赖 WPF 或 Shell |
| IconAdapt.Imaging | SkiaSharp 边界分析、图像合成和 ICO 编码 | 无 UI 线程依赖，原生图像资源按调用释放 |
| IconAdapt.Windows | 桌面发现、图标提取、STA COM、持久化与恢复 | 串行队列与跨实例存储锁，写前日志 |

存储服务当前与 Windows 快捷方式事务放在同一类库，主题设置由 App 保存。

preview.4 的界面采用 WPF WindowChrome、顶部导航、紧凑网格、固定检查器和底部操作栏。控件主题在 App.xaml 中统一定义；ViewModel 提供简短状态与完整诊断，错误严重性依据实际结果判定。界面重构不改变图像编码、备份格式或 Shell 写入事务。

preview.5 产品名和应用程序集名更改为 Forma。源码命名空间与 `%LOCALAPPDATA%\IconAdapt\` 存储目录沿用原名，避免更名打断已有 ICO 引用和恢复链。字体资源集中在 App.xaml，应用标志通过代码几何生成。

## 处理流程

桌面扫描 → 快捷方式解析 → 图标提取 → 可见边界分析 → 缩放与合成 → 生成预览及多尺寸 ICO → 备份及记录 → 更新快捷方式图标引用 → 通知 Shell → 展示结果。

渲染在后台执行。Shell COM 调用使用专门的 STA 工作线程和串行队列。WPF 更新返回 UI 上下文，跨线程图像解码后冻结。原生图标句柄与图像对象按来源正确释放。preview.3 直接读取原生图标组 / ICO，保留实际尺寸及各尺寸最佳帧，失败才回退 Shell 并提示来源限制。

Core 的 IconFrame / SourceFrames 保留原生像素；Imaging 按实际可见跨度为输出选择合适帧，用预乘 Alpha 面积采样缩小。12 个输出尺寸含常见缩放倍率的中间帧。底板和前景整体按圆角裁切一次，阴影独立。App 直接解码输出帧，由 NativePixelImage 按物理像素绘制；PixelImageGeometry 处理 DPI 与边缘对齐，manifest 声明 PerMonitorV2，实际多屏行为仍待验证。详见 [清晰度设计](icon-quality.md)。

## 输出与保存

默认根目录：`%LOCALAPPDATA%\IconAdapt\`，使用系统目录 API 获取，不硬编码用户路径。测试可通过 `--data-root` 指定独立目录。

- `settings.json`：用户配置和主题参数。
- `icons\`：以批次 ID 和项目序号保存持久图标资源，避免 Shell 使用旧缓存。
- `backups\<batch-id>\`：原始快捷方式副本与恢复资料。
- `operations\<batch-id>.json`：带 schemaVersion 的操作记录。
- `operations.lock`：跨进程操作锁。

当前没有独立诊断日志；操作错误保存在批次记录和界面。截图自动化失败会写出独立错误文件。

不把最终 ICO 放在临时目录。缓存清理和卸载设计必须考虑快捷方式仍引用图标文件的情况。

## 修改与恢复

1. 解析真实桌面位置并去重；检查文件存在、格式及写权限。
2. 保存原始图标路径、索引、快捷方式备份及修改前文件指纹；先持久化批次记录。
3. 生成最终 ICO，确认输出可读取，再写入图标引用。
4. 每项分别记录 queued / pending / applied / failed / cancelled / restoring / restored / conflict 状态；修改后状态不确定时保留可恢复状态，失败不掩盖部分成功。
5. 恢复前检查快捷方式是否被其他程序或用户改过。通常只恢复原图标字段；冲突须展示给用户，不静默用整份旧快捷方式覆盖。
6. 保留可用于异常恢复的原始副本；操作记录先 Flush(true)，再通过临时文件加替换方式更新。快捷方式先在同目录副本中修改、检查属性和源 hash，再原子替换。

恢复较新批次后同步更新直接上层批次的有效指纹，支持连续适配的逆序恢复。扫描已适配项目时沿合法备份链提取原始图案，避免重复套框。记录有 schema、ID、大小、hash 与存储路径检查；不接受设备路径、备用数据流或符号链接跳转。

更新使用 `IShellLink` 和 `IPersistFile`，再通过 `SHChangeNotify` 通知 Shell。通知不等于图标一定即时刷新，需实机验证，不能承诺所有入口同步变化。不以重启 Explorer 或删除全局图标缓存作为常规流程。

preview.2 修正属性比较：允许 Shell 规范化 HasRelativePath 结构位，仍严格检查绝对目标、PIDL 和所有已检查的实际启动属性及其他行为位。验证失败仅列出字段名称，不把启动参数写入错误消息。访问被拒绝时给出中文权限建议，界面也兼容旧英文错误；仅对确认 applied 的项目显示更新成功提示。

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
