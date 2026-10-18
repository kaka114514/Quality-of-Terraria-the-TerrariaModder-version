$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=Join-Path $root 'bin/v110-world/behavior'
New-Item $fixture -ItemType Directory -Force | Out-Null
$rate=Get-Content (Join-Path $root 'Patches/WorldQoLCompletionPatches.cs') -Raw
$rate='using System;using Terraria;using System.Linq;using TerrariaModder.Core.Config;using TerrariaModder.Core.Net;using QualityOfTerraria.Networking;'+$rate.Substring($rate.IndexOf('namespace '),$rate.IndexOf('        [HarmonyPatch]')-$rate.IndexOf('namespace '))+'}}'
Set-Content (Join-Path $fixture 'Rate.cs') $rate
$player=Get-Content (Join-Path $root 'Patches/PlayerQoLPatches.cs') -Raw
$toolStart=$player.IndexOf('        [HarmonyPatch(typeof(Player), "CapAttackSpeeds")]')
$tool=$player.Substring($toolStart,$player.IndexOf('        [HarmonyPatch(typeof(Player), "ApplyItemAnimation"')-$toolStart)
$respawnStart=$player.IndexOf('        [HarmonyPatch(typeof(Player), "GetRespawnTime")]')
$respawn=$player.Substring($respawnStart,$player.IndexOf('        // 6. 死亡保存增益')-$respawnStart)
'using System;using HarmonyLib;using Terraria;using Terraria.ID;namespace QualityOfTerraria.Patches{public static class PlayerQoLPatches{'+$tool+$respawn+'}}' | Set-Content (Join-Path $fixture 'Player.cs')
Copy-Item (Join-Path $PSScriptRoot 'V110.WorldBehavior.Harness.txt') (Join-Path $fixture 'Harness.cs') -Force
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Compile Include="../../../QualityOfTerrariaConfig.cs"/><Compile Include="../../../Patches/WorldAndEnvironmentPatches.cs"/><Compile Include="../../../Patches/WorldFeaturePatches.cs"/><Compile Include="../../../Patches/RedPotionPatches.cs"/><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference><Reference Include="TerrariaModder.Core"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll</HintPath></Reference></ItemGroup></Project>
"@ | Set-Content (Join-Path $fixture 'WorldChecks.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'WorldChecks.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw '世界行为检查编译失败'}
& (Join-Path $fixture 'bin/Release/net48/WorldChecks.exe')
if($LASTEXITCODE -ne 0){throw '世界行为检查失败'}
