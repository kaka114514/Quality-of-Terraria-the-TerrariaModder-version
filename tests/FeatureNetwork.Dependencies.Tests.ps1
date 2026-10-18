$ErrorActionPreference='Stop'
Add-Type -Path 'D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\core\deps\Mono.Cecil.dll'
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:\App\Game\Steam\steamapps\common\Terraria\Terraria.exe')
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '../bin/feature-network/production/QualityOfTerraria.dll'))
$checks=0
function Check($ok,$name){if(!$ok){throw "FAIL: $name"};$script:checks++;Write-Output "PASS $name"}
function Method($type,$name){@($game.MainModule.GetType($type).Methods | Where-Object Name -eq $name)}
function Calls($method,$name){@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name}).Count}
try {
    $spawn=Method 'Terraria.NPC' 'NewNPC'
    Check ($spawn.Count -eq 1 -and $spawn[0].Parameters.Count -eq 10) '真实NPC.NewNPC签名唯一且包含四个ai和Target'
    Check ((Calls $spawn[0] 'GetAvailableNPCSlot') -eq 1) 'NewNPC实际通过槽位创建实体'
    $slots=Method 'Terraria.NPC' 'GetAvailableNPCSlot'
    Check (@($slots[0].Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'netMode'}).Count -eq 0) '槽位分配没有客户端禁用保护，客户端调用会创建孤立NPC'
    Check (@($spawn[0].Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'spawnNeedsSyncing'}).Count -eq 1) 'NewNPC仅服务器设置生成同步标记'
    $pull=(Method 'Terraria.Player' 'ItemCheck_CheckFishingBobber_PullBobber')[0]
    Check (@($pull.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldc.i4.s' -and $_.Operand -eq 61}).Count -eq 1) '原版收杆客户端猪鲨召唤发61包'
    Check (@($pull.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldc.i4' -and $_.Operand -eq 130}).Count -eq 1) '原版收杆客户端钓出敌怪发130包'
    Check ((Calls $pull 'SendData') -eq 2 -and (Calls $pull 'NewNPC') -eq 1) '原版保留客户端发包和服务器NPC生成分支'
    $give=(Method 'Terraria.Projectile' 'AI_061_FishingBobber_GiveItemToPlayer')[0]
    Check ((Calls $give 'QuickSpawnItem') -eq 1) '原版渔获数量和给予物品入口保留'
    $overflow=(Method 'Terraria.Player' 'GetOrDropItem')[0]
    Check ((Calls $overflow 'GetItem') -eq 1 -and (Calls $overflow 'RequestNewItem') -eq 1) '原版优先入背包，溢出请求世界掉落'
    $request=Method 'Terraria.Item' 'RequestNewItem' | Where-Object {$_.Parameters[1].ParameterType.FullName -eq 'Microsoft.Xna.Framework.Vector2'}
    Check ((Calls $request 'SendData') -eq 1 -and @($request.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldc.i4.s' -and $_.Operand -eq 21}).Count -eq 1) '世界溢出物品RequestNewItem原版发21 ItemSync'
    $section=Method 'Terraria.NetMessage' 'SendSection'
    Check ($section.Count -eq 1 -and $section[0].IsStatic -and $section[0].Parameters.Count -eq 3) '扫描结果区块发送API真实签名为SendSection(client,x,y)'
    $network=$mod.MainModule.GetType('QualityOfTerraria.Networking.FeatureNetwork')
    $complete=$network.Methods | Where-Object Name -eq 'CompleteScan'
    Check ((Calls $complete 'SendSection') -eq 1) '实际生产IL通过原版SendSection同步目标区块'
    Check (@($network.Methods | Where-Object {$_.Name -eq 'Initialize' -and $_.IsAssembly -and !$_.IsPublic}).Count -eq 1) '网络生命周期入口为internal，不触发加载器提前公开无参初始化'
    $dummy=$mod.MainModule.GetType('QualityOfTerraria.Features.TargetDummySystem')
    $toggle=$dummy.Methods | Where-Object Name -eq 'ToggleOrSpawnAtCursor'
    Check ((Calls $toggle 'RequestDummy') -eq 1 -and (Calls $toggle 'NewNPC') -eq 0) '快捷键实际生产IL发服务器请求，NPC创建只在权威辅助入口'
    $fisher=$mod.MainModule.GetType('QualityOfTerraria.Features.Autofisher')
    $update=$fisher.Methods | Where-Object Name -eq 'Update'
    Check ((Calls $update 'FishingCheck') -eq 1) '自动钓鱼仍执行真实FishingCheck而无固定渔获表'
    Write-Output "功能联机原版依赖和生产IL: $checks/$checks"
} finally {$game.Dispose();$mod.Dispose()}
