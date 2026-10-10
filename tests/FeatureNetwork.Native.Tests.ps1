$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=Join-Path $root 'bin/feature-network/native'
New-Item $fixture -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'FeatureNetwork.NativeHarness.txt') (Join-Path $fixture 'Harness.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Reference Include="QualityOfTerraria"><HintPath>../production/QualityOfTerraria.dll</HintPath></Reference><Reference Include="Terraria"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/Terraria.exe</HintPath></Reference><Reference Include="TerrariaModder.Core"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll</HintPath></Reference><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference></ItemGroup></Project>
'@ | Set-Content (Join-Path $fixture 'FeatureNativeHooks.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $root 'QualityOfTerraria.csproj') -c Release --no-restore --verbosity quiet -p:OutputPath=bin/feature-network/production/ -p:IntermediateOutputPath=obj/feature-network/
if($LASTEXITCODE -ne 0){throw '独立生产DLL编译失败'}
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'FeatureNativeHooks.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw '原版IL检查编译失败'}
& (Join-Path $fixture 'bin/Release/net48/FeatureNativeHooks.exe')
if($LASTEXITCODE -ne 0){throw '原版IL挂接检查失败'}
