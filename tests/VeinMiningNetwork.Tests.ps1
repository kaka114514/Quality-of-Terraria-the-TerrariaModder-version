$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$path=Join-Path $root 'Networking/VeinMiningNetwork.cs'
if (!(Test-Path $path)) {throw 'FAIL: 连锁请求必须消费真实发送者的成功原版破矿记录，网络实现尚不存在'}
$production=Get-Content $path -Raw
$patch=Get-Content (Join-Path $root 'Patches/VeinMiningOriginPatches.cs') -Raw
$runtime=Get-Content (Join-Path $root 'Networking/NetworkRuntime.cs') -Raw
$transport=Get-Content (Join-Path $root 'Networking/NetSyncPayload.cs') -Raw
$harness=Get-Content (Join-Path $PSScriptRoot 'VeinMiningNetwork.Harness.txt') -Raw
$code=$harness
foreach($s in @($transport,$production,$patch,$runtime)){$code += [regex]::Replace($s,'(?m)^using .*;\r?\n','')}
Add-Type -TypeDefinition $code
[VeinNetworkTests]::Run()
