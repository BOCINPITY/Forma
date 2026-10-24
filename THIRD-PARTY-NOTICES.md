# 第三方组件说明

Forma（原 IconAdapt）以 [MIT 许可证](LICENSE) 开源，并使用下列开源组件。上游组件保留各自的许可。

| 组件 | 使用方式 | 上游与许可 |
| --- | --- | --- |
| .NET / WPF / Windows Forms | Windows 桌面框架及图标互操作；便携版包含运行时文件 | [dotnet/runtime](https://github.com/dotnet/runtime)、[dotnet/wpf](https://github.com/dotnet/wpf)、[dotnet/winforms](https://github.com/dotnet/winforms)，MIT 及其第三方声明 |
| SkiaSharp 4.153.1 | 图标合成；包含 Windows 原生依赖 | [mono/SkiaSharp](https://github.com/mono/SkiaSharp)，MIT 及其第三方声明 |
| Skia | SkiaSharp 使用的原生图形引擎 | [google/skia](https://github.com/google/skia)，BSD 风格许可及其第三方声明 |
| LZMA SDK 26.01 | 高压缩 EXE 的解压启动模块；替换图标、版本和普通权限清单资源 | [7-Zip LZMA SDK](https://www.7-zip.org/sdk.html)，公共领域；与 Forma 的 MIT 许可兼容 |

分发文件夹的 `licenses` 子目录保存对应许可证及上游第三方声明。生成包时保留这些文件；它们是授权条款的一部分。

工具不附带 Apple 图标或第三方应用图标包。用户本机应用图标仅用于本地适配，其权利归原作者或权利人所有。

## Third-party notices

Forma (formerly IconAdapt) is released under the [MIT license](LICENSE). Its dependencies retain their own licenses:

| Component | Usage | Upstream and license |
| --- | --- | --- |
| .NET / WPF / Windows Forms | Windows desktop framework and icon interop; the portable package includes runtime files | [dotnet/runtime](https://github.com/dotnet/runtime), [dotnet/wpf](https://github.com/dotnet/wpf), [dotnet/winforms](https://github.com/dotnet/winforms); MIT and their third-party notices |
| SkiaSharp 4.153.1 | Icon composition, including native Windows dependencies | [mono/SkiaSharp](https://github.com/mono/SkiaSharp); MIT and its third-party notices |
| Skia | Native graphics engine used by SkiaSharp | [google/skia](https://github.com/google/skia); BSD-style license and its third-party notices |
| LZMA SDK 26.01 | Decompression launcher for the compressed EXE; icon, version and asInvoker manifest resources are replaced | [7-Zip LZMA SDK](https://www.7-zip.org/sdk.html), public domain; compatible with Forma's MIT license |

The `licenses` directory contains the original upstream license texts and third-party notices. Preserve these files when redistributing the portable package. The runtime notices correspond to .NET 10.0.13 (SDK 10.0.402); update the notices when updating the bundled runtime or dependencies.

Forma does not distribute Apple icons or third-party application icon packs. Icons extracted from locally installed applications remain the property of their respective owners.
