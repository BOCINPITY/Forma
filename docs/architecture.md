# 技术架构

[文档导航](README.md) · [English](en/architecture.md)

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

preview.6 保留上述结构，重做浅色工具栏、内缩面板和成组控件。`BackdropStyle.MacOS` 追加为枚举值 3，原有设置值不变。新安装由 ViewModel 默认选择新模式，已有配置按原样读取，用户可点击“macOS 预设”切换。

preview.7 移除 UI 历史页和批次选择，使用 `RestoreDefaultsAsync` 恢复当前数据根全部活动路径。服务在同一 STA 任务及跨实例锁内共享记录，按新到旧恢复并修复父层指纹；某路径冲突后阻止该路径的更早层，其他路径继续。结果按初始活动路径去重，仅全部层恢复才计为成功；取消返回部分结果，日志保留以供重试。原 `RestoreAsync(batchId)` 继续作为底层兼容接口。ViewModel 在恢复后重新扫描刷新指纹，并在当前页展示完整错误；不删除旧备份或迁移数据。

preview.8 在 App 层加入 `Controls/Motion`，集中管理透明度、开关位移、箭头旋转与展开高度。模板触发器只设置目标，动画使用当前显示值接续并替换旧时钟，完成 / 卸载时清理；禁用动效时立即收敛。`NativePixelImage.AnimateSourceChanges` 只在检查器启用，原始绘图及 Imaging 渲染管线保持不变。

UI 测试加载真实 App 资源时，使用仅向测试程序集开放的构造入口关闭正式 `OnStartup`。WPF 即使未调用 `Application.Run` 也会排队启动回调，因此不能仅靠不运行消息循环来隔离服务；测试会核对该回调已被拦截及没有额外窗口。

preview.9 在左侧工作区加入 `DesktopPreview`，与图标库共用 `ShortcutItem`、检查器焦点及预览尺寸命令。控件维护独立的勾选项集合，不修改图标库的默认集合视图；监听扫描集合与勾选变化，卸载时解除订阅。`DesktopImage` 优先使用当前导出帧，否则回退原图并提供明确状态；回退原图禁止放大。背景由 `DesktopBackdrop` 用 WPF 渐变与路径绘制，不访问壁纸或 Shell 布局。切换背景、原始 / 适配、尺寸仅改变显示，不重新生成 ICO。

## 处理流程

桌面扫描 → 快捷方式解析 → 图标提取 → 可见边界分析 → 缩放与合成 → 生成预览及多尺寸 ICO → 备份及记录 → 更新快捷方式图标引用 → 通知 Shell → 展示结果。

渲染在后台执行。Shell COM 调用使用专门的 STA 工作线程和串行队列。WPF 更新返回 UI 上下文，跨线程图像解码后冻结。原生图标句柄与图像对象按来源正确释放。preview.3 直接读取原生图标组 / ICO，保留实际尺寸及各尺寸最佳帧，失败才回退 Shell 并提示来源限制。

Core 的 IconFrame / SourceFrames 保留原生像素；Imaging 按实际可见跨度为输出选择合适帧，用预乘 Alpha 面积采样缩小。12 个输出尺寸含常见缩放倍率的中间帧。底板和前景整体按圆角裁切一次，阴影独立。App 直接解码输出帧，由 NativePixelImage 按物理像素绘制；PixelImageGeometry 处理 DPI 与边缘对齐，manifest 声明 PerMonitorV2，实际多屏行为仍待验证。详见 [清晰度设计](icon-quality.md)。

`MacIconMaterial` 在原图解码后分析可见轮廓，preview.9 使用网格内实际 Alpha 面积、边缘覆盖和变化区分已有底板、圆形标志、透明标志及细线 / 复杂图案，并从颜色直方图估计材质颜色。光学跨度补偿有上限；非底板依据可见轮廓和目标超椭圆计算安全区，各原生帧独立检查，选帧与绘制使用同一有效跨度。形态分析仅在 macOS 模式执行；旧三种模式保留原渲染路径。前景沿用 `PixelResampler`，光照 / 内缘与内容合成后，只应用一次外轮廓裁切。自动分析不具备语义识别能力，不保证所有异形图标都适用。

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
