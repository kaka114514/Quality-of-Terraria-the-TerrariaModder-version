using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;
using Terraria.Enums;
using Terraria.ID;
using System.Linq;
using TerrariaModder.Core.Config;
using TerrariaModder.Core.Net;
using QualityOfTerraria.Networking;

namespace QualityOfTerraria.Patches
{
    public static class WorldQoLCompletionPatches
    {
        public static float HostileSpawnRateMultiplier => Mod.Config?.HostileSpawnRateMultiplier ?? 1f;
        public static float CritterSpawnRateMultiplier => Mod.Config?.CritterSpawnRateMultiplier ?? 1f;
        private static bool _naturalSpawnAttempt;
        public static string SpawnRateStatus => Main.netMode == 1 ? NetSync.LocalPlayerIsAdmin ? "房间规则：管理员可调整" : "房间规则：由房主／管理员调整" : "敌对 " + HostileSpawnRateMultiplier.ToString("0.#") + "× · 小动物 " + CritterSpawnRateMultiplier.ToString("0.#") + "×";

        public static bool SetSpawnRates(float hostile, float critter)
        {
            if (Main.netMode == 1) return false;
            if (Mod.Config == null || float.IsNaN(hostile) || float.IsInfinity(hostile) || float.IsNaN(critter) || float.IsInfinity(critter)) return false;
            float previousHostile = Mod.Config.HostileSpawnRateMultiplier, previousCritter = Mod.Config.CritterSpawnRateMultiplier;
            Mod.Config.HostileSpawnRateMultiplier = Math.Max(0f, Math.Min(500f, hostile));
            Mod.Config.CritterSpawnRateMultiplier = Math.Max(0f, Math.Min(500f, critter));
            string error;
            bool saved = Main.netMode == 2 ? ConfigManager.GetCurrentScope() == ConfigScope.Server && SettingsSync.SaveVerified(Mod.Config, out error)
                : LocalWorldSettings.Save(out error);
            if (!saved) { Mod.Config.HostileSpawnRateMultiplier = previousHostile; Mod.Config.CritterSpawnRateMultiplier = previousCritter; return false; }
            if (Main.netMode == 2)
            {
                foreach (string key in new[] { nameof(QualityOfTerrariaConfig.HostileSpawnRateMultiplier), nameof(QualityOfTerrariaConfig.CritterSpawnRateMultiplier) })
                {
                    var metadata = Mod.Config.GetPropertyMetadata().First(p => p.Property.Name == key);
                    NetSync.BroadcastConfigChange("quality-of-terraria", metadata, Mod.Config, -1);
                }
            }
            return true;
        }

        public static bool SetSingleSpawnRate(int kind, float value)
        {
            if (Main.netMode == 1 || Mod.Config == null || (kind != 0 && kind != 1) ||
                float.IsNaN(value) || float.IsInfinity(value)) return false;
            float clamped = Math.Max(0f, Math.Min(500f, value));
            float hostile = kind == 0 ? clamped : Mod.Config.HostileSpawnRateMultiplier;
            float critter = kind == 1 ? clamped : Mod.Config.CritterSpawnRateMultiplier;
            float previousHostile = Mod.Config.HostileSpawnRateMultiplier;
            float previousCritter = Mod.Config.CritterSpawnRateMultiplier;
            if (kind == 0) Mod.Config.HostileSpawnRateMultiplier = clamped;
            else Mod.Config.CritterSpawnRateMultiplier = clamped;
            string error;
            bool saved = Main.netMode == 2 ? ConfigManager.GetCurrentScope() == ConfigScope.Server && SettingsSync.SaveVerified(Mod.Config, out error)
                : LocalWorldSettings.Save(out error);
            if (!saved)
            {
                Mod.Config.HostileSpawnRateMultiplier = previousHostile;
                Mod.Config.CritterSpawnRateMultiplier = previousCritter;
                return false;
            }
            if (Main.netMode == 2)
            {
                string key = kind == 0 ? nameof(QualityOfTerrariaConfig.HostileSpawnRateMultiplier) : nameof(QualityOfTerrariaConfig.CritterSpawnRateMultiplier);
                var metadata = Mod.Config.GetPropertyMetadata().First(p => p.Property.Name == key);
                NetSync.BroadcastConfigChange("quality-of-terraria", metadata, Mod.Config, -1);
            }
            return true;
        }

        public static void ResetSession() { }

        internal static void ScaleSpawnRate(ref int spawnRate, ref int maxSpawns, float multiplier)
        {
            if (multiplier <= 0f) { spawnRate = int.MaxValue; maxSpawns = 0; return; }
            spawnRate = Math.Max(1, (int)Math.Round(spawnRate / (double)multiplier));
            maxSpawns = Math.Max(1, (int)Math.Round(maxSpawns * (double)multiplier));
        }

        internal static float GetCandidateSpawnScale(int npcType)
        {
            if (npcType < 0 || npcType >= NPCID.Sets.CountsAsCritter.Length) return 1f;
            float max = Math.Max(HostileSpawnRateMultiplier, CritterSpawnRateMultiplier);
            bool smallCritter = NPCID.Sets.CountsAsCritter[npcType] &&
                !NPCID.Sets.IsTownPet[npcType] && !NPCID.Sets.IsTownSlime[npcType];
            float selected = smallCritter ? CritterSpawnRateMultiplier : HostileSpawnRateMultiplier;
            return max <= 0f ? 0f : selected / max;
        }

        [HarmonyPatch]
        public static class SpawnRatePatch
        {
            public static MethodBase TargetMethod() => AccessTools.Method(AccessTools.Inner(typeof(NPC), "Spawner"), "GetSpawnRate");
            public static void Postfix(ref int spawnRate, ref int maxSpawns)
            { if (Main.netMode != 1) ScaleSpawnRate(ref spawnRate, ref maxSpawns, Math.Max(HostileSpawnRateMultiplier, CritterSpawnRateMultiplier)); }
        }

        [HarmonyPatch]
        public static class NaturalSpawnCandidatePatch
        {
            private static MethodBase SpawnCandidate() => AccessTools.Method(
                AccessTools.Inner(typeof(NPC), "Spawner"), "SpawnNPC",
                new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(float), typeof(float), typeof(float), typeof(int) });

            [HarmonyTargetMethod]
            private static MethodBase TargetMethod() => SpawnCandidate();

            private static bool Prefix(int __2)
            {
                if (Main.netMode == 1 || !_naturalSpawnAttempt || __2 < 0 || __2 >= NPCID.Sets.CountsAsCritter.Length) return true;
                float scale = GetCandidateSpawnScale(__2);
                return scale >= 1f || (scale > 0f && Main.rand.NextDouble() < scale);
            }
        }

        [HarmonyPatch(typeof(NPC), "SpawnNPC")]
        public static class NaturalSpawnScopePatch
        {
            private static void Prefix() { _naturalSpawnAttempt = true; }
            private static void Postfix() { _naturalSpawnAttempt = false; }
        }

        // 只阻止自然刷怪循环，不影响显式召唤 Boss 或城镇 NPC。
        [HarmonyPatch(typeof(NPC), nameof(NPC.SpawnNPC))]
        public static class ZeroSpawnRatePatch
        {
            public static bool Prefix() => Main.netMode == 1 || Math.Max(HostileSpawnRateMultiplier, CritterSpawnRateMultiplier) > 0f;
        }

        [HarmonyPatch(typeof(WorldGen), "AttemptToGrowTreeFromSapling")]
        public static class TreeDepthPatch
        {
            public static void Prefix(ref bool __2)
            {
                if (Main.netMode != 1 && Mod.Config != null && Mod.Config.FastTreeGrowth) __2 = false;
            }
        }

        [HarmonyPatch]
        public static class TreeGrowthPatch
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(WorldGen), "UpdateWorld_OvergroundTile");
                yield return AccessTools.Method(typeof(WorldGen), "UpdateWorld_UndergroundTile");
            }

            public static void Postfix(int i, int j)
            {
                if (Main.netMode == 1 || Mod.Config == null || !Mod.Config.FastTreeGrowth || !WorldGen.InWorld(i, j, 2)) return;
                Tile tile = Main.tile[i, j];
                if (tile != null && tile.active() && (tile.type == TileID.Saplings || tile.type == 590 || tile.type == 595 || tile.type == 615))
                    WorldGen.AttemptToGrowTreeFromSapling(i, j, false, 0, false);
            }
        }

        internal static int LifeFruitRollRange(int original)
        {
            return Main.netMode != 1 && Mod.Config != null && Mod.Config.FastLifeFruitGrowth ? Math.Max(1, original / 5) : original;
        }

        [HarmonyPatch(typeof(WorldGen), "UpdateWorld_GrassGrowth")]
        public static class LifeFruitGrowthPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> code = new List<CodeInstruction>(instructions);
                FieldInfo progression = AccessTools.Field(typeof(NPC), nameof(NPC.downedMechBossAny));
                MethodInfo next = AccessTools.Method(typeof(Terraria.Utilities.UnifiedRandom), "Next", new[] { typeof(int) });
                for (int i = 0; i < code.Count - 5; i++)
                {
                    // 1.4.5.8：机械 Boss 门槛后的第一处随机判定专属生命果；保留阶段、空间及密度限制。
                    if (!code[i].LoadsField(progression) || !code[i + 4].Calls(next)) continue;
                    code.Insert(i + 4, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WorldQoLCompletionPatches), nameof(LifeFruitRollRange))));
                    return code;
                }
                throw new InvalidOperationException("未找到 Terraria 1.4.5.8 生命果随机判定，停止应用速生补丁。");
            }
        }

        private delegate int CountTileCategory(int[] counts, TileScanGroup category);
        private static readonly CountTileCategory CountCategory = AccessTools.MethodDelegate<CountTileCategory>(AccessTools.Method(typeof(WorldGen), "GetTileTypeCountByCategory"));
        public static int HousingTileCount(int[] counts, TileScanGroup category)
        {
            if (Main.netMode != 1 && Mod.Config != null && Mod.Config.TownNPCLiveInEvil && category == TileScanGroup.TotalGoodEvil) return 0;
            return CountCategory(counts, category);
        }

        [HarmonyPatch(typeof(WorldGen), "ScoreRoom")]
        public static class EvilHousingPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                MethodInfo original = AccessTools.Method(typeof(WorldGen), "GetTileTypeCountByCategory");
                foreach (CodeInstruction code in instructions)
                {
                    if (code.Calls(original)) code.operand = AccessTools.Method(typeof(WorldQoLCompletionPatches), nameof(HousingTileCount));
                    yield return code;
                }
            }
        }

        internal static bool IsPlayerBomb(int type)
        {
            return type == ProjectileID.Bomb || type == ProjectileID.Dynamite || type == ProjectileID.StickyBomb ||
                type == ProjectileID.StickyDynamite || type == ProjectileID.BouncyBomb || type == ProjectileID.BouncyDynamite ||
                type == ProjectileID.BombFish || type == ProjectileID.Explosives || type == ProjectileID.ScarabBomb ||
                type == ProjectileID.WetBomb || type == ProjectileID.LavaBomb || type == ProjectileID.HoneyBomb ||
                type == ProjectileID.DryBomb || type == ProjectileID.DirtBomb || type == ProjectileID.DirtStickyBomb ||
                type == ProjectileID.FreezeBomb || type == ProjectileID.SuperBomb || type == ProjectileID.SuperStickyBomb;
        }

        [HarmonyPatch(typeof(Projectile), "Damage_EVP")]
        public static class BombPlayerDamagePatch
        {
            public static bool Prefix(Projectile __instance)
            {
                return Mod.Config == null || !Mod.Config.BombsNoPlayerDamage || !IsPlayerBomb(__instance.type) ||
                    (__instance.owner != Main.myPlayer && __instance.type != ProjectileID.Explosives);
            }
        }
    }
}
