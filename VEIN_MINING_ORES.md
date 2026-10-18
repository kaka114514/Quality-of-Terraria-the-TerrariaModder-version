# 连锁挖矿支持的矿石列表

连锁挖矿功能通过 `TileID.Sets.Ore[type]` 判断，会自动连锁挖掘以下所有泰拉瑞亚原版矿石：

# 连锁挖矿支持的矿石列表

连锁挖矿功能通过 `TileID.Sets.Ore[type]` 和手动添加的宝石类型判断，会自动连锁挖掘以下所有泰拉瑞亚原版矿石：

## 宝石（Gems）- 已手动添加支持
- **紫水晶** (Amethyst, TileID 63)
- **黄玉** (Topaz, TileID 64)
- **蓝宝石** (Sapphire, TileID 65)
- **绿宝石** (Emerald, TileID 66)
- **红宝石** (Ruby, TileID 67)
- **钻石** (Diamond, TileID 68)
- **琥珀** (Amber, TileID 178)

**注意**：宝石方块在泰拉瑞亚原版中**不在** `TileID.Sets.Ore` 中，因此本模组手动添加了宝石支持。

## 前期矿石（困难模式前）
- **铜矿石** (Copper Ore)
- **锡矿石** (Tin Ore)
- **铁矿石** (Iron Ore)
- **铅矿石** (Lead Ore)
- **银矿石** (Silver Ore)
- **钨矿石** (Tungsten Ore)
- **金矿石** (Gold Ore)
- **铂金矿石** (Platinum Ore)
- **陨石** (Meteorite)
- **魔矿** (Demonite Ore)
- **血腥矿** (Crimtane Ore)
- **黑曜石** (Obsidian) - 如果被标记为矿石
- **狱石** (Hellstone)

## 困难模式矿石
- **钴矿石** (Cobalt Ore)
- **钯金矿石** (Palladium Ore)
- **秘银矿石** (Mythril Ore)
- **山铜矿石** (Orichalcum Ore)
- **精金矿石** (Adamantite Ore)
- **钛金矿石** (Titanium Ore)
- **叶绿矿石** (Chlorophyte Ore)

## 后期矿石
- **夜明矿** (Luminite) - 月亮领主掉落

## 连锁规则
- **精确匹配模式** (`VeinMiningMatchExactType = true`，默认开启)：只连锁相同类型的矿石（例如只挖铜矿，不会连到铁矿）
- **镐力检查** (`VeinMiningCheckPickaxePower = true`，默认开启)：只连锁当前镐子能挖的矿石
- **连锁范围**：默认 30 格（`VeinMiningRange`）
- **最大数量**：默认 500 块（`VeinMiningMaxBlocks`）

## 判断逻辑
代码位置：`Features/VeinMiningSystem.cs` 第 60 行
```csharp
internal static bool IsOre(ushort type) => type < TileID.Sets.Ore.Length && TileID.Sets.Ore[type];
```

这个判断会识别所有被泰拉瑞亚引擎标记为矿石的方块类型，包括原版和模组添加的矿石（如果模组正确设置了 `TileID.Sets.Ore`）。
