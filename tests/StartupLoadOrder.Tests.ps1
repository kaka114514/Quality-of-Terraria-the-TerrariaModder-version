param([switch]$FailingRelease)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin\startup-load-order'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StartupLoadOrder.Harness.txt') -Destination (Join-Path $fixture 'Harness.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'StartupLoadOrder.csproj') -Encoding UTF8
& "$env:USERPROFILE\.dotnet\dotnet.exe" build (Join-Path $fixture 'StartupLoadOrder.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '启动顺序测试编译失败' }
$dll = Join-Path $root 'bin\Release\QualityOfTerraria.dll'
if ($FailingRelease) { $dll = Join-Path $root 'bin\startup-investigation\QualityOfTerraria.dll' }
& (Join-Path $fixture 'bin\Release\net48\StartupLoadOrder.exe') $dll
if ($LASTEXITCODE -ne 0) { throw '真实依赖启动顺序回归失败' }
