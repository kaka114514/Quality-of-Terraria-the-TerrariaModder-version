$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$source = Get-Content (Join-Path $root 'UI/BuffAndSpawnRateUI.cs') -Raw
if (!$source.Contains('internal static float SliderValue(')) { throw 'FAIL: 刷怪面板缺少0—100自由滑动调节' }
function Method([string]$signature) {
 $start=$source.IndexOf($signature);$open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
 while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
 $source.Substring($start,$end-$start)
}
$method = Method 'internal static float SliderValue('
Add-Type -TypeDefinition @"
using System;
public static class SliderHarness { $method public static float Value(int mouse,int x,int width){return SliderValue(mouse,x,width);} }
"@
foreach ($case in @(@(-10,10,100,0),@(10,10,100,0),@(60,10,100,50),@(110,10,100,100),@(150,10,100,100),@(40,10,0,0))) {
 $actual=[SliderHarness]::Value($case[0],$case[1],$case[2]);if($actual -ne $case[3]){throw "拖动位置转换失败：$actual"}
}
if (!$source.Contains('ToolNetwork.Request("spawn-rate"')) { throw '联机滑条未交给服务器确认' }
if (!$source.Contains('_sliderDragging')) { throw '滑条缺少拖拽会话状态' }
Write-Host 'PASS: 6个滑条边界、联机入口与拖动状态检查'
