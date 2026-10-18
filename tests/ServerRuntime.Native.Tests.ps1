param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../bin/Release/QualityOfTerraria.dll'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin/server-runtime-native'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ServerRuntime.Native.Harness.txt') -Destination (Join-Path $fixture 'Harness.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference></ItemGroup></Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'Native.csproj') -Encoding UTF8
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'Native.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '专服运行模块测试构建失败' }
$env:QOT_SOURCE_ROOT = $root
& (Join-Path $fixture 'bin/Release/net48/Native.exe') (Resolve-Path $AssemblyPath)
if ($LASTEXITCODE -ne 0) { throw '专服运行模块测试失败' }
