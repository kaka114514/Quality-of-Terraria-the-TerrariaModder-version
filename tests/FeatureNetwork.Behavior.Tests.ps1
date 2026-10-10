$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$dummy=Get-Content (Join-Path $root 'Features/TargetDummySystem.cs') -Raw
$dummy=$dummy.Substring(0,$dummy.IndexOf('        public static void DrawHUD()'))+"`n    }`n}"
$locator=Get-Content (Join-Path $root 'Features/StructureLocator.cs') -Raw
$runtime=Get-Content (Join-Path $root 'Networking/NetworkRuntime.cs') -Raw
$networkPath=Join-Path $root 'Networking/FeatureNetwork.cs'
$network=if(Test-Path $networkPath){Get-Content $networkPath -Raw}else{'namespace QualityOfTerraria.Networking {internal static class FeatureNetwork {public static int Requests;internal static void RequestDummy(Vector2 p){Requests++;}internal static bool RequestLocator(StructureLocator.StructureType t){Requests++;return true;}}}'}
$harness=Get-Content (Join-Path $PSScriptRoot 'FeatureNetwork.Harness.txt') -Raw
foreach($s in @($dummy,$locator,$runtime,$network)){$harness+="`n"+[regex]::Replace($s,'(?m)^using .*;\r?\n','')}
Add-Type -TypeDefinition $harness -Language CSharp -IgnoreWarnings
[FeatureNetworkTests]::Run()
