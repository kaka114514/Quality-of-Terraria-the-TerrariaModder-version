$ErrorActionPreference='Stop'
Add-Type -Path 'D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/Mono.Cecil.dll'
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:/App/Game/Steam/steamapps/common/Terraria/Terraria.exe')
$script:checks=0
function Check($ok,$name){$script:checks++;if(!$ok){throw "FAIL $name"};Write-Output "PASS $name"}
function Method($type,$method){($game.MainModule.Types|Where-Object Name -eq $type).Methods|Where-Object Name -eq $method|Select-Object -First 1}
function Calls($method,$name){@($method.Body.Instructions|Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name })}
Check ($game.Name.Version.ToString() -eq '1.4.5.8') '当前被测原版程序集版本1.4.5.8'
foreach($name in @('hardUpdateWorld','UpdateWorld_OvergroundTile','UpdateWorld_UndergroundTile','UpdateWorld_GrassGrowth')){
 $method=Method WorldGen $name
 Check (@($method.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'AllowedToSpreadInfections'}).Count -gt 0) "自然更新 $name 具有独立感染门槛"
}
$money=Method NPC NPCLoot_DropMoney
Check (@($money.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'value'}).Count -eq 1) '钱币入口只读取一次NPC基础价值'
$banner=Method BannerSystem AddNPCKillBy
Check ((Calls $banner AddKill).Count -eq 1 -and (Calls $banner AddClaimableBanner).Count -eq 1) '旗帜使用一次真实击杀统计与一次原版领取入口'
$send=Method NetMessage SendData
$switch=$send.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'switch'}|Select-Object -First 1
$start=$switch.Operand[71].Offset
$end=$switch.Operand[72].Offset
Check (@($send.Body.Instructions|Where-Object {$_.Offset -ge $start -and $_.Offset -lt $end -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'travelShop'}).Count -eq 1) '原版消息72序列化旅商货物'
$receive=Method MessageBuffer GetData
$switch=$receive.Body.Instructions|Where-Object {$_.OpCode.Name -eq 'switch'}|Select-Object -First 1
$start=$switch.Operand[71].Offset
$end=$switch.Operand[72].Offset
Check (@($receive.Body.Instructions|Where-Object {$_.Offset -ge $start -and $_.Offset -lt $end -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'travelShop'}).Count -eq 1) '原版消息72接收旅商货物'
$request=Method TeleportPylonsSystem HandleTeleportRequest
Check ((Calls $request InTileEntityInteractionRange).Count -eq 1) '晶塔传送具有第二次源范围检查'
Check (@($request.Body.Instructions|Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -match 'Danger|Boss|Invasion'}).Count -eq 0) '当前原版传送入口不检查战斗危险'
Write-Output "真实1.4.5.8 IL契约：$script:checks 项通过"
