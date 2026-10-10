$ErrorActionPreference = 'Stop'
Add-Type -Path 'D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\core\deps\Mono.Cecil.dll'
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:\App\Game\Steam\steamapps\common\Terraria\Terraria.exe')
$player = $game.MainModule.GetType('Terraria.Player')
$projectile = $game.MainModule.GetType('Terraria.Projectile')
$checks = 0
function Check([bool]$condition, [string]$label) {
    if (!$condition) { throw "FAIL: $label" }
    $script:checks++
    Write-Output "PASS $label"
}
foreach ($name in @('ApplyItemTime', 'ApplyItemAnimation')) {
    $match = @($player.Methods | Where-Object { $_.Name -eq $name -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'Terraria.Item' })
    Check ($match.Count -eq 1) "原版 $name(Item) 签名唯一"
}
Check (@($player.Methods | Where-Object Name -eq 'UpdateEquips').Count -eq 1) 'UpdateEquips 补丁目标唯一'
Check (@($projectile.Methods | Where-Object Name -eq 'Damage').Count -eq 1) 'Projectile.Damage 补丁目标唯一'
$key = $game.MainModule.GetType('Terraria.DataStructures.ProjectileKey')
Check (@($key.Methods | Where-Object { $_.Name -eq 'op_Implicit' -and $_.ReturnType.FullName -eq 'System.UInt32' }).Count -eq 1) 'ProjectileKey 可转换为完整无符号身份'
Check (@($key.Methods | Where-Object Name -eq 'get_Generation').Count -eq 1) 'ProjectileKey 包含槽复用的生成代次'
$itemCheck = $player.Methods | Where-Object Name -eq 'ItemCheck'
$il = @($itemCheck.Body.Instructions)
Check (@($il | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'DoPotionOfReturnTeleportationAndSetTheComebackPoint' }).Count -eq 1) '原版返回药水保留回程点副作用'
Check (@($il | Where-Object { $_.OpCode.Name -eq 'ldc.i4.s' -and $_.Operand -eq 20 }).Count -ge 2) '原版两种回程药水保留20帧触发常量'
foreach ($method in @('Shellphone_Spawn', 'MagicConch', 'DemonConch')) {
    Check (@($il | Where-Object { $_.Operand -and $_.Operand.ToString() -match ('Terraria.Player::' + $method + '\(') }).Count -ge 1) "原版独立目的地 $method"
}
$update = $player.Methods | Where-Object Name -eq 'Update'
$updateCalls = @($update.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
Check ([Array]::IndexOf($updateCalls, 'UpdateEquips') -lt [Array]::IndexOf($updateCalls, 'ItemCheckWrapped')) '工具属性应用在本帧工具使用前'
$output = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '..\bin\Release\QualityOfTerraria.dll'))
$recall = $output.MainModule.GetType('QualityOfTerraria.Patches.PlayerQoLPatches').NestedTypes | Where-Object Name -eq 'InstantRecallPatch'
$postfix = $recall.Methods | Where-Object Name -eq 'Postfix'
Check (@($postfix.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -match 'Spawn|Teleport' }).Count -eq 0) '编译产物瞬传仅调整计时、不替换传送函数'
$game.Dispose()
$output.Dispose()
Write-Output "工具程序集契约检查：$checks/$checks 通过"
