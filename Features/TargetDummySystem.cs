using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria.Features
{
    public static class TargetDummySystem
    {
        public static bool IsTrackingDummy = false;
        public static int ActiveDummyWhoAmI = -1;
        public static float InstantDPS = 0f;
        public static float AverageDPS = 0f;
        public static long TotalDamage = 0;
        public static float DurationSeconds = 0f;

        private static readonly object _lock = new object();
        private sealed class DamageRecord
        {
            public int Frame;
            public int Damage;
        }
        private static readonly Queue<DamageRecord> _slidingWindow = new Queue<DamageRecord>();
        private static int _sessionStartFrame = -1;
        private static int _sessionLastFrame = -1;
        private static long _sessionDamage = 0;

        // 来源相同的重复回调共享一条记录；同帧不同攻击保留各自伤害。
        private static readonly Dictionary<Tuple<int, long>, DamageRecord> _frameHits = new Dictionary<Tuple<int, long>, DamageRecord>();
        private static int _hitFrame = -1;
        private static long _unknownHitSource = 0;

        public static void ToggleOrSpawnAtCursor()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead) return;
            if (Main.netMode == 1)
            {
                Networking.FeatureNetwork.RequestDummy(Main.MouseWorld);
                return;
            }
            if (Main.netMode == 2) return;
            // 单机保留原来的附近假人移除和旧无绑定假人清理行为。
            bool removed = false;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || npc.type != NPCID.TargetDummy) continue;
                if (Vector2.Distance(npc.Center, Main.MouseWorld) < 80f || (i == ActiveDummyWhoAmI && npc.ai[0] == -1f))
                {
                    npc.active = false;
                    removed = true;
                }
            }
            if (removed) { BindDummy(-1); return; }
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && npc.type == NPCID.TargetDummy && npc.ai[0] == -1f) RemoveDummy(npc);
            }
            NPC existing = ActiveDummyWhoAmI >= 0 && ActiveDummyWhoAmI < Main.maxNPCs ? Main.npc[ActiveDummyWhoAmI] : null;
            BindDummy(ToggleForPlayer(player, Main.MouseWorld, existing));
        }

        // 只能由单机或服务器调用；服务器以当前玩家会话持有的NPC对象核验所有权。
        internal static int ToggleForPlayer(Player player, Vector2 position, NPC existing)
        {
            if (Main.netMode == 1) return -1;
            if (existing != null && existing.active && existing.type == NPCID.TargetDummy && existing.ai[0] == -1f)
            {
                RemoveDummy(existing);
                return -1;
            }
            int index = NPC.NewNPC(null, (int)position.X, (int)position.Y, NPCID.TargetDummy);
            if (index < 0 || index >= Main.maxNPCs) return -1;
            NPC dummy = Main.npc[index];
            dummy.Center = position;
            dummy.homeless = true;
            dummy.ai[0] = -1f;
            dummy.ai[1] = -1f;
            dummy.immortal = true;
            dummy.dontTakeDamage = false;
            dummy.life = dummy.lifeMax = 1000;
            dummy.defense = dummy.defDefense = 0;
            dummy.knockBackResist = 0f;
            dummy.velocity = Vector2.Zero;
            dummy.timeLeft = 7200;
            dummy.netUpdate = true;
            if (Main.netMode == 2) NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, index);
            return index;
        }

        internal static void RemoveDummy(NPC dummy)
        {
            if (Main.netMode == 1 || dummy == null || !dummy.active || dummy.type != NPCID.TargetDummy || dummy.ai[0] != -1f ||
                dummy.whoAmI < 0 || dummy.whoAmI >= Main.maxNPCs || !ReferenceEquals(Main.npc[dummy.whoAmI], dummy)) return;
            dummy.active = false;
            dummy.netUpdate = true;
            if (Main.netMode == 2) NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, dummy.whoAmI);
        }

        internal static void BindDummy(int index)
        {
            ActiveDummyWhoAmI = index;
            IsTrackingDummy = index >= 0;
            Reset();
            if (index >= 0) SoundEngine.PlaySound(SoundID.Dig);
            else SoundEngine.PlaySound(SoundID.Item14);
            Main.NewText(index >= 0 ? "【测试假人】已放置自己的测试假人！再次按键移除，右键面板重置。" : "【测试假人】已移除自己的测试假人。", 100, 240, 255);
        }

        public static void RecordDamage(int dummyIndex, int damage)
        {
            lock (_lock)
            {
                RecordDamage(dummyIndex, damage, --_unknownHitSource);
            }
        }

        public static void RecordDamage(int dummyIndex, int damage, long hitSource)
        {
            if (!Mod.WorldActive || Main.gameMenu || Main.netMode == 2 || damage <= 0) return;
            if (Main.netMode == 1 && ActiveDummyWhoAmI < 0) return;

            // 若当前已绑定唯一种植的假人，严格只统计该假人
            if (ActiveDummyWhoAmI >= 0 && dummyIndex != ActiveDummyWhoAmI)
            {
                return;
            }

            int currentFrame = (int)Main.GameUpdateCount;

            lock (_lock)
            {
                if (_hitFrame != currentFrame)
                {
                    _frameHits.Clear();
                    _hitFrame = currentFrame;
                }
                var key = Tuple.Create(dummyIndex, hitSource);
                DamageRecord existing;
                if (_frameHits.TryGetValue(key, out existing))
                {
                    if (damage > existing.Damage)
                    {
                        int diff = damage - existing.Damage;
                        _sessionDamage += diff;
                        TotalDamage += diff;
                        existing.Damage = damage;
                    }
                    return;
                }

                // 标记正在测伤，激活 HUD
                IsTrackingDummy = true;

                // 距离上次受击超过 3 秒 (180 帧)，自动开启新的一轮测伤会话
                if (_sessionLastFrame < 0 || (currentFrame - _sessionLastFrame > 180))
                {
                    _sessionStartFrame = currentFrame;
                    _sessionDamage = 0;
                    _slidingWindow.Clear();
                }

                _sessionLastFrame = currentFrame;
                _sessionDamage += damage;
                TotalDamage += damage;

                var record = new DamageRecord { Frame = currentFrame, Damage = damage };
                _frameHits.Add(key, record);
                _slidingWindow.Enqueue(record);
            }
        }

        public static void Update()
        {
            if (!IsTrackingDummy && TotalDamage <= 0)
                return;

            int currentFrame = (int)Main.GameUpdateCount;

            lock (_lock)
            {
                // 1. 滑动窗口维护：仅保留最近 1 秒 (60 帧) 内的伤害记录
                while (_slidingWindow.Count > 0 && currentFrame - _slidingWindow.Peek().Frame >= 60)
                {
                    _slidingWindow.Dequeue();
                }

                long windowSum = 0;
                foreach (var record in _slidingWindow)
                {
                    windowSum += record.Damage;
                }

                // 2. 瞬时 DPS：直接取最近 1 秒 (60 帧) 的滑动窗口总伤 (独立于原版，不受野怪与全局玩家状态干扰)
                InstantDPS = windowSum;

                // 3. 平均 DPS 与持续时间计算
                if (_sessionStartFrame >= 0 && _sessionLastFrame >= _sessionStartFrame)
                {
                    int durationFrames = currentFrame - _sessionStartFrame;
                    // 若已停手超过 3 秒，锁定为最终打靶耗时
                    if (currentFrame - _sessionLastFrame > 180)
                    {
                        durationFrames = _sessionLastFrame - _sessionStartFrame;
                    }
                    float elapsedSec = Math.Max(1.0f, durationFrames / 60f);
                    DurationSeconds = durationFrames / 60f;
                    AverageDPS = _sessionDamage / elapsedSec;
                }

                // 若停手超过 3 秒，瞬时 DPS 自然衰减为 0，平均 DPS 锁定展示
                if (_sessionLastFrame >= 0 && currentFrame - _sessionLastFrame > 180)
                {
                    InstantDPS = 0f;
                }
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                TotalDamage = 0;
                InstantDPS = 0f;
                AverageDPS = 0f;
                DurationSeconds = 0f;
                _sessionStartFrame = -1;
                _sessionLastFrame = -1;
                _sessionDamage = 0;
                _hitFrame = -1;
                _unknownHitSource = 0;
                _frameHits.Clear();
                _slidingWindow.Clear();
            }
        }

        public static void ResetSession()
        {
            Reset();
            IsTrackingDummy = false;
            ActiveDummyWhoAmI = -1;
            _isDragging = false;
            _dragOffsetX = _dragOffsetY = 0;
        }

        public static int HudX = 20;
        public static int HudY = 160;
        public static int HudW = 230;
        public static int HudH = 68;
        private static bool _isDragging = false;
        private static int _dragOffsetX = 0;
        private static int _dragOffsetY = 0;

        public static void DrawHUD()
        {
            // 只有正在追踪假人打靶测伤时才绘制，绝不在日常打怪或扔手榴弹时误弹！
            if (!IsTrackingDummy && TotalDamage <= 0)
                return;

            HudW = Math.Min(230, UI.PanelLayout.ScreenWidth);
            UI.PanelLayout.ClampToScreen(ref HudX, ref HudY, HudW, HudH);
            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;
            bool hudHover = mouseX >= HudX && mouseX <= HudX + HudW && mouseY >= HudY && mouseY <= HudY + HudH;

            if (_isDragging)
            {
                Main.player[Main.myPlayer].mouseInterface = true;
                if (Main.mouseLeft)
                {
                    HudX = Math.Max(0, Math.Min(UIRenderer.ScreenWidth - HudW, mouseX - _dragOffsetX));
                    HudY = Math.Max(0, Math.Min(UIRenderer.ScreenHeight - HudH, mouseY - _dragOffsetY));
                }
                else
                {
                    _isDragging = false;
                }
            }
            else if ((UIRenderer.MouseLeftClick || (Main.mouseLeft && Main.mouseLeftRelease)) && hudHover)
            {
                _isDragging = true;
                _dragOffsetX = mouseX - HudX;
                _dragOffsetY = mouseY - HudY;
                UIRenderer.ConsumeClick();
            }

            if (hudHover)
            {
                Main.player[Main.myPlayer].mouseInterface = true;

                // 右键面板快捷清零并关闭 HUD
                if (UIRenderer.MouseRightClick)
                {
                    Reset();
                    IsTrackingDummy = false;
                    SoundEngine.PlaySound(SoundID.MenuClose);
                    UIRenderer.ConsumeRightClick();
                    return;
                }
            }

            UIRenderer.DrawPanel(HudX, HudY, HudW, HudH, 25, 25, 35, 220);
            UIRenderer.DrawRectOutline(HudX, HudY, HudW, HudH, 255, 180, 50, 255, 1);
            UI.PanelLayout.DrawFittedText("🎯 打靶测伤 (右键重置/关闭)", HudX + 10, HudY + 6, HudW - 20, 255, 200, 100);
            UI.PanelLayout.DrawFittedText($"实时: {InstantDPS:0} | 平均: {AverageDPS:0.0}", HudX + 10, HudY + 24, HudW - 20, 255, 110, 110);
            UI.PanelLayout.DrawFittedText($"总伤: {TotalDamage} | 耗时: {DurationSeconds:0.0}s", HudX + 10, HudY + 44, HudW - 20, 180, 230, 255);
        }
    }
}
