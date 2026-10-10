# 源码构建

本项目是 [ImproveGame（更好的体验）](https://github.com/487666123/ImproveGame/tree/1.4.4) 的 TerrariaModder 移植版，实现的功能以 README 为准。原项目采用 MIT 许可证，版权声明见 LICENSE。

需要 Windows、.NET SDK、.NET Framework 4.8、XNA Framework 4.0，以及 Terraria 1.4.5.8 和 TerrariaModder 0.4.1。

1. 在 `QualityOfTerraria.csproj` 中将 Terraria、TerrariaModder.Core 和 Harmony 的 `HintPath` 改为自己的安装路径。XNA 引用默认使用 Windows GAC 路径。
2. 将自己游戏中的 ReLogic 程序集放到 `lib/ReLogic.dll`。Terraria.exe 包含资源 `Terraria.Libraries.ReLogic.ReLogic.dll`，可通过程序集资源提取工具导出。游戏与加载器二进制依赖不随本仓库分发。
3. 运行 `dotnet build -c Release`，输出为 `bin/Release/QualityOfTerraria.dll`。
4. 将输出 DLL、`manifest.json` 和 `icon.png` 放到游戏的 `TerrariaModder/mods/quality-of-terraria/` 目录。

`build.ps1` 会构建并部署；使用前请修改其中的目标游戏路径。`tests/` 中的回归脚本部分依赖本机游戏路径，需要按安装位置调整；XNA 行为测试使用 32 位 Windows PowerShell。
