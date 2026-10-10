# 源码构建

本项目是 [ImproveGame（更好的体验）](https://github.com/487666123/ImproveGame/tree/1.4.4) 的 TerrariaModder 移植版，实现的功能以 README 为准。原项目采用 MIT 许可证，版权声明见 LICENSE。

需要 Windows、.NET SDK、.NET Framework 4.8、XNA Framework 4.0，以及 Terraria 1.4.5.8 和 TerrariaModder 0.4.1。

1. 在 `QualityOfTerraria.csproj` 中将 Terraria、TerrariaModder.Core 和 Harmony 的 `HintPath` 改为自己的安装路径。XNA 引用默认使用 Windows GAC 路径。
2. 将自己游戏中的 ReLogic 程序集放到 `lib/ReLogic.dll`。Terraria.exe 包含资源 `Terraria.Libraries.ReLogic.ReLogic.dll`，可通过程序集资源提取工具导出。游戏与加载器二进制依赖不随本仓库分发。
3. 运行 `dotnet build -c Release`，输出为 `bin/Release/QualityOfTerraria.dll`。
4. 将输出 DLL、`manifest.json` 和 `icon.png` 放到游戏的 `TerrariaModder/mods/quality-of-terraria/` 目录。

`build.ps1` 会构建并部署；使用前请修改其中的目标游戏路径。只验证源码时使用 `dotnet build -c Release`，不会自动发布、推送或复制到游戏目录。联机发布包需要房主／专用服务器和所有玩家安装同版本，本版 manifest 的 `multiplayer` 为 `required`。

## 回归检查

`tests/` 中的游戏、Core、Harmony、Mono.Cecil及SDK路径需要按本机安装位置调整。多数脚本可在 PowerShell 7 中执行；依赖真实XNA／Core运行时的脚本使用32位 Windows PowerShell。测试生成物放在忽略的 `bin/` 下，不随发布包分发。

先构建生产DLL，再运行相关脚本。以下为 v1.1.0 的主要检查：

| 检查范围 | 脚本 |
| --- | --- |
| 配置范围、默认值与旧布尔迁移 | `V110.WorldConfig.Tests.ps1` |
| 晶塔、感染、钱币、旗帜、红药水、复活与倍率行为 | `V110.WorldBehavior.Tests.ps1`、`WorldQoL.Tests.ps1` |
| 真实原版IL契约与组合补丁挂接 | `V110.WorldContracts.Tests.ps1`、`V110.WorldNative.Tests.ps1`、`AllPatches.Native.Tests.ps1` |
| 护士收费与原版入口 | `Nurse.Behavior.Tests.ps1`、`Nurse.Native.Tests.ps1` |
| 存储交易、重复请求、身份及断线恢复 | `RemoteStorage.Transactions.Tests.ps1`、`RemoteStorage.Protocol.Tests.ps1`、`Review.RemoteStorage.MarkerIdentity.Tests.ps1`、`Review.RemoteStorage.Disconnect.Tests.ps1` |
| 事务记录、鼠标物品与库存冻结 | `StorageTransactionJournal.Tests.ps1`、`StorageTransactionJournal.Native.Tests.ps1`、`StorageInventoryGuard.Tests.ps1`、`StorageCoins.Pending.Tests.ps1` |
| 设置申请、保存核验与单机世界配置 | `SettingsSync.Tests.ps1`、`SettingsSyncCore.Tests.ps1`、`SettingsSyncSaveCore.Tests.ps1`、`LocalWorldSettings.Tests.ps1` |
| 设置列表、刷怪滑条与旅商按钮 | `SettingsSyncUI.Tests.ps1`、`SpawnRateUI.Tests.ps1`、`TravelShopUI.Tests.ps1` |
| 建造、液体与世界工具请求 | `ToolNetwork.Behavior.Tests.ps1`、`ToolNetwork.Native.Tests.ps1`、`Tools.Behavior.Tests.ps1`、`Tools.Contracts.Tests.ps1` |
| 结构定位、测试假人与功能网络 | `FeatureNetwork.Behavior.Tests.ps1`、`FeatureNetwork.Native.Tests.ps1`、`FeatureNetwork.Dependencies.Tests.ps1` |
| 既有存储、UI和生命周期回归 | `Storage.Tests.ps1`、`StorageBehavior.Tests.ps1`、`StorageUI.Tests.ps1`、`StorageCrafting.Tests.ps1`、`StorageIcon.Tests.ps1`、`StorageIcon.Drawing.Tests.ps1`、`PanelLayout.Tests.ps1`、`QuickPlacePreview.Tests.ps1`、`WorldSession.Tests.ps1`、`ModLifecycle.Tests.ps1`、`StartupLoadOrder.Tests.ps1` |

例如在项目根目录执行独立行为检查：

```powershell
dotnet build -c Release
.\tests\V110.WorldConfig.Tests.ps1
.\tests\V110.WorldBehavior.Tests.ps1
.\tests\WorldQoL.Tests.ps1
.\tests\RemoteStorage.Transactions.Tests.ps1
.\tests\SettingsSync.Tests.ps1
.\tests\ToolNetwork.Behavior.Tests.ps1
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
