param([string]$AssemblyPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if (!$AssemblyPath) {$AssemblyPath=Join-Path $root 'bin/Release/QualityOfTerraria.dll'}
$fixture=Join-Path $root 'bin/all-patches-native'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AllPatches.Native.Harness.txt') -Destination (Join-Path $fixture 'Harness.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference></ItemGroup></Project>
'@ | Set-Content (Join-Path $fixture 'Native.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'Native.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw '全补丁挂接测试构建失败'}
$output=Join-Path $fixture 'bin/Release/net48'
Copy-Item -LiteralPath $AssemblyPath -Destination (Join-Path $output 'QualityOfTerraria.dll') -Force
$env:QOT_SOURCE_ROOT=$root
& (Join-Path $output 'Native.exe')
if($LASTEXITCODE -ne 0){throw '全补丁真实挂接检查失败'}
