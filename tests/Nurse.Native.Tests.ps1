$ErrorActionPreference='Stop'
Add-Type -Path 'D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\core\deps\Mono.Cecil.dll'
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:\App\Game\Steam\steamapps\common\Terraria\Terraria.exe')
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $PSScriptRoot '../bin/Release/QualityOfTerraria.dll'))
$checks=0
function Check($ok,$label){if(!$ok){throw "FAIL: $label"};$script:checks++;Write-Output "PASS $label"}
try {
    $main=$game.MainModule.GetType('Terraria.Main')
    $cost=@($main.Methods | Where-Object Name -eq 'GetNurseHealCost')
    $heal=@($main.Methods | Where-Object Name -eq 'NPCChatText_DoNurseHeal')
    Check ($cost.Count -eq 1 -and $cost[0].IsStatic -and $cost[0].Parameters.Count -eq 0 -and $cost[0].ReturnType.FullName -eq 'System.Int32') '原版价格入口签名唯一'
    Check ($heal.Count -eq 1 -and $heal[0].IsStatic -and $heal[0].Parameters.Count -eq 1 -and $heal[0].Parameters[0].ParameterType.FullName -eq 'System.Int32') '原版护士实际付款入口唯一'
    $priceIL=@($cost[0].Body.Instructions)
    $healIL=@($heal[0].Body.Instructions)
    Check (@($priceIL | Where-Object {$_.Operand -and $_.Operand.ToString().Contains('NurseCannotRemoveDebuff')}).Count -eq 1) '原版收费过滤不可清除状态'
    Check (@($priceIL | Where-Object {$_.OpCode.Name -eq 'ldc.i4.s' -and $_.Operand -eq 60}).Count -ge 1) '原版收费使用60刻门槛'
    Check (@($healIL | Where-Object {$_.Operand -and $_.Operand.ToString().Contains('NurseCannotRemoveDebuff')}).Count -eq 1) '原版清除同样过滤不可清除状态'
    Check (@($healIL | Where-Object {$_.Operand -and $_.Operand.ToString().Contains('::BuyItem(')}).Count -eq 1) '验证实际治疗入口包含付款'
    $dialogue=$game.MainModule.GetType('Terraria.GameContent.NPCInteractions/Actions/NurseHeal').Methods | Where-Object Name -eq 'Interact'
    Check (@($dialogue.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'NPCChatText_DoNurseHeal'}).Count -eq 1) '实际NPC护士对话动作调用已挂接付款入口'
    $patch=$mod.MainModule.GetType('QualityOfTerraria.Patches.NurseTreatmentPatch')
    Check ($null -ne $patch) '编译产物包含护士对话补丁'
    $attribute=@($patch.CustomAttributes | Where-Object {$_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch'})[0]
    Check ($attribute.ConstructorArguments[0].Value.FullName -eq 'Terraria.Main' -and $attribute.ConstructorArguments[1].Value -eq 'NPCChatText_DoNurseHeal') 'Harmony补丁挂到核验的原版付款入口'
    $prefix=$patch.Methods | Where-Object Name -eq 'Prefix'
    Check ($prefix.ReturnType.FullName -eq 'System.Boolean' -and $prefix.Parameters[0].Name -eq '__0' -and $prefix.Parameters[0].ParameterType.FullName -eq 'System.Int32&') 'Prefix在收费前可跳过原版并更新实际价格参数'
    $prefixCalls=@($prefix.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.Name})
    Check ([Array]::IndexOf($prefixCalls,'NeedsTreatment') -ge 0 -and [Array]::IndexOf($prefixCalls,'NeedsTreatment') -lt [Array]::IndexOf($prefixCalls,'Cost')) '编译产物先判实际需求后取价格'
    $nurse=$mod.MainModule.GetType('QualityOfTerraria.Features.NurseTreatment')
    $ctor=$nurse.Methods | Where-Object Name -eq '.cctor'
    Check (@($ctor.Body.Instructions | Where-Object {$_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq 'GetNurseHealCost'}).Count -eq 1) '快捷治疗绑定实际原版价格方法而非固定费率'
    Write-Output "护士原版IL和编译补丁契约: $checks/$checks 通过"
} finally {$game.Dispose();$mod.Dispose()}
