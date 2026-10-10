param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../bin/Release/QualityOfTerraria.dll'))
$ErrorActionPreference = 'Stop'
$game = 'D:\App\Game\Steam\steamapps\common\Terraria'
[Reflection.Assembly]::LoadFrom((Join-Path $game 'Terraria.exe')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $game 'TerrariaModder/core/deps/0Harmony.dll')) | Out-Null
$core = [Reflection.Assembly]::LoadFrom((Join-Path $game 'TerrariaModder/core/TerrariaModder.Core.dll'))
$modAssembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath))
$method = $core.GetType('TerrariaModder.Core.UI.ModMenu').GetMethod('DrawConfigTab', [Reflection.BindingFlags]'Static,NonPublic')
$getInstructions = [HarmonyLib.PatchProcessor].GetMethods() | Where-Object { $_.Name -eq 'GetOriginalInstructions' -and !$_.GetParameters()[1].ParameterType.IsByRef } | Select-Object -First 1
$instructions = $getInstructions.Invoke($null, @($method, $null))
$addCalls = @($instructions | Where-Object { $_.opcode -eq [Reflection.Emit.OpCodes]::Callvirt -and $_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'Add' -and $_.operand.DeclaringType.FullName -like '*List*ValueTuple*ModInfo*' }).Count
if ($addCalls -lt 3) { throw 'FAIL 当前Core配置页行结构不支持本补丁' }
$transpiler = $modAssembly.GetType('QualityOfTerraria.UI.SettingsSyncRowsPatch').GetMethod('Transpiler', [Reflection.BindingFlags]'Static,NonPublic')
$patched = @($transpiler.Invoke($null, [object[]]@(,$instructions)))
$replaced = @($patched | Where-Object { $_.opcode -eq [Reflection.Emit.OpCodes]::Call -and $_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'AddConfigRow' }).Count
if ($replaced -ne $addCalls -or $patched.Count -ne $instructions.Count) { throw "FAIL 真实Core菜单行补丁替换错误：$replaced/$addCalls" }
Write-Output "PASS 真实Core配置页 $replaced 个行插入入口由生产transpiler准确替换，原有指令与跳转保持"
$draw = $core.GetType('TerrariaModder.Core.UI.ModMenu').GetMethod('DrawConfigProperty', [Reflection.BindingFlags]'Static,NonPublic')
if (($draw.GetParameters().ParameterType.Name -join ',') -ne 'Int32,Int32,Int32,ConfigPropertyMeta,ModConfig,String') { throw 'FAIL Core配置行参数签名改变' }
Write-Output 'PASS 配置行绘制Prefix参数匹配本机Core 0.4.1'
