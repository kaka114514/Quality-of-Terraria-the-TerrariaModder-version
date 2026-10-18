$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$fixture = Join-Path $root 'bin/ui-compat-fixture'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Add-Type -Path 'D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/Mono.Cecil.dll'
$core = [Mono.Cecil.AssemblyDefinition]::ReadAssembly('D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll')
try {
    $meta = $core.MainModule.Types | Where-Object { $_.Name -eq 'ConfigPropertyMeta' }
    foreach ($name in @('Label','Description')) {
        $getter = $meta.Methods | Where-Object { $_.Name -eq "get_$name" }
        $ops = @($getter.Body.Instructions | ForEach-Object { $_.OpCode.Name }) -join ','
        if ($ops -ne 'ldarg.0,ldfld,ret') { throw "Core getter no longer an auto-property: $name" }
        Write-Output "Cecil PASS ConfigPropertyMeta.$name = ldarg.0,ldfld,ret"
    }
    $renderer = $core.MainModule.Types | Where-Object { $_.Name -eq 'UIRenderer' }
    $click = $renderer.Methods | Where-Object { $_.Name -eq 'get_MouseLeftClick' }
    if ($click.Body.ExceptionHandlers.Count -eq 0) { throw 'Core MouseLeftClick getter protection changed' }
    Write-Output 'Cecil PASS MouseLeftClick has exception handlers; not an auto-property'
} finally { $core.Dispose() }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UICompatibility.Native.Harness.txt') -Destination (Join-Path $fixture 'Harness.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UICompatibility.Native.Stubs.txt') -Destination (Join-Path $fixture 'Stubs.cs') -Force
Copy-Item -LiteralPath (Join-Path $root 'UI/SettingsSyncUI.cs') -Destination (Join-Path $fixture 'SettingsSyncUI.cs') -Force
Copy-Item -LiteralPath (Join-Path $root 'lib/ReLogic.dll') -Destination $fixture -Force
@'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net48</TargetFramework><OutputType>Exe</OutputType><PlatformTarget>x86</PlatformTarget><LangVersion>latest</LangVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All"/><Reference Include="0Harmony"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/deps/0Harmony.dll</HintPath></Reference><Reference Include="TerrariaModder.Core"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/TerrariaModder/core/TerrariaModder.Core.dll</HintPath></Reference><Reference Include="Terraria"><HintPath>D:/App/Game/Steam/steamapps/common/Terraria/Terraria.exe</HintPath><Private>false</Private></Reference></ItemGroup></Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'UICompatibility.csproj')
& "$env:USERPROFILE/.dotnet/dotnet.exe" build (Join-Path $fixture 'UICompatibility.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'UI compatibility fixture build failed' }
$exe = Join-Path $fixture 'bin/Release/net48/UICompatibility.exe'
foreach ($mode in @('early','late','early-bridge','early-real','late-real','root-before-real','core-before-root-real','root-repatch-real','vein-before-root-real')) {
    & $exe $mode
    if ($LASTEXITCODE -ne 0) { throw "UI compatibility check failed: $mode" }
}
