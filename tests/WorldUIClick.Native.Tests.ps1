param([string]$AssemblyPath = (Join-Path $PSScriptRoot '../bin/Release/QualityOfTerraria.dll'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin/world-ui-click-native'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WorldUIClick.Native.Harness.txt') -Destination (Join-Path $fixture 'Harness.cs') -Force
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference><Reference Include="Microsoft.Xna.Framework"><HintPath>C:/Windows/Microsoft.NET/assembly/GAC_32/Microsoft.Xna.Framework/v4.0_4.0.0.0__842cf8be1de50553/Microsoft.Xna.Framework.dll</HintPath></Reference></ItemGroup></Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'WorldUIClick.Native.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'WorldUIClick.Native.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw '世界界面真实点击检查构建失败' }
$output = Join-Path $fixture 'bin/Release/net48'
Copy-Item -LiteralPath (Resolve-Path $AssemblyPath) -Destination (Join-Path $output 'QualityOfTerraria.dll') -Force
$env:QOT_SOURCE_ROOT = $root
& (Join-Path $output 'WorldUIClick.Native.exe')
if ($LASTEXITCODE -ne 0) { throw '世界界面真实点击检查失败' }
