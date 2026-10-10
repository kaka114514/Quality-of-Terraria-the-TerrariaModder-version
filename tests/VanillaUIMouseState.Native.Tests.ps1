param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../bin/Release/QualityOfTerraria.dll'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin/vanilla-ui-mouse-state-native'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VanillaUIMouseState.Native.Harness.txt') -Destination (Join-Path $fixture 'Harness.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/></ItemGroup></Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'VanillaUIMouseState.Native.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'VanillaUIMouseState.Native.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '原版鼠标状态真实回归检查构建失败' }
$output = Join-Path $fixture 'bin/Release/net48'
Copy-Item -LiteralPath (Resolve-Path $AssemblyPath) -Destination (Join-Path $output 'QualityOfTerraria.dll') -Force
$env:QOT_SOURCE_ROOT = $root
& (Join-Path $output 'VanillaUIMouseState.Native.exe') (Join-Path $output 'QualityOfTerraria.dll')
if ($LASTEXITCODE -ne 0) { throw '原版鼠标状态真实回归检查失败' }
