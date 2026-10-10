$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path (Split-Path $PSScriptRoot) 'Mod.cs') -Raw
$initialize = $source.Substring($source.IndexOf('public override void Initialize('), $source.IndexOf('private void ApplyReadyPatches(') - $source.IndexOf('public override void Initialize('))
if ($initialize.Contains('CreateClassProcessor')) { throw 'FAIL: 在内容就绪前补丁Core UI会固定其他模组未翻译文本，并提前编译输入方法' }
if ($source.Contains('FrameEvents.OnPostUpdate += ApplyReadyPatches')) { throw 'FAIL: 将补丁安装留到首个世界更新会造成进入世界卡顿' }
$contentReady = $source.Substring($source.IndexOf('public void OnContentReady('), $source.IndexOf('public void OnWorldLoad(') - $source.IndexOf('public void OnContentReady('))
if (!$contentReady.Contains('ApplyReadyPatches();')) { throw '内容就绪阶段应安装一次补丁，不能等待世界更新' }
if (!$source.Contains('if (!_contentReady || _patchesApplied) return;')) { throw '补丁必须等内容就绪且只安装一次' }
Write-Host 'PASS: 内容就绪阶段安装一次补丁，不在进入世界首帧集中安装'
