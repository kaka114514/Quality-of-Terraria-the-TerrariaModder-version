using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Terraria;

namespace QualityOfTerraria.Patches
{
    // 与参考模组相同：让按住下方向键时，原版的两个平台穿透标志同时生效。
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class PlatformFallthroughPatch
    {
        private static bool ShouldFallThrough(Player player)
            => player != null && player.controlDown && (Mod.Config == null || Mod.Config.PlatformFallthroughEnabled);

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            FieldInfo velocity = AccessTools.Field(typeof(Entity), nameof(Entity.velocity));
            FieldInfo slideDir = AccessTools.Field(typeof(Player), nameof(Player.slideDir));
            FieldInfo controlDown = AccessTools.Field(typeof(Player), nameof(Player.controlDown));
            int match = -1;
            object ignorePlatforms = null;
            object fallThrough = null;

            for (int i = 0; i + 14 < code.Count; i++)
            {
                if (!IsLdarg0(code[i]) || !code[i + 1].LoadsField(velocity) || !IsStoreLocal(code[i + 2])) continue;
                int offset = 0;
                if (i + 5 < code.Count && IsLdarg0(code[i + 3]) && code[i + 4].LoadsField(slideDir) && IsStoreLocal(code[i + 5])) offset = 3;
                int clear = i + 3 + offset;
                if (clear + 7 >= code.Count || !IsLdarg0(code[clear]) || !IsLdcI4Zero(code[clear + 1]) ||
                    !code[clear + 2].StoresField(slideDir) || !IsLdcI4Zero(code[clear + 3]) ||
                    !IsStoreLocal(code[clear + 4]) || !IsLdarg0(code[clear + 5]) ||
                    !code[clear + 6].LoadsField(controlDown) || !IsStoreLocal(code[clear + 7]) ||
                    code[clear + 4].operand == null || code[clear + 7].operand == null) continue;
                match = clear + 3;
                ignorePlatforms = code[clear + 4].operand;
                fallThrough = code[clear + 7].operand;
                break;
            }
            if (match < 0) throw new InvalidOperationException("未找到 Player.Update 的平台穿透局部变量初始化序列");
            var replacement = new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, controlDown),
                NewStoreLocal(fallThrough),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PlatformFallthroughPatch), nameof(ShouldFallThrough))),
                NewStoreLocal(ignorePlatforms)
            };
            replacement[0].labels.AddRange(code[match].labels);
            code.RemoveRange(match, 5);
            code.InsertRange(match, replacement);
            return code;
        }

        private static bool IsLdarg0(CodeInstruction instruction)
            => instruction.opcode == OpCodes.Ldarg_0;

        private static bool IsLdcI4Zero(CodeInstruction instruction)
            => instruction.opcode == OpCodes.Ldc_I4_0 || (instruction.opcode == OpCodes.Ldc_I4_S && instruction.operand is sbyte value && value == 0);

        private static bool IsStoreLocal(CodeInstruction instruction)
            => instruction.opcode == OpCodes.Stloc || instruction.opcode == OpCodes.Stloc_S ||
               instruction.opcode == OpCodes.Stloc_0 || instruction.opcode == OpCodes.Stloc_1 || instruction.opcode == OpCodes.Stloc_2 || instruction.opcode == OpCodes.Stloc_3;

        private static CodeInstruction NewStoreLocal(object local)
        {
            if (local is int index) return CodeInstruction.StoreLocal(index);
            return new CodeInstruction(OpCodes.Stloc, local);
        }
    }
}
