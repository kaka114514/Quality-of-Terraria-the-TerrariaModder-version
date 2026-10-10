$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin/world-qol/native'
New-Item -ItemType Directory -Force $fixture | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'WorldQoL.NativeHarness.txt') (Join-Path $fixture 'Harness.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All" />
<Reference Include="QualityOfTerraria"><HintPath>../../Release/QualityOfTerraria.dll</HintPath></Reference>
<Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference>
<Reference Include="Terraria"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/Terraria.exe</HintPath></Reference>
<Reference Include="TerrariaModder.Core"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll</HintPath></Reference>
</ItemGroup></Project>
'@ | Set-Content (Join-Path $fixture 'NativeHooks.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $root 'QualityOfTerraria.csproj') -c Release --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '生产 DLL 编译失败' }
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'NativeHooks.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '原版 IL 挂接检查编译失败' }
& (Join-Path $fixture 'bin/Release/net48/NativeHooks.exe')
if ($LASTEXITCODE -ne 0) { throw '原版 IL 挂接检查失败' }
