$ErrorActionPreference = 'Stop'
Add-Type -Path 'D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\core\deps\Mono.Cecil.dll'
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '..\bin\Release\QualityOfTerraria.dll'))
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:\App\Game\Steam\steamapps\common\Terraria\Terraria.exe')
try {
    $drawing = $mod.MainModule.GetType('QualityOfTerraria.UI.StorageItemDrawing')
    if (!$drawing) { throw '两个窗口缺少保留实际物品实例的原版图标绘制入口' }
    $icon = $drawing.Methods | Where-Object Name -eq 'DrawIcon'
    if ($icon.Parameters[0].ParameterType.FullName -ne 'Terraria.Item') { throw '绘制必须传实际物品，不能仅传物品ID' }
    $calls = @($icon.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
    if (!($calls -match 'Terraria.UI.ItemSlot::DrawItemIcon')) { throw '未调用原版图标染色和动画流程' }
    foreach ($name in @('ExtremeStorageUI', 'SuperVaultUI')) {
        $draw = $mod.MainModule.GetType('QualityOfTerraria.UI.' + $name).Methods | Where-Object Name -eq 'Draw'
        if ($name -eq 'ExtremeStorageUI') { $draw = $mod.MainModule.GetType('QualityOfTerraria.UI.' + $name).Methods | Where-Object Name -eq 'DrawItemGrid' }
        $calls = @($draw.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
        if (!($calls -match 'StorageItemDrawing::DrawIcon')) { throw "$name 没有使用完整物品绘制" }
        if ($calls -match 'UIRenderer::DrawItem\(') { throw "$name 仍使用仅传ID的白色纹理绘制" }
        Write-Host "PASS $name 绘制实际物品实例"
    }
    $native = $game.MainModule.GetType('Terraria.UI.ItemSlot').Methods | Where-Object Name -eq 'DrawItemIcon'
    $nativeCalls = @($native.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.Name })
    if (!($nativeCalls -contains 'GetAlpha') -or !($nativeCalls -contains 'GetColor') -or !($nativeCalls -contains 'GetFrame')) { throw '原版绘制入口没有颜色覆盖或动画帧处理' }
    Write-Host 'PASS 真实1.4.5.8原版入口包含颜色覆盖、透明度及动画帧'
} finally { $mod.Dispose(); $game.Dispose() }
