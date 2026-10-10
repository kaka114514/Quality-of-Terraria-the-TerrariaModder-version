$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=Join-Path $root 'bin/v110-world/native'
New-Item $fixture -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'V110.WorldNative.Harness.txt') (Join-Path $fixture 'Harness.cs') -Force
$player=Get-Content (Join-Path $root 'Patches/PlayerQoLPatches.cs') -Raw
$toolStart=$player.IndexOf('        [HarmonyPatch(typeof(Player), nameof(Player.UpdateEquips))]')
$tool=$player.Substring($toolStart,$player.IndexOf('        [HarmonyPatch(typeof(Player), "ApplyItemAnimation"')-$toolStart)
$respawnStart=$player.IndexOf('        [HarmonyPatch(typeof(Player), "GetRespawnTime")]')
$respawn=$player.Substring($respawnStart,$player.IndexOf('        // 6. 死亡保存增益')-$respawnStart)
'using System;using HarmonyLib;using Terraria;using Terraria.ID;namespace QualityOfTerraria.Patches{public static class PlayerQoLPatches{'+$tool+$respawn+'}}' | Set-Content (Join-Path $fixture 'Player.cs')
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/>
<Compile Include="../../../QualityOfTerrariaConfig.cs"/><Compile Include="../../../Patches/WorldAndEnvironmentPatches.cs"/><Compile Include="../../../Patches/WorldFeaturePatches.cs"/><Compile Include="../../../Patches/WorldQoLCompletionPatches.cs"/><Compile Include="../../../Patches/RedPotionPatches.cs"/>
<Reference Include="Terraria"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/Terraria.exe</HintPath></Reference><Reference Include="TerrariaModder.Core"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll</HintPath></Reference><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference><Reference Include="Microsoft.Xna.Framework"><HintPath>C:/Windows/Microsoft.NET/assembly/GAC_32/Microsoft.Xna.Framework/v4.0_4.0.0.0__842cf8be1de50553/Microsoft.Xna.Framework.dll</HintPath></Reference></ItemGroup></Project>
'@ | Set-Content (Join-Path $fixture 'WorldNativeHooks.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'WorldNativeHooks.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw '原版IL检查编译失败'}
& (Join-Path $fixture 'bin/Release/net48/WorldNativeHooks.exe')
if($LASTEXITCODE -ne 0){throw '原版IL挂接检查失败'}
