$ErrorActionPreference='Stop'
Add-Type -Path 'D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\core\deps\Mono.Cecil.dll'
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:\App\Game\Steam\steamapps\common\Terraria\Terraria.exe')
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '../bin/Release/QualityOfTerraria.dll'))
$checks=0
function Check($ok,$label){if(!$ok){throw "FAIL: $label"};$script:checks++;Write-Output "PASS $label"}
try {
    $main=$game.MainModule.GetType('Terraria.Main')
    $instance=$main.Fields | Where-Object Name -eq 'instance'
    $shop=$main.Fields | Where-Object Name -eq 'shop'
    Check ($instance.IsStatic -and $instance.IsPublic -and $instance.FieldType.FullName -eq 'Terraria.Main') 'Main.instance为公开静态Main实例'
    Check (!$shop.IsStatic -and $shop.IsPublic -and $shop.FieldType.FullName -eq 'Terraria.Chest[]') 'Main.shop为公开实例字段'
    $setup=@($game.MainModule.GetType('Terraria.Chest').Methods | Where-Object {$_.Name -eq 'SetupShop' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Int32'})
    Check ($setup.Count -eq 1 -and !$setup[0].IsStatic) 'Chest.SetupShop(int)为实例方法'
    $response=$mod.MainModule.GetType('QualityOfTerraria.Networking.ToolNetwork').Methods | Where-Object Name -eq 'HandleResponse'
    $il=@($response.Body.Instructions)
    Check (@($il | Where-Object {$_.OpCode.Name -eq 'ldsfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.FullName -eq $instance.FullName}).Count -ge 2) '实际编译产物通过Main.instance读取商店'
    Check (@($il | Where-Object {$_.OpCode.Name -eq 'ldfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.FullName -eq $shop.FullName}).Count -ge 2) '实际编译产物以实例指令读取shop'
    Check (@($il | Where-Object {$_.OpCode.Name -eq 'ldsfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.FullName -eq $shop.FullName}).Count -eq 0) '不存在将shop当静态字段读取的错误IL'
    Check (@($il | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.FullName -eq $setup[0].FullName}).Count -eq 1) '旅商确认实际调用原版实例SetupShop'
    $near=@($game.MainModule.GetType('Terraria.Player').Methods | Where-Object {$_.Name -eq 'isNearNPC' -and $_.Parameters.Count -eq 2 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Int32' -and $_.Parameters[1].ParameterType.FullName -eq 'System.Single'})
    Check ($near.Count -eq 1 -and !$near[0].IsStatic -and $near[0].ReturnType.FullName -eq 'System.Boolean') '服务器附近旅商核验绑定实际Player实例方法'
    Write-Output "工具网络原版依赖与实际编译IL: $checks/$checks 通过"
} finally {$game.Dispose();$mod.Dispose()}
