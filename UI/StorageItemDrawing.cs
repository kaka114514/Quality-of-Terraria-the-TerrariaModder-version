using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.UI;

namespace QualityOfTerraria.UI
{
    internal static class StorageItemDrawing
    {
        internal static void DrawIcon(Item item, int x, int y, int width, int height)
        {
            if (item == null || item.IsAir || Main.spriteBatch == null) return;
            ItemSlot.DrawItemIcon(item, ItemSlot.Context.ChestItem, Main.spriteBatch,
                new Vector2(x + width / 2f, y + height / 2f), 1f, Math.Min(width, height), Color.White);
        }
    }
}
