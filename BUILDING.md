# 源码构建

本项目是 [ImproveGame（更好的体验）](https://github.com/487666123/ImproveGame/tree/1.4.4) 的 TerrariaModder 移植版，实现的功能以 README 为准。原项目采用 MIT 许可证，版权声明见 LICENSE。

需要 Windows、.NET SDK、.NET Framework 4.8、XNA Framework 4.0，以及 Terraria 1.4.5.8 和 TerrariaModder 0.4.1。

1. 在 `QualityOfTerraria.csproj` 中将 Terraria、TerrariaModder.Core 和 Harmony 的 `HintPath` 改为自己的安装路径。XNA 引用默认使用 Windows GAC 路径。
2. 将自己游戏中的 ReLogic 程序集放到 `lib/ReLogic.dll`。Terraria.exe 包含资源 `Terraria.Libraries.ReLogic.ReLogic.dll`，可通过程序集资源提取工具导出。游戏与加载器二进制依赖不随本仓库分发。
3. 运行 `dotnet build .\QualityOfTerraria.csproj -c Release` 构建客户端 DLL，再运行 `.\build-server.ps1` 构建服务器模块；输出分别为 `bin/Release/QualityOfTerraria.dll` 和 `bin/Release/QualityOfTerraria.Server.bin`。
4. 将客户端 DLL、服务器模块、`manifest.json` 和 `icon.png` 放到游戏的 `TerrariaModder/mods/quality-of-terraria/` 目录。启动专用服务器时，客户端 DLL 会自动加载同目录的服务器模块。

`build.ps1` 会先构建两种运行模块再部署；使用前请修改其中的目标游戏路径。单独运行 `dotnet build .\QualityOfTerraria.csproj -c Release` 只构建客户端，完整发布还需运行 `build-server.ps1`。构建不会自动推送或发布。联机发布包需要房主／专用服务器和所有玩家安装同版本；专用服务器目录还需包含 `QualityOfTerraria.Server.bin`，本版 manifest 的 `multiplayer` 为 `required`。

## 回归检查

本轮快捷收纳、标记箱效果、倍率设置与世界控制回归：构建客户端后执行 `pwsh -NoProfile -File tests/PortableStorage.Tests.ps1`，再执行 `pwsh -NoProfile -File tests/RemoteStorage.NonHost.Tests.ps1`。前者使用真实 Terraria/Core/生产程序集验证窗口保活、药水阈值、远程制作站、信息配件、原版服务器扣料、配置迁移、时间边界及房主权限；捕获网络和音效，不代替真实绘制、输入法及双客户端验收。

非房主终极存储回归（2026-10-20）：构建客户端后运行 `pwsh -NoProfile -File tests/RemoteStorage.NonHost.Tests.ps1`。脚本编译 x86 测试程序，使用本机真实 Terraria/Core 和生产 DLL，覆盖普通玩家握手、标记保存、快照物品、远程打开，以及身份、令牌、锁箱和占用保护；网络发送与界面输出被测试捕获，不启动游戏。测试数据仅写入 `bin/nonhost-tests/`。该检查不替代两台实际客户端联机验收。`tests/` 是本地保留文件，不随 GitHub 源码分发。

角色隔离回归也包含在上述脚本中：同安装 ID 的两个角色独立增删、旧列表一次性复制、清空服务端会话后从磁盘恢复、换槽位／安装 ID、空列表不重复迁移、旧客户端缓存不覆盖服务器，以及角色 ID 文件持久化、改名和新角色复用文件名。云端切换只验证 Harmony 挂接，Steam 云端实际读写仍需游戏验收。

`tests/` 中的游戏、Core、Harmony、Mono.Cecil及SDK路径需要按本机安装位置调整。多数脚本可在 PowerShell 7 中执行；依赖真实XNA／Core运行时的脚本使用32位 Windows PowerShell。测试生成物放在忽略的 `bin/` 下，不随发布包分发。

Core 0.4.1 的 `NetSync` 字符串字段单包上限为 255 字节。长 Base64 载荷统一经过 `Networking/NetSyncPayload.cs` 分片与重组，终极存储、工具、连锁挖矿、结构定位和设置同步不得直接调用底层存取方法。分片回归使用 `tests/NetSyncPayload.Tests.ps1`。

先构建生产DLL，再运行相关脚本。以下列出 v1.0.1 与 v1.1.0 的主要检查：

| 检查范围 | 脚本 |
| --- | --- |
| 配置范围、默认值与旧布尔迁移 | `V110.WorldConfig.Tests.ps1` |
| 晶塔、感染、钱币、旗帜、红药水、复活与倍率行为 | `V110.WorldBehavior.Tests.ps1`、`WorldQoL.Tests.ps1` |
| 真实原版IL契约与组合补丁挂接 | `V110.WorldContracts.Tests.ps1`、`V110.WorldNative.Tests.ps1`、`AllPatches.Native.Tests.ps1` |
| 护士收费与原版入口 | `Nurse.Behavior.Tests.ps1`、`Nurse.Native.Tests.ps1` |
| 存储交易、重复请求、身份及断线恢复 | `RemoteStorage.Transactions.Tests.ps1`、`RemoteStorage.Protocol.Tests.ps1`、`Review.RemoteStorage.MarkerIdentity.Tests.ps1`、`Review.RemoteStorage.Disconnect.Tests.ps1` |
| 事务记录、鼠标物品与库存冻结 | `StorageTransactionJournal.Tests.ps1`、`StorageTransactionJournal.Native.Tests.ps1`、`StorageInventoryGuard.Tests.ps1`、`StorageCoins.Pending.Tests.ps1` |
| 设置申请、保存核验与单机世界配置 | `SettingsSync.Tests.ps1`、`SettingsSyncCore.Tests.ps1`、`SettingsSyncSaveCore.Tests.ps1`、`LocalWorldSettings.Tests.ps1` |
| 设置列表、刷怪滑条与旅商按钮 | `SettingsSyncUI.Tests.ps1`、`SpawnRateUI.Tests.ps1`、`TravelShopUI.Tests.ps1`、`WorldUIClick.Tests.ps1`、`WorldUIClick.Native.Tests.ps1`、`PatchLifecycle.Tests.ps1`、`UICompatibility.Native.Tests.ps1`、`VanillaUIMouseState.Native.Tests.ps1` |
| 关闭面板残留鼠标边界、专服运行与网络帧更新 | `WorldSession.Tests.ps1`、`ServerRuntime.Native.Tests.ps1`、`ServerUpdate.Tests.ps1` |
| 远程快照分发、地图定位游标与自动钓鱼间隔 | `RemoteStorage.Dispatch.Tests.ps1`、`WorldQoL.Tests.ps1` |
| 建造、液体与世界工具请求 | `ToolNetwork.Behavior.Tests.ps1`、`ToolNetwork.Native.Tests.ps1`、`Tools.Behavior.Tests.ps1`、`Tools.Contracts.Tests.ps1` |
| 连锁挖矿、设置迁移、速度级别与按键 | `VeinMining.Behavior.Tests.ps1`、`VeinMiningNetwork.Tests.ps1`、`VeinSpeed.Behavior.Tests.ps1`、`VeinIntegration.Input.Tests.ps1` |
| 结构定位、测试假人与功能网络 | `FeatureNetwork.Behavior.Tests.ps1`、`FeatureNetwork.Native.Tests.ps1`、`FeatureNetwork.Dependencies.Tests.ps1` |
| 既有存储、UI和生命周期回归 | `Storage.Tests.ps1`、`StorageBehavior.Tests.ps1`、`StorageUI.Tests.ps1`、`StorageCrafting.Tests.ps1`、`StorageIcon.Tests.ps1`、`StorageIcon.Drawing.Tests.ps1`、`PanelLayout.Tests.ps1`、`QuickPlacePreview.Tests.ps1`、`WorldSession.Tests.ps1`、`ModLifecycle.Tests.ps1`、`StartupLoadOrder.Tests.ps1` |

例如在项目根目录执行独立行为检查：

```powershell
dotnet build .\QualityOfTerraria.csproj -c Release
.\tests\V110.WorldConfig.Tests.ps1
.\tests\V110.WorldBehavior.Tests.ps1
.\tests\WorldQoL.Tests.ps1
.\tests\RemoteStorage.Transactions.Tests.ps1
.\tests\SettingsSync.Tests.ps1
.\tests\ToolNetwork.Behavior.Tests.ps1
.\tests\VeinMining.Behavior.Tests.ps1
.\tests\VeinMiningNetwork.Tests.ps1
.\tests\VeinSpeed.Behavior.Tests.ps1
.\tests\VeinIntegration.Input.Tests.ps1
.\tests\FeatureNetwork.Behavior.Tests.ps1
```

真实Core／XNA运行时检查可明确选择32位PowerShell；Native脚本会构建net48 x86测试程序，并对实际1.4.5.8程序集挂接Harmony补丁，不启动游戏：

```powershell
$qotX86Shell = "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
& $qotX86Shell -NoProfile -ExecutionPolicy Bypass -File .\tests\SettingsSyncCore.Tests.ps1
& $qotX86Shell -NoProfile -ExecutionPolicy Bypass -File .\tests\SettingsSyncSaveCore.Tests.ps1
& $qotX86Shell -NoProfile -ExecutionPolicy Bypass -File .\tests\V110.WorldNative.Tests.ps1
& $qotX86Shell -NoProfile -ExecutionPolicy Bypass -File .\tests\AllPatches.Native.Tests.ps1
```

`FeatureNetwork.Dependencies.Tests.ps1`读取功能网络检查生成的独立DLL，应在`FeatureNetwork.Native.Tests.ps1`之后执行。包含`-AssemblyPath`参数的脚本可指定待测DLL；独立测试构建不要与主DLL构建并发覆盖同一输出。

## 运行验证与数据保护

构建、行为测试和IL挂接通过不能证明完整游戏体验通过。发布前使用两个实际客户端和房主／专用服务器，验证全世界箱子与原版箱子窗口、不同前缀及满背包、并发与断线重连、管理员和非管理员设置申请、刷怪滑条、旅商货物、晶塔、结构扫描、个人假人DPS，以及中文输入和不同UI缩放。

远程存取记录在同一次服务器运行期间支持查询断线前的结果。服务器重启后旧结果无法确认、记录损坏或鼠标记录与角色存档不一致时，库存保持锁定；不能删除日志后重发未知交易。排查和备份应同时保留角色／世界存档、`TerrariaModder/player-data/`、`TerrariaModder/world-data/`及`.cursor`记录。普通鼠标物品保存恢复也必须核验对应角色存档。

自动钓鱼仍走客户端原版收获，不能把新增渔获直接写入远程客户端箱子镜像。结构扫描只在主线程按预算遍历，服务器负责发送找到目标的区块。游戏运行验证未完成时，应列出具体未验场景，不宣称“全部联机功能已验证”。

## 当前工作区验证边界（2026-10-17）

根目录包含客户端和服务器两个工程，构建时明确指定工程，避免 MSBuild 在多个项目之间无法选择。服务器工程依赖先生成的客户端 DLL。

终极存储单击取物重复提示仍未通过游戏验收。协议测试中的事务桩可能在确认后清除 Pending，而生产代码为延后角色保存会保留已确认记录；当前协议测试不能据此证明恢复路径正常。终极存储、快速放置联机卡顿和连锁来源警告的修复状态应在真实客户端与服务器中确认。

本地迁移资料与当前排查线索在 `docs/migration/HANDOFF.md`，对话归档说明在 `docs/README.md`；这些私有文件由 .gitignore 忽略。本节是工作区状态说明，不代表新版本已经发布。
