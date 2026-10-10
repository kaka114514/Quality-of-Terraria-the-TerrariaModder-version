$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path (Split-Path $PSScriptRoot) 'Mod.cs') -Raw
$initialize = $source.Substring($source.IndexOf('public override void Initialize('), $source.IndexOf('private void ApplyReadyPatches(') - $source.IndexOf('public override void Initialize('))
if ($initialize.Contains('CreateClassProcessor')) { throw 'FAIL: 在内容就绪前补丁Core UI会固定其他模组未翻译文本，并提前编译输入方法' }
if (!$source.Contains('FrameEvents.OnPostUpdate += ApplyReadyPatches')) { throw '缺少内容就绪后首帧安装补丁的生命周期入口' }
if (!$source.Contains('FrameEvents.OnPostUpdate -= ApplyReadyPatches')) { throw '卸载时未解绑延迟安装入口' }
if (!$source.Contains('if (!_contentReady || _patchesApplied) return;')) { throw '补丁必须等内容就绪且只安装一次' }
Write-Host 'PASS: 补丁延后到全部内容就绪后的首帧，且卸载精确解绑'
