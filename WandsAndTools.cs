using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using QualityOfTerraria.Features;
using QualityOfTerraria.UI;
using QualityOfTerraria.Networking;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.ID;
using TerrariaModder.Core.UI;

namespace QualityOfTerraria
{
    public static class WandsAndTools
    {
        // ================= 1. 快速放置：手持物块/平台/墙拖拽框选铺设 =================
        public static bool QuickPlaceActive = false;
        public static bool SpaceWandDragActive { get => QuickPlaceActive; set => QuickPlaceActive = value; }

        private static bool _isDragging = false;
        private static int _dragStartX = -1;
        private static int _dragStartY = -1;
        private static int _dragCurX = -1;
        private static int _dragCurY = -1;
        private static int _hoverTileX = -1;
        private static int _hoverTileY = -1;
        private static bool _hasHoverTile = false;

        public static void ExecuteQuickPlace()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            QuickPlaceActive = !QuickPlaceActive;
            if (QuickPlaceActive)
            {
                SoundEngine.PlaySound(SoundID.MenuOpen);
                Main.NewText("快速放置: 已开启！(手持方块/平台/墙，按住左键拖拽铺设，右键取消)", 100, 255, 150);
            }
            else
            {
                SoundEngine.PlaySound(SoundID.MenuClose);
                Main.NewText("快速放置: 已关闭。", 255, 150, 100);
                _isDragging = false;
            }
        }

        public static void ExecuteSpaceWand() => ExecuteQuickPlace();

        public static void UpdateQuickPlace()
        {
            _hasHoverTile = false;
            if (!QuickPlaceActive)
            {
                _isDragging = false;
                return;
            }

            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead || Main.playerInventory || Main.gameMenu || Main.drawingPlayerChat || Main.editChest || Main.editSign)
            {
                _isDragging = false;
                return;
            }

            if (IsMouseOverToolUI() && !_isDragging)
            {
                return;
            }

            // 关键：开启快速放置时，在世界视口中按住或移动鼠标均强制设为 UI 状态，彻底拦截原版手持物品的单格自动放置/工具挥舞
            player.mouseInterface = true;
            player.controlUseItem = false;

            // 支持鼠标右键取消当前正在进行的框选
            if (Main.mouseRight && _isDragging)
            {
                _isDragging = false;
                SoundEngine.PlaySound(SoundID.MenuClose);
                return;
            }

            int mouseTileX = (int)Math.Floor(Main.MouseWorld.X / 16f);
            int mouseTileY = (int)Math.Floor(Main.MouseWorld.Y / 16f);
            // 在世界输入阶段保存坐标，避免 HUD 绘制阶段的 UI 缩放造成单格预览偏移。
            _hoverTileX = mouseTileX;
            _hoverTileY = mouseTileY;
            _hasHoverTile = true;

            // 鼠标左键按下开始/维持拖拽框选
            if (Main.mouseLeft)
            {
                if (!_isDragging)
                {
                    _isDragging = true;
                    _dragStartX = mouseTileX;
                    _dragStartY = mouseTileY;
                }
                _dragCurX = mouseTileX;
                _dragCurY = mouseTileY;
            }
            else if (_isDragging)
            {
                // 鼠标左键松开，批量执行铺设
                ExecuteDragPlacement(player, _dragStartX, _dragStartY, _dragCurX, _dragCurY);
                _isDragging = false;
            }
        }

        public static void UpdateSpaceWand() => UpdateQuickPlace();

        private static bool IsMouseOverToolUI()
        {
            // Player.Update 后仍处于世界鼠标坐标阶段；只在 UI 命中判断期间切换。
            PlayerInput.SetZoom_UI();
            try
            {
                int mouseX = UIRenderer.MouseX;
                int mouseY = UIRenderer.MouseY;
                bool overQuickPlaceHud = QuickPlaceActive && mouseX >= QuickPlaceHudX && mouseX <= QuickPlaceHudX + QuickPlaceHudW &&
                                         mouseY >= QuickPlaceHudY && mouseY <= QuickPlaceHudY + QuickPlaceHudH;
                bool overLiquidHud = CurrentLiquidMode != LiquidWandMode.Off && mouseX >= LiquidHudX && mouseX <= LiquidHudX + LiquidHudW &&
                                     mouseY >= LiquidHudY && mouseY <= LiquidHudY + LiquidHudH;
                return overQuickPlaceHud || overLiquidHud || UIRenderer.IsMouseOverAnyPanel();
            }
            finally
            {
                PlayerInput.SetZoom_World();
            }
        }

        private static void ExecuteDragPlacement(Player player, int x1, int y1, int x2, int y2)
        {
            if (Main.netMode == 1)
            {
                ToolNetwork.Request("place", player.selectedItem, x1, y1, x2, y2);
                return;
            }
            ApplyDragPlacement(player, x1, y1, x2, y2);
        }

        internal static int ApplyDragPlacement(Player player, int x1, int y1, int x2, int y2)
        {
            var config = SettingsSync.GetPlayerConfig(player.whoAmI);
            if (Main.netMode == 2 && config == null)
            {
                Main.NewText("正在等待个人设置交换确认，请稍后使用建造工具", 255, 180, 100);
                return 0;
            }
            Item held = player.HeldItem;
            if (held == null || held.IsAir)
            {
                Main.NewText("快速放置：手中没有物品！请手持方块、平台或墙壁。", 255, 120, 120);
                return 0;
            }

            bool isTile = held.createTile >= 0;
            bool isWall = held.createWall > 0;

            if (!isTile && !isWall)
            {
                Main.NewText($"快速放置：当前手持物品 [{held.Name}] 不是可放置的方块、平台或背景墙！", 255, 120, 120);
                return 0;
            }

            int minX = Math.Max(10, Math.Min(x1, x2));
            int maxX = Math.Min(Main.maxTilesX - 10, Math.Max(x1, x2));
            int minY = Math.Max(10, Math.Min(y1, y2));
            int maxY = Math.Min(Main.maxTilesY - 10, Math.Max(y1, y2));

            int placed = 0;
            bool noConsume = config != null && config.WandMaterialNoConsume;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    Tile tile = Main.tile[x, y];
                    if (tile == null)
                    {
                        tile = new Tile();
                        Main.tile[x, y] = tile;
                    }

                    if (isTile)
                    {
                        // 放置方块/平台：仅在当前格未激活(为空)时放置，避免覆盖破坏已有实心结构
                        if (!tile.active())
                        {
                            WorldGen.PlaceTile(x, y, held.createTile, false, true, player.whoAmI, held.placeStyle);
                            if (tile.active())
                            {
                                placed++;
                                if (!noConsume && held.stack > 0)
                                {
                                    held.stack--;
                                    if (held.stack <= 0)
                                    {
                                        held.TurnToAir();
                                        Main.NewText($"快速放置：手持物块 [{held.Name}] 已耗尽！共铺设 {placed} 格。", 255, 160, 100);
                                        goto PlacementDone;
                                    }
                                }
                            }
                        }
                    }
                    else if (isWall)
                    {
                        // 放置背景墙：仅在当前格无背景墙时铺设
                        if (tile.wall == 0)
                        {
                            WorldGen.PlaceWall(x, y, held.createWall, false);
                            if (tile.wall > 0)
                            {
                                placed++;
                                if (!noConsume && held.stack > 0)
                                {
                                    held.stack--;
                                    if (held.stack <= 0)
                                    {
                                        held.TurnToAir();
                                        Main.NewText($"快速放置：手持背景墙 [{held.Name}] 已耗尽！共铺设 {placed} 格。", 255, 160, 100);
                                        goto PlacementDone;
                                    }
                                }
                            }
                        }
                    }
                }
            }

        PlacementDone:
            if (placed > 0)
            {
                SoundEngine.PlaySound(SoundID.Dig);
                if (Main.netMode == 2) SyncTileArea(minX, minY, maxX, maxY);
            }
            return placed;
        }

        // 精确将瓦片世界坐标投影为 UI 屏幕坐标 (融合 ZoomMatrix 视距缩放与 UIScale 界面比例)
        public static Rectangle GetTileWorldToScreenRect(int minTileX, int minTileY, int maxTileX, int maxTileY)
        {
            Vector2 worldTopLeft = new Vector2(minTileX * 16f, minTileY * 16f);
            Vector2 worldBottomRight = new Vector2((maxTileX + 1) * 16f, (maxTileY + 1) * 16f);

            Vector2 screenTopLeft = Vector2.Transform(worldTopLeft - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix);
            Vector2 screenBottomRight = Vector2.Transform(worldBottomRight - Main.screenPosition, Main.GameViewMatrix.ZoomMatrix);

            float uiScale = Main.UIScale;
            if (uiScale <= 0.01f) uiScale = 1f;

            int x = (int)Math.Round(screenTopLeft.X / uiScale);
            int y = (int)Math.Round(screenTopLeft.Y / uiScale);
            int w = (int)Math.Round((screenBottomRight.X - screenTopLeft.X) / uiScale);
            int h = (int)Math.Round((screenBottomRight.Y - screenTopLeft.Y) / uiScale);

            return new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
        }

        // ================= 2. 液体魔杖：模式切换、吸收与生成 =================
        public enum LiquidWandMode
        {
            Off = 0,
            Water = 1,
            Lava = 2,
            Shimmer = 3,
            Honey = 4
        }

        public static LiquidWandMode CurrentLiquidMode = LiquidWandMode.Off;
        // 储量使用原版液体单位（255 单位 = 1 格）；单机在游戏进程保留，联机由服务器保存本世界会话库存。
        public static int StoredWater = 0;
        public static int StoredLava = 0;
        public static int StoredShimmer = 0;
        public static int StoredHoney = 0;

        public static void ExecuteLiquidWand()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            CurrentLiquidMode = (LiquidWandMode)(((int)CurrentLiquidMode + 1) % 5);

            switch (CurrentLiquidMode)
            {
                case LiquidWandMode.Off:
                    SoundEngine.PlaySound(SoundID.MenuClose);
                    Main.NewText("液体魔杖: 已关闭", 200, 200, 200);
                    break;
                case LiquidWandMode.Water:
                    SoundEngine.PlaySound(SoundID.MenuOpen);
                    Main.NewText($"液体魔杖: 水模式 [已存流体: {StoredWater / 255.0:0.##} 格（本次游戏）]", 100, 200, 255);
                    break;
                case LiquidWandMode.Lava:
                    SoundEngine.PlaySound(SoundID.MenuOpen);
                    Main.NewText($"液体魔杖: 岩浆模式 [已存流体: {StoredLava / 255.0:0.##} 格（本次游戏）]", 255, 140, 50);
                    break;
                case LiquidWandMode.Shimmer:
                    SoundEngine.PlaySound(SoundID.MenuOpen);
                    Main.NewText($"液体魔杖: 微光模式 [已存流体: {StoredShimmer / 255.0:0.##} 格（本次游戏）]", 255, 180, 255);
                    break;
                case LiquidWandMode.Honey:
                    SoundEngine.PlaySound(SoundID.MenuOpen);
                    Main.NewText($"液体魔杖: 蜂蜜模式 [已存流体: {StoredHoney / 255.0:0.##} 格（本次游戏）]", 255, 220, 100);
                    break;
            }
        }

        public static void UpdateLiquidWand()
        {
            if (CurrentLiquidMode == LiquidWandMode.Off)
                return;

            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead || Main.playerInventory || Main.gameMenu || Main.drawingPlayerChat || Main.editChest || Main.editSign)
                return;

            // 快速放置会在世界视口设置 mouseInterface；面板命中才是真正的 UI 悬停。
            if (IsMouseOverToolUI() || (!QuickPlaceActive && player.mouseInterface))
                return;

            KeyboardState keyState = Keyboard.GetState();
            int mX = (int)(Main.MouseWorld.X / 16f);
            int mY = (int)(Main.MouseWorld.Y / 16f);

            if (mX < 10 || mX >= Main.maxTilesX - 10 || mY < 10 || mY >= Main.maxTilesY - 10)
                return;

            bool absorb = keyState.IsKeyDown(Keys.OemSemicolon);
            bool release = keyState.IsKeyDown(Keys.OemQuotes);
            if (!absorb && !release) return;
            if (Main.netMode == 1)
            {
                ToolNetwork.Request("liquid", -1, mX, mY, (int)CurrentLiquidMode, absorb ? 1 : 0, release ? 1 : 0);
                return;
            }
            ApplyLiquid(mX, mY, CurrentLiquidMode, absorb, release);
        }

        internal static void ApplyLiquid(int mX, int mY, LiquidWandMode mode, bool absorb, bool release)
        {
            // 1. 按住 ; 键 (OemSemicolon)：吸收鼠标指针周围液体
            if (absorb)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int x = mX + dx;
                        int y = mY + dy;
                        if (x < 10 || x >= Main.maxTilesX - 10 || y < 10 || y >= Main.maxTilesY - 10)
                            continue;

                        Tile tile = Main.tile[x, y];
                        if (tile != null && tile.liquid > 0)
                        {
                            int lType = tile.liquidType();
                            StoreLiquid(lType, tile.liquid);

                            tile.liquid = 0;
                            WorldGen.SquareTileFrame(x, y, true);
                            if (Main.netMode == 2)
                            {
                                NetMessage.sendWater(x, y);
                            }
                        }
                    }
                }
            }

            // 2. 按住 ' 键 (OemQuotes)：在鼠标所在格生成流体
            if (release)
            {
                Tile tile = Main.tile[mX, mY];
                if (tile != null && (!tile.active() || !Main.tileSolid[tile.type]))
                {
                    int targetLiquidType = 0;
                    int amount = 0;

                    switch (mode)
                    {
                        case LiquidWandMode.Water:
                            targetLiquidType = 0;
                            amount = TransferLiquid(tile.liquid, tile.liquidType(), targetLiquidType, ref StoredWater);
                            break;
                        case LiquidWandMode.Lava:
                            targetLiquidType = 1;
                            amount = TransferLiquid(tile.liquid, tile.liquidType(), targetLiquidType, ref StoredLava);
                            break;
                        case LiquidWandMode.Honey:
                            targetLiquidType = 2;
                            amount = TransferLiquid(tile.liquid, tile.liquidType(), targetLiquidType, ref StoredHoney);
                            break;
                        case LiquidWandMode.Shimmer:
                            targetLiquidType = 3;
                            amount = TransferLiquid(tile.liquid, tile.liquidType(), targetLiquidType, ref StoredShimmer);
                            break;
                    }

                    if (amount > 0)
                    {
                        tile.liquid = (byte)(tile.liquidType() == targetLiquidType ? tile.liquid + amount : amount);
                        tile.liquidType(targetLiquidType);
                        Liquid.AddWater(mX, mY);
                        WorldGen.SquareTileFrame(mX, mY, true);
                        if (Main.netMode == 2)
                        {
                            NetMessage.sendWater(mX, mY);
                        }
                    }
                }
            }
        }

        private static int TransferLiquid(int currentAmount, int currentType, int targetType, ref int stored)
        {
            int amount = Math.Min(stored, currentType == targetType ? 255 - currentAmount : 255);
            if (amount <= 0) return 0;
            // 保留液体替换功能；原格液体先回收，避免被覆盖时凭空消失。
            if (currentType != targetType) StoreLiquid(currentType, currentAmount);
            stored -= amount;
            return amount;
        }

        private static void StoreLiquid(int liquidType, int amount)
        {
            if (liquidType == 0) StoredWater += amount;
            else if (liquidType == 1) StoredLava += amount;
            else if (liquidType == 2) StoredHoney += amount;
            else if (liquidType == 3) StoredShimmer += amount;
        }

        public static void ResetSession()
        {
            QuickPlaceActive = false;
            CurrentLiquidMode = LiquidWandMode.Off;
            _isDragging = _isDraggingQuickPlaceHud = _isDraggingLiquidHud = false;
            _hasHoverTile = false;
            _dragStartX = _dragStartY = _dragCurX = _dragCurY = _hoverTileX = _hoverTileY = -1;
            _dragOffsetQuickPlaceX = _dragOffsetQuickPlaceY = _dragOffsetLiquidX = _dragOffsetLiquidY = 0;
        }

        // ================= 3. 城镇 NPC 立即回家 (N 键) =================
        public static void ExecuteNPCHome()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            if (Main.netMode == 1) { ToolNetwork.Request("home"); return; }
            int count = ApplyNPCHome();
            SoundEngine.PlaySound(SoundID.MenuOpen);
            Main.NewText($"城镇管理：已将 {count} 位城镇NPC召回房间！", 150, 255, 150);
        }

        internal static int ApplyNPCHome()
        {
            int count = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && npc.townNPC && !npc.homeless && npc.homeTileX > 0 && npc.homeTileY > 0)
                {
                    npc.position.X = npc.homeTileX * 16;
                    npc.position.Y = (npc.homeTileY - 1) * 16;
                    npc.netUpdate = true;
                    if (Main.netMode == 2)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, i);
                    }
                    count++;
                }
            }

            return count;
        }

        // ================= 4. 快捷护士瞬间治疗 (H 键，收费与判空) =================
        public static void ExecuteQuickNurse()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead)
                return;

            if (RemoteStorageOperations.Busy || ToolNetwork.Busy)
            {
                Main.NewText("正在等待服务器物品操作确认，请稍后治疗", 255, 180, 100);
                return;
            }
            int missingLife = Math.Max(0, player.statLifeMax2 - player.statLife);
            if (!NurseTreatment.NeedsTreatment(player))
            {
                Main.NewText(NurseTreatment.NoTreatment, 120, 255, 120);
                return;
            }
            long cost = NurseTreatment.Cost();
            string costStr = FormatCoins(cost);
            if (cost > 0 && !player.BuyItem(cost))
            {
                Main.NewText($"钱币不足！护士治疗需要 {costStr}，护士拒绝治疗！", 255, 100, 100);
                return;
            }

            int healed = (int)missingLife;
            player.statLife = player.statLifeMax2;
            player.statMana = player.statManaMax2;

            NurseTreatment.ClearDebuffs(player);

            player.HealEffect(healed);
            SoundEngine.PlaySound(SoundID.Item4);

            if (Main.netMode == 1)
            {
                NetMessage.SendData(MessageID.PlayerLifeMana, -1, -1, null, player.whoAmI);
                NetMessage.SendData(MessageID.PlayerBuffs, -1, -1, null, player.whoAmI);
            }

            Main.NewText($"护士治疗：已扣除 {costStr}，生命与魔力已回满，负面状态已清除！", 120, 255, 180);
        }

        private static string FormatCoins(long coins)
        {
            long plat = coins / 1000000;
            long gold = (coins % 1000000) / 10000;
            long silver = (coins % 10000) / 100;
            long copper = coins % 100;
            var parts = new List<string>();
            if (plat > 0) parts.Add($"{plat} 铂金币");
            if (gold > 0) parts.Add($"{gold} 金币");
            if (silver > 0) parts.Add($"{silver} 银币");
            if (copper > 0 || parts.Count == 0) parts.Add($"{copper} 铜币");
            return string.Join(" ", parts);
        }

        // ================= 5. 打开随身钱罐 (. 键) =================
        public static void ExecuteOpenBank()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active)
                return;

            if (player.chest == -2)
            {
                player.chest = -1;
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
            else
            {
                player.chest = -2; // -2 为猪猪钱罐
                Main.playerInventory = true;
                SoundEngine.PlaySound(SoundID.MenuOpen);
            }
        }

        // ================= 6. 科技刷墙魔杖 (O 键) =================
        public static void ExecuteWallWand()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            Player player = Main.LocalPlayer;
            if (player == null || !player.active || player.dead)
                return;

            int slot = FindWallSlot(player);
            if (Main.netMode == 1) { ToolNetwork.Request("wall", slot); return; }
            ApplyWallWand(player);
        }

        internal static int FindWallSlot(Player player)
        {
            for (int slot = 0; slot < 50; slot++)
                if (player.inventory[slot] != null && !player.inventory[slot].IsAir && player.inventory[slot].createWall > 0) return slot;
            return -1;
        }

        internal static int ApplyWallWand(Player player)
        {
            var config = SettingsSync.GetPlayerConfig(player.whoAmI);
            if (Main.netMode == 2 && config == null)
            {
                Main.NewText("正在等待个人设置交换确认，请稍后使用建造工具", 255, 180, 100);
                return 0;
            }
            int slot = FindWallSlot(player);
            Item wallMaterial = slot < 0 ? null : player.inventory[slot];

            if (wallMaterial == null)
            {
                Main.NewText("快速铺墙：背包中没有背景墙！", 255, 100, 100);
                return 0;
            }

            int pX = (int)(player.Center.X / 16f);
            int pY = (int)(player.Center.Y / 16f);
            int placed = 0;
            bool noConsume = config != null && config.WandMaterialNoConsume;

            for (int dx = -10; dx <= 10; dx++)
            {
                for (int dy = -10; dy <= 10; dy++)
                {
                    int x = pX + dx;
                    int y = pY + dy;

                    if (x < 10 || x >= Main.maxTilesX - 10 || y < 10 || y >= Main.maxTilesY - 10)
                        continue;

                    Tile tile = Main.tile[x, y];
                    if (tile != null && tile.wall == 0)
                    {
                        WorldGen.PlaceWall(x, y, wallMaterial.createWall, false);
                        if (tile.wall > 0)
                        {
                            placed++;
                            if (!noConsume && wallMaterial.stack > 0)
                            {
                                wallMaterial.stack--;
                                if (wallMaterial.stack <= 0)
                                { wallMaterial.TurnToAir(); goto WallDone; }
                            }
                        }
                    }
                }
            }

        WallDone:
            if (Main.netMode == 2) SyncTileArea(pX - 10, pY - 10, pX + 10, pY + 10);
            SoundEngine.PlaySound(SoundID.Dig);
            return placed;
        }

        // ================= 7. 气象控制 (U 键) =================
        public static void ExecuteWeatherController()
        {
            if (!Mod.WorldActive || Main.gameMenu) return;
            if (Main.netMode == 1) { ToolNetwork.Request("weather"); return; }
            ApplyWeather();
        }

        internal static void ApplyWeather()
        {
            if (!Main.raining)
            {
                Main.StartRain();
                Main.maxRaining = 0.8f;
                SoundEngine.PlaySound(SoundID.Item4);
                Main.NewText("气象控制：已开启暴雨！", 100, 220, 255);
            }
            else
            {
                Main.StopRain();
                SoundEngine.PlaySound(SoundID.Item4);
                Main.NewText("气象控制：天气转晴！", 255, 230, 100);
            }

            if (Main.netMode == 2)
            {
                NetMessage.SendData(MessageID.WorldData);
            }
        }



        internal static void SyncTileArea(int minX, int minY, int maxX, int maxY)
        {
            minX = Math.Max(10, minX); minY = Math.Max(10, minY);
            maxX = Math.Min(Main.maxTilesX - 11, maxX); maxY = Math.Min(Main.maxTilesY - 11, maxY);
            for (int x = minX; x <= maxX; x += 20)
                for (int y = minY; y <= maxY; y += 20)
                    NetMessage.SendTileSquare(-1, x, y, Math.Min(20, maxX - x + 1), Math.Min(20, maxY - y + 1));
        }

        // HUD 坐标与可拖拽支持
        public static int QuickPlaceHudX = 20;
        public static int QuickPlaceHudY = 80;
        public static int QuickPlaceHudW = 300;
        public static int QuickPlaceHudH = 32;
        public static int SpaceWandHudX { get => QuickPlaceHudX; set => QuickPlaceHudX = value; }
        public static int SpaceWandHudY { get => QuickPlaceHudY; set => QuickPlaceHudY = value; }
        public static int SpaceWandHudW { get => QuickPlaceHudW; set => QuickPlaceHudW = value; }
        public static int SpaceWandHudH { get => QuickPlaceHudH; set => QuickPlaceHudH = value; }
        private static bool _isDraggingQuickPlaceHud = false;
        private static int _dragOffsetQuickPlaceX = 0;
        private static int _dragOffsetQuickPlaceY = 0;

        public static int LiquidHudX = 20;
        public static int LiquidHudY = 118;
        public static int LiquidHudW = 390;
        public static int LiquidHudH = 32;
        private static bool _isDraggingLiquidHud = false;
        private static int _dragOffsetLiquidX = 0;
        private static int _dragOffsetLiquidY = 0;

        // ================= 9. 界面 HUD 渲染 (快速放置拖拽框 / 液体魔杖状态栏) =================
        public static void DrawHUD()
        {
            QuickPlaceHudW = Math.Min(300, PanelLayout.ScreenWidth);
            LiquidHudW = Math.Min(390, PanelLayout.ScreenWidth);
            PanelLayout.ClampToScreen(ref QuickPlaceHudX, ref QuickPlaceHudY, QuickPlaceHudW, QuickPlaceHudH);
            PanelLayout.ClampToScreen(ref LiquidHudX, ref LiquidHudY, LiquidHudW, LiquidHudH);
            int mouseX = UIRenderer.MouseX;
            int mouseY = UIRenderer.MouseY;

            // 快速放置提示与拖拽框 (支持拖动)
            if (QuickPlaceActive)
            {
                bool quickPlaceHover = mouseX >= QuickPlaceHudX && mouseX <= QuickPlaceHudX + QuickPlaceHudW &&
                                       mouseY >= QuickPlaceHudY && mouseY <= QuickPlaceHudY + QuickPlaceHudH;

                if (_isDraggingQuickPlaceHud)
                {
                    Main.player[Main.myPlayer].mouseInterface = true;
                    if (Main.mouseLeft)
                    {
                        QuickPlaceHudX = Math.Max(0, Math.Min(UIRenderer.ScreenWidth - QuickPlaceHudW, mouseX - _dragOffsetQuickPlaceX));
                        QuickPlaceHudY = Math.Max(0, Math.Min(UIRenderer.ScreenHeight - QuickPlaceHudH, mouseY - _dragOffsetQuickPlaceY));
                    }
                    else
                    {
                        _isDraggingQuickPlaceHud = false;
                    }
                }
                else if ((UIRenderer.MouseLeftClick || (Main.mouseLeft && Main.mouseLeftRelease)) && quickPlaceHover)
                {
                    _isDraggingQuickPlaceHud = true;
                    _dragOffsetQuickPlaceX = mouseX - QuickPlaceHudX;
                    _dragOffsetQuickPlaceY = mouseY - QuickPlaceHudY;
                    UIRenderer.ConsumeClick();
                }

                if (quickPlaceHover)
                {
                    Main.player[Main.myPlayer].mouseInterface = true;
                }

                Item held = Main.LocalPlayer?.HeldItem;
                string heldName = (held != null && !held.IsAir && (held.createTile >= 0 || held.createWall > 0)) ? held.Name : "未手持方块/平台/墙";

                UIRenderer.DrawPanel(QuickPlaceHudX, QuickPlaceHudY, QuickPlaceHudW, QuickPlaceHudH, 20, 30, 20, 220);
                UIRenderer.DrawRectOutline(QuickPlaceHudX, QuickPlaceHudY, QuickPlaceHudW, QuickPlaceHudH, 100, 255, 150, 255, 1);
                PanelLayout.DrawFittedText($"快速放置: [{heldName}] (左键框选)", QuickPlaceHudX + 10, QuickPlaceHudY + 8, QuickPlaceHudW - 20, 100, 255, 150);

                if (_isDragging)
                {
                    int minX = Math.Min(_dragStartX, _dragCurX);
                    int maxX = Math.Max(_dragStartX, _dragCurX);
                    int minY = Math.Min(_dragStartY, _dragCurY);
                    int maxY = Math.Max(_dragStartY, _dragCurY);

                    // 经过视角缩放矩阵转换的精准屏幕选框
                    Rectangle screenRect = GetTileWorldToScreenRect(minX, minY, maxX, maxY);

                    // 绿色半透明背景填充与亮色边框
                    UIRenderer.DrawRect(screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height, 40, 220, 100, 75);
                    UIRenderer.DrawRectOutline(screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height, 100, 255, 150, 255, 2);

                    // 鼠标旁浮动实时尺寸与铺设说明
                    int wCount = maxX - minX + 1;
                    int hCount = maxY - minY + 1;
                    int totalCount = wCount * hCount;
                    UIRenderer.DrawTextSmall($"铺设 [{heldName}] ({wCount}x{hCount} = {totalCount}格)", mouseX + 16, mouseY + 16, 120, 255, 180, 255);
                }
                else if (_hasHoverTile && !Main.playerInventory && !Main.gameMenu)
                {
                    // 未拖拽时，为鼠标悬停瓦片提供单格准星高亮
                    int curTileX = _hoverTileX;
                    int curTileY = _hoverTileY;
                    Rectangle singleRect = GetTileWorldToScreenRect(curTileX, curTileY, curTileX, curTileY);
                    UIRenderer.DrawRectOutline(singleRect.X, singleRect.Y, singleRect.Width, singleRect.Height, 100, 255, 150, 160, 1);
                }
            }

            // 液体魔杖提示栏 (支持拖动)
            if (CurrentLiquidMode != LiquidWandMode.Off)
            {
                bool liquidHover = mouseX >= LiquidHudX && mouseX <= LiquidHudX + LiquidHudW &&
                                   mouseY >= LiquidHudY && mouseY <= LiquidHudY + LiquidHudH;

                if (_isDraggingLiquidHud)
                {
                    Main.player[Main.myPlayer].mouseInterface = true;
                    if (Main.mouseLeft)
                    {
                        LiquidHudX = Math.Max(0, Math.Min(UIRenderer.ScreenWidth - LiquidHudW, mouseX - _dragOffsetLiquidX));
                        LiquidHudY = Math.Max(0, Math.Min(UIRenderer.ScreenHeight - LiquidHudH, mouseY - _dragOffsetLiquidY));
                    }
                    else
                    {
                        _isDraggingLiquidHud = false;
                    }
                }
                else if ((UIRenderer.MouseLeftClick || (Main.mouseLeft && Main.mouseLeftRelease)) && liquidHover)
                {
                    _isDraggingLiquidHud = true;
                    _dragOffsetLiquidX = mouseX - LiquidHudX;
                    _dragOffsetLiquidY = mouseY - LiquidHudY;
                    UIRenderer.ConsumeClick();
                }

                if (liquidHover)
                {
                    Main.player[Main.myPlayer].mouseInterface = true;
                }

                string modeName = "";
                int count = 0;
                byte r = 255, g = 255, b = 255;

                switch (CurrentLiquidMode)
                {
                    case LiquidWandMode.Water:
                        modeName = "水模式"; count = StoredWater; r = 100; g = 200; b = 255;
                        break;
                    case LiquidWandMode.Lava:
                        modeName = "岩浆模式"; count = StoredLava; r = 255; g = 140; b = 50;
                        break;
                    case LiquidWandMode.Shimmer:
                        modeName = "微光模式"; count = StoredShimmer; r = 255; g = 180; b = 255;
                        break;
                    case LiquidWandMode.Honey:
                        modeName = "蜂蜜模式"; count = StoredHoney; r = 255; g = 220; b = 100;
                        break;
                }

                UIRenderer.DrawPanel(LiquidHudX, LiquidHudY, LiquidHudW, LiquidHudH, 25, 25, 35, 220);
                UIRenderer.DrawRectOutline(LiquidHudX, LiquidHudY, LiquidHudW, LiquidHudH, r, g, b, 255, 1);
                PanelLayout.DrawFittedText($"[液体魔杖: {modeName}] 已存: {count / 255.0:0.##} 格（{(Main.netMode == 1 ? ToolNetwork.LiquidStatus : "本次游戏")}） | [;] 吸收 | ['] 放置", LiquidHudX + 10, LiquidHudY + 8, LiquidHudW - 20, r, g, b);
            }
        }
    }
}

