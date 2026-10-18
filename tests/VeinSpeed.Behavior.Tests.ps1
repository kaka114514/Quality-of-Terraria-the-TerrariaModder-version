$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=Join-Path $root 'bin/vein-speed/behavior'
New-Item $fixture -ItemType Directory -Force | Out-Null
function Extract([string]$source,[string]$signature) {
    $start=$source.IndexOf($signature); if($start -lt 0){throw "缺少生产入口 $signature"}
    $open=$source.IndexOf('{',$start);$end=$open+1;$depth=1
    while($depth -gt 0){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++}
    $source.Substring($start,$end-$start)
}
$qol=Get-Content (Join-Path $root 'Patches/PlayerQoLPatches.cs') -Raw
$patches=(Extract $qol 'public static class ToolStatsPatch')+(Extract $qol 'public static class ToolAnimationPatch')+(Extract $qol 'public static class InstantRecallPatch')
'using System;using Terraria;using Terraria.ID;namespace QualityOfTerraria.Patches{public static class PlayerQoLPatches{'+$patches+'}}' | Set-Content (Join-Path $fixture 'Player.cs')
Copy-Item (Join-Path $PSScriptRoot 'VeinSpeed.Behavior.Harness.txt') (Join-Path $fixture 'Harness.cs') -Force
$extra=''
foreach($file in 'Features/ToolSpeedSystem.cs','Features/VeinConfigMigration.cs'){if(Test-Path (Join-Path $root $file)){$extra+='<Compile Include="../../../'+$file+'"/>'}}
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Compile Include="../../../QualityOfTerrariaConfig.cs"/>$extra<Reference Include="TerrariaModder.Core"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll</HintPath></Reference></ItemGroup></Project>
"@ | Set-Content (Join-Path $fixture 'Checks.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'Checks.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw '工具速度行为检查编译失败'}
& (Join-Path $fixture 'bin/Release/net48/Checks.exe')
if($LASTEXITCODE -ne 0){throw '工具速度行为检查失败'}
