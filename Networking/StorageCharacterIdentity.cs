using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Terraria;
using Terraria.IO;
using Terraria.Utilities;

namespace QualityOfTerraria.Networking
{
    internal static class StorageCharacterIdentity
    {
        private const string Suffix = ".qot-character-id";

        internal static string GetOrCreate(PlayerFileData file)
        {
            if (file == null || string.IsNullOrEmpty(file.Path) || file.ServerSideCharacter)
                throw new InvalidDataException("无法确认本地角色存档，未启用角色箱子标记");
            string path = file.Path + Suffix;
            if (FileUtilities.Exists(path, file.IsCloudSave))
            {
                string value = Encoding.UTF8.GetString(FileUtilities.ReadAllBytes(path, file.IsCloudSave)).Trim();
                Guid id;
                if (!Guid.TryParseExact(value, "N", out id) || id == Guid.Empty)
                    throw new InvalidDataException("角色 ID 文件损坏，请保留文件并恢复备份：" + path);
                return id.ToString("N");
            }
            string created = Guid.NewGuid().ToString("N");
            Save(file, created);
            return created;
        }

        private static void Save(PlayerFileData file, string id)
        {
            string path = file.Path + Suffix;
            byte[] bytes = Encoding.UTF8.GetBytes(id);
            if (file.IsCloudSave)
            {
                if (!FileUtilities.WriteAllBytes(path, bytes, true)) throw new IOException("无法保存云端角色 ID");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            if (Encoding.UTF8.GetString(FileUtilities.ReadAllBytes(path, file.IsCloudSave)) != id)
                throw new IOException("角色 ID 保存核验失败");
        }

        // 新角色即便复用了已删除角色的文件名，也不能继承旧角色身份。
        [HarmonyPatch(typeof(PlayerFileData), nameof(PlayerFileData.CreateAndSave))]
        internal static class CreatePatch
        {
            public static void Postfix(PlayerFileData __result)
            {
                if (Main.netMode == 2 || __result == null) return;
                try { Save(__result, Guid.NewGuid().ToString("N")); }
                catch (Exception error) { Mod.LogError("保存新角色 ID 失败", error); }
            }
        }

        [HarmonyPatch]
        internal static class MovePatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(PlayerFileData), nameof(PlayerFileData.MoveToCloud));
                yield return AccessTools.Method(typeof(PlayerFileData), nameof(PlayerFileData.MoveToLocal));
            }

            public static void Prefix(PlayerFileData __instance, out string __state)
            {
                __state = null;
                if (Main.netMode == 2) return;
                try { __state = GetOrCreate(__instance); }
                catch (Exception error) { Mod.LogError("读取迁移角色 ID 失败", error); }
            }

            public static void Postfix(PlayerFileData __instance, string __state)
            {
                if (__state == null) return;
                try { Save(__instance, __state); }
                catch (Exception error) { Mod.LogError("迁移角色 ID 失败", error); }
            }
        }
    }
}
