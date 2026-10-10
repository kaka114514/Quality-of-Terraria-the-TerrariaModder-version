using System;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.Graphics.Capture;

namespace QualityOfTerraria.Patches
{
    /// <summary>
    /// 修复原版 Terraria 在部分分辨率/全屏启动模式下 CaptureManager 构造函数抛出 NullReferenceException
    /// 进而导致 TypeInitializationException 的致命崩溃。
    /// </summary>
    [HarmonyPatch(typeof(CaptureManager), MethodType.Constructor)]
    public static class CaptureManagerPatch
    {
        public static bool Prefix(CaptureManager __instance)
        {
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                // 1. 安全初始化 CaptureInterface
                var interfaceField = typeof(CaptureManager).GetField("_interface", flags);
                if (interfaceField != null)
                {
                    interfaceField.SetValue(__instance, new CaptureInterface());
                }

                // 2. 检查 GraphicsDevice 是否已就绪，通过反射安全初始化 CaptureCamera
                if (!Main.dedServ && Main.instance != null)
                {
                    try
                    {
                        var gdProp = typeof(Main).GetProperty("GraphicsDevice");
                        var dev = gdProp?.GetValue(Main.instance, null);
                        if (dev != null)
                        {
                            var cameraType = typeof(CaptureManager).Assembly.GetType("Terraria.Graphics.Capture.CaptureCamera");
                            if (cameraType != null)
                            {
                                var ctor = cameraType.GetConstructor(flags, null, new[] { dev.GetType() }, null);
                                if (ctor != null)
                                {
                                    var cameraObj = ctor.Invoke(new[] { dev });
                                    var cameraField = typeof(CaptureManager).GetField("_camera", flags);
                                    cameraField?.SetValue(__instance, cameraObj);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // GraphicsDevice 暂未就绪时忽略，TerrariaModder 核心已有 null-camera 完整防御
                    }
                }
            }
            catch
            {
                // 拦截全部异常，确保构造函数成功完成，彻底避免触发 TypeInitializationException
            }

            return false; // 跳过原版构造函数，彻底杜绝崩溃
        }
    }
}
