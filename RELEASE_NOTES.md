# v1.1.1 发布清单

**发布日期**: 2026-10-18

## 已修复问题

### 1. 连锁挖矿提示不可见 ✅
- **根因**: VeinMining.TryVeinMine 中过早调用 `CombatText.NewText` 并返回 false，导致提示文本被创建但立即被丢弃
- **修复**: 先完成所有验证，确认可以挖矿后再调用 NewText，保证返回 true
- **影响**: 蓝色"连锁挖掘"提示现在正常显示

### 2. 终极存储反复提示 ✅
- **根因**: UltimateStorage UI 按住快速点击时，每次点击都触发完整的 `QuickStash` 调用
- **修复**: 实现 `StorageOperationQueue` 请求队列系统，自动合并短时间内的重复请求
- **配置**: 6 帧合并窗口（100ms），保持响应性和用户体验平衡
- **影响**: 单次点击只显示一条提示，连续点击自动合并

### 3. 存储操作间隔优化 ✅
- **改进**: 提示抑制间隔从 12 帧降低到 6 帧
- **效果**: 响应更快，用户体验更流畅

## 文件清理

### 工作区
- ✅ 更新 AGENTS.md（添加修复记录，清理未解决问题列表）
- ✅ 更新 README.md（v1.1.1 更新日志）
- ✅ 更新 CLAUDE.md（反映当前状态）

### 发布准备
- ✅ 创建 `github_push/` 目录，包含所有可公开文件
- ✅ 排除 AGENTS.md, CLAUDE.md, NEW_SESSION_QUICKSTART.md, QUICK_DEPLOY.ps1
- ✅ 排除 lib/*.dll（添加 lib/README.md 说明）

### Mods 目录
- ⚠️ **待手动清理**: `D:\App\Game\Steam\steamapps\common\Terraria\TerrariaModder\mods\quality-of-terraria\`
  - 删除 `QualityOfTerraria.Server.bin.bak`
  - 删除 `QualityOfTerraria.dll.bak`
  - 文件当前被锁定，需要完全退出游戏和 TerrariaModder 后手动删除

## 版本信息

- **Manifest**: 1.1.1
- **Git**: 需要提交当前改动并打标签
- **构建**: 使用 `dotnet build .\QualityOfTerraria.csproj -c Release` + `.\build-server.ps1`

## 下一步

1. 完全退出游戏和 TerrariaModder
2. 手动删除 mods 目录的 .bak 文件
3. 提交 git 改动：
   ```bash
   git add .
   git commit -m "v1.1.1: 修复连锁挖矿提示和存储重复提示问题"
   git tag -a v1.1.1 -m "Release v1.1.1"
   ```
4. 推送到 GitHub：
   ```bash
   cd github_push
   git init
   git add .
   git commit -m "v1.1.1: 修复连锁挖矿提示和存储重复提示问题"
   git remote add origin <your-github-repo-url>
   git push -u origin main
   git push --tags
   ```

## 测试验收

所有问题已通过用户实际游戏测试验收：
- ✅ 连锁挖矿蓝色提示正常显示
- ✅ 终极存储单次点击只显示一条提示
- ✅ 存储操作响应速度提升

**状态**: 可以发布 🎉
