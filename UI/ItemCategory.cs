using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.Creative;

namespace QualityOfTerraria.UI
{
    internal static class ItemCategory
    {
        internal static readonly string[] Names = { "全部", "武器", "盔甲", "时装", "物块", "家具", "配饰", "装备", "消耗品", "工具", "材料", "其他" };
        internal static readonly StorageTab[] Tabs = {
            StorageTab.All, StorageTab.Weapons, StorageTab.Armor, StorageTab.Vanity, StorageTab.BuildingBlocks,
            StorageTab.FurnitureItems, StorageTab.Accessories, StorageTab.Equipment, StorageTab.Consumables,
            StorageTab.Tools, StorageTab.Materials, StorageTab.Other
        };
        private static readonly List<IItemEntryFilter> Filters = new List<IItemEntryFilter> {
            new ItemFilters.Weapon(), new ItemFilters.Armor(), new ItemFilters.Vanity(),
            new ItemFilters.BuildingBlock(), new ItemFilters.Furniture(), new ItemFilters.Accessories(),
            new ItemFilters.MiscAccessories(), new ItemFilters.Consumables(), new ItemFilters.Tools(), new ItemFilters.Materials()
        };
        // 仅用于种类排序选定分组；分类筛选仍保留原版允许的重叠。
        private static readonly int[] SortPriority = { 2, 3, 6, 7, 9, 1, 4, 5, 8, 10, 11 };

        static ItemCategory()
        {
            Filters.Add(new ItemFilters.MiscFallback(new List<IItemEntryFilter>(Filters)));
        }

        internal static bool Matches(Item item, int category)
        {
            if (item == null || item.IsAir || category < 0 || category > Filters.Count) return false;
            return category == 0 || Filters[category - 1].FitsFilter(item);
        }

        internal static int GetSortOrder(Item item)
        {
            foreach (int category in SortPriority)
                if (Matches(item, category)) return category;
            return Names.Length;
        }
    }
}
