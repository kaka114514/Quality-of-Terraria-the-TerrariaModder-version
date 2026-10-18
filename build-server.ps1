$ErrorActionPreference = 'Stop'
$dotnet = if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" } else { 'dotnet' }
& $dotnet build (Join-Path $PSScriptRoot 'QualityOfTerraria.Server.csproj') -c Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Server module build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Server\QualityOfTerraria.Server.dll') -Destination (Join-Path $PSScriptRoot 'bin\Release\QualityOfTerraria.Server.bin') -Force
