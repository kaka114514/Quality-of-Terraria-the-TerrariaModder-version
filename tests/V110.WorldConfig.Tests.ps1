$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$fixture=Join-Path $root 'bin/v110-world/config'
New-Item $fixture -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'V110.WorldConfig.Harness.txt') (Join-Path $fixture 'Harness.cs') -Force
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Compile Include="../../../QualityOfTerrariaConfig.cs"/><Reference Include="TerrariaModder.Core"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll</HintPath></Reference></ItemGroup></Project>
"@ | Set-Content (Join-Path $fixture 'ConfigChecks.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'ConfigChecks.csproj') -c Release --verbosity quiet
if($LASTEXITCODE -ne 0){throw '配置行为检查编译失败'}
& (Join-Path $fixture 'bin/Release/net48/ConfigChecks.exe')
if($LASTEXITCODE -ne 0){throw '配置行为检查失败'}
