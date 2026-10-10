$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin/world-qol/regression'
New-Item -ItemType Directory -Force $fixture | Out-Null
((Get-Content (Join-Path $PSScriptRoot 'WorldQoL.Harness.txt') -Raw) -replace '(?<!\.)\bMain\.', 'Terraria.Main.') | Set-Content (Join-Path $fixture 'Harness.cs')
@'
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup>
<ItemGroup>
<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All" />
<Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference>
<Compile Include="Harness.cs" />
<Compile Include="../../../Features/StructureLocator.cs" />
<Compile Include="../../../Features/Autofisher.cs" />
<Compile Include="../../../Patches/WorldQoLCompletionPatches.cs" />
</ItemGroup></Project>
'@ | Set-Content (Join-Path $fixture 'WorldRegression.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'WorldRegression.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '世界功能测试编译失败' }
& (Join-Path $fixture 'bin/Release/net48/WorldRegression.exe')
if ($LASTEXITCODE -ne 0) { throw '世界功能行为测试失败' }
