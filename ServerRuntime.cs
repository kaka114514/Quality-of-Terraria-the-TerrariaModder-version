using TerrariaModder.Core;

namespace QualityOfTerraria
{
#if QOT_SERVER
    // Core 0.4.1 专服不安装 FrameEvents；在服务器主线程处理世界会话和网络请求。
    [HarmonyLib.HarmonyPatch(typeof(Terraria.Main), "DoUpdate")]
    internal static class ServerUpdatePatch
    {
        public static void Prefix() => Mod.UpdateServerSession();
    }
#else
    internal static class ServerRuntime
    {
        internal static IMod Load(ModContext context)
        {
            string path = System.IO.Path.Combine(context.ModFolder, "QualityOfTerraria.Server.bin");
            // 使用 .bin 防止 Injector 把服务器模块当成客户端模组扫描并自动 PatchAll。
            var assembly = System.Reflection.Assembly.LoadFrom(path);
            var mod = (IMod)System.Activator.CreateInstance(assembly.GetType("QualityOfTerraria.Mod", true));
            mod.Initialize(context);
            return mod;
        }
    }
#endif
}
