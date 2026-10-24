# 第三方组件说明

IconAdapt 使用下列开源组件。项目本身的开源许可证尚未决定；上游组件的许可不因此改变。

| 组件 | 使用方式 | 上游与许可 |
| --- | --- | --- |
| .NET / WPF / Windows Forms | Windows 桌面框架及图标互操作；便携版包含运行时文件 | [dotnet/runtime](https://github.com/dotnet/runtime)、[dotnet/wpf](https://github.com/dotnet/wpf)、[dotnet/winforms](https://github.com/dotnet/winforms)，MIT 及其第三方声明 |
| SkiaSharp 4.153.1 | 图标合成；包含 Windows 原生依赖 | [mono/SkiaSharp](https://github.com/mono/SkiaSharp)，MIT 及其第三方声明 |
| Skia | SkiaSharp 使用的原生图形引擎 | [google/skia](https://github.com/google/skia)，BSD 风格许可及其第三方声明 |

分发文件夹的 `licenses` 子目录保存对应许可证及上游第三方声明。生成包时保留这些文件；它们是授权条款的一部分。

工具不附带 Apple 图标或第三方应用图标包。用户本机应用图标仅用于本地适配，其权利归原作者或权利人所有。
