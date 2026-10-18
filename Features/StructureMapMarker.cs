using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Map;
using Terraria.UI;

namespace QualityOfTerraria.Features
{
    // 结构定位结果的地图标记。原版玩家标记尺寸固定且偏小，这里改用地图层绘制更大的彩色脉冲标记，
    // 鼠标悬停在标记上时在光标旁显示结构名称。标记数据只保留到退出当前世界。
    internal sealed class StructureMapMarker : IMapLayer
    {
        private static StructureMapMarker _layer;
        private static MapIconOverlay _owner;

        internal static void EnsureRegistered()
        {
            if (Main.dedServ || Main.MapIcons == null) return;
            if (_layer != null && ReferenceEquals(_owner, Main.MapIcons)) return;
            _layer = new StructureMapMarker();
            Main.MapIcons.AddLayer(_layer);
            _owner = Main.MapIcons;
        }

        public void Draw(ref MapOverlayDrawContext context, ref string text)
        {
            if (Main.gameMenu) return;
            IReadOnlyList<KeyValuePair<StructureLocator.StructureType, Point>> markers = StructureLocator.Markers;
            if (markers.Count == 0) return;
            var frame = new SpriteFrame(1, 5);
            frame.CurrentRow = (byte)((Main.GameUpdateCount / 6UL) % 5UL);
            Texture2D texture = TextureAssets.MapPing.Value;
            for (int i = 0; i < markers.Count; i++)
            {
                KeyValuePair<StructureLocator.StructureType, Point> marker = markers[i];
                Vector2 position = new Vector2(marker.Value.X + 0.5f, marker.Value.Y + 0.5f);
                Color color = GetColor(marker.Key);
                // 外层光晕加内层脉冲，保证在小地图和全屏地图上都醒目。
                context.Draw(texture, position, color * 0.35f, frame, 3.4f, 3.8f, Alignment.Center);
                if (!context.Draw(texture, position, color, frame, 1.9f, 2.2f, Alignment.Center).IsMouseOver) continue;
                text = StructureLocator.GetStructureName(marker.Key);
                // 右键取消这个标记；消费掉本次右键，避免同一帧再次触发。
                if (Main.mouseRight && Main.mouseRightRelease)
                {
                    StructureLocator.RemoveMarker(marker.Key);
                    Main.mouseRightRelease = false;
                }
            }
        }

        private static Color GetColor(StructureLocator.StructureType type)
        {
            switch (type)
            {
                case StructureLocator.StructureType.Aether: return new Color(255, 140, 255);
                case StructureLocator.StructureType.Dungeon: return new Color(120, 140, 255);
                case StructureLocator.StructureType.Temple: return new Color(150, 255, 120);
                case StructureLocator.StructureType.FloatingIsland: return new Color(140, 230, 255);
                case StructureLocator.StructureType.Pyramid: return new Color(255, 220, 120);
                case StructureLocator.StructureType.PlanteraBulb: return new Color(255, 120, 200);
                case StructureLocator.StructureType.EnchantedSword: return new Color(180, 200, 255);
                case StructureLocator.StructureType.BeeHive: return new Color(255, 200, 80);
                case StructureLocator.StructureType.MarbleCave: return new Color(235, 235, 235);
                default: return new Color(150, 165, 200);
            }
        }
    }
}
