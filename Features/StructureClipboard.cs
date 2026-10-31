using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO.Compression;
using Microsoft.Xna.Framework;
using QualityOfTerraria.UI;
using TerrariaModder.Core.UI;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;
using Terraria.Localization;

namespace QualityOfTerraria.Features
{
    // 本地结构剪贴板只保存方块/墙及其基础帧信息，不保存箱子内容或独立实体。
    internal static class StructureClipboard
    {
        internal const int MaxStructures = 100;
        internal const int MaxTiles = 10000;
        private const int FormatVersion = 1;
        private static string _path;
        private static readonly List<Structure> Items = new List<Structure>();
        private static readonly Dictionary<string, int> MaterialItems = new Dictionary<string, int>();
        private static int _selectedIndex = -1;
        private static bool _pastePreview;
        private static bool _missingPasteNotified;
        private static bool _initialized;
        private static int _previewX, _previewY;

        internal static IReadOnlyList<Structure> Structures => Items;
        internal static Structure SelectedStructure => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null;
        internal static bool IsPastePreview => _pastePreview;
        internal static int PreviewX => _previewX;
        internal static int PreviewY => _previewY;
        // 用尺寸减一后再居中，避免偶数宽高的结构整体向左上偏一格。
        internal static int GetPasteAnchorX(int cursorX) => SelectedStructure == null ? cursorX : cursorX - (SelectedStructure.Width - 1) / 2;
        internal static int GetPasteAnchorY(int cursorY) => SelectedStructure == null ? cursorY : cursorY - (SelectedStructure.Height - 1) / 2;

        internal static void Initialize()
        {
            _initialized = false;
            Items.Clear();
            _path = null;
            _selectedIndex = -1;
            _pastePreview = false;
            _missingPasteNotified = false;
            if (Mod.Config == null || string.IsNullOrEmpty(Mod.Config.FilePath)) return;
            string directory = Path.GetDirectoryName(Mod.Config.FilePath);
            if (string.IsNullOrEmpty(directory)) return;
            _path = Path.Combine(directory, "quality-of-terraria.structures.json");
            _initialized = true;
            try
            {
                if (File.Exists(_path))
                {
                    FileModel file = Read(_path);
                    if (file != null && file.Version == FormatVersion && file.Structures != null)
                        Items.AddRange(file.Structures.Where(IsValid).Take(MaxStructures));
                }
                _selectedIndex = Items.Count > 0 ? 0 : -1;
            }
            catch (Exception error) { Mod.LogError("读取结构剪贴板失败", error); }
        }

        internal static void EnsureInitialized()
        {
            if (!_initialized) Initialize();
        }

        internal static void Reset() { Items.Clear(); _path = null; _selectedIndex = -1; _pastePreview = false; _missingPasteNotified = false; _initialized = false; }

        internal static Structure Capture(int[,] tiles, int[,] walls, short[,] frameX = null, short[,] frameY = null)
        {
            if (tiles == null || walls == null) throw new ArgumentNullException("tiles");
            int width = tiles.GetLength(0), height = tiles.GetLength(1);
            if (width <= 0 || height <= 0 || (long)width * height > MaxTiles) throw new ArgumentException("结构大小超过限制");
            var cells = new Cell[width * height];
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                int index = y * width + x;
                cells[index] = new Cell
                {
                    TileType = Math.Max(0, tiles[x, y]),
                    Active = tiles[x, y] > 0,
                    MaterialItemType = tiles[x, y] > 0 ? FindMaterialItem(tiles[x, y], false) : 0,
                    WallType = Math.Max(0, walls[x, y]),
                    WallMaterialItemType = walls[x, y] > 0 ? FindMaterialItem(walls[x, y], true) : 0,
                    FrameX = frameX == null ? (short)0 : frameX[x, y],
                    FrameY = frameY == null ? (short)0 : frameY[x, y]
                };
            }
            return new Structure { Name = "未命名结构", Width = width, Height = height, Cells = cells };
        }

        internal static bool Save(Structure structure, out string error)
        {
            EnsureInitialized();
            error = null;
            if (!IsValid(structure)) { error = "结构数据无效"; return false; }
            if (Items.Count >= MaxStructures) { error = "结构保存数量已达到100个上限"; return false; }
            structure.Name = NormalizeName(structure.Name, "结构" + (Items.Count + 1));
            Items.Add(Clone(structure));
            return SaveFile(out error);
        }

        internal static bool Rename(int index, string name, out string error)
        {
            error = null;
            if (index < 0 || index >= Items.Count) { error = "结构不存在"; return false; }
            name = NormalizeName(name, null);
            if (name == null) { error = "结构名称无效"; return false; }
            Items[index].Name = name;
            return SaveFile(out error);
        }

        internal static bool Delete(int index, out string error)
        {
            error = null;
            if (index < 0 || index >= Items.Count) { error = "结构不存在"; return false; }
            Structure removed = Items[index];
            Items.RemoveAt(index);
            if (SaveFile(out error)) return true;
            Items.Insert(index, removed);
            return false;
        }

        internal static void CaptureSelection(int x1, int y1, int x2, int y2)
        {
            if (Main.tile == null) { Main.NewText("结构复制：世界方块尚未加载", 255, 160, 100); return; }
            int minX = Math.Max(1, Math.Min(x1, x2)), maxX = Math.Min(Main.maxTilesX - 2, Math.Max(x1, x2));
            int minY = Math.Max(1, Math.Min(y1, y2)), maxY = Math.Min(Main.maxTilesY - 2, Math.Max(y1, y2));
            int width = maxX - minX + 1, height = maxY - minY + 1;
            if (width <= 0 || height <= 0 || (long)width * height > MaxTiles) { Main.NewText("结构复制：选区超过10000格上限", 255, 160, 100); return; }
            var cells = new Cell[width * height];
            for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            {
                Tile tile = Main.tile[minX + x, minY + y];
                var cell = new Cell();
                if (tile != null)
                {
                    cell.WallType = tile.wall; cell.WallMaterialItemType = tile.wall > 0 ? FindMaterialItem(tile.wall, true) : 0; cell.WallColor = tile.wallColor();
                    cell.WallFrameX = tile.wallFrameX(); cell.WallFrameY = tile.wallFrameY(); cell.WallFrameNumber = tile.wallFrameNumber();
                    if (tile.active() && !IsContainer(tile.type) && !HasIndependentEntity(minX + x, minY + y))
                    {
                        cell.Active = true; cell.TileType = tile.type; cell.MaterialItemType = FindMaterialItem(tile.type, false); cell.FrameX = tile.frameX; cell.FrameY = tile.frameY; cell.TileFrameNumber = tile.frameNumber();
                        cell.TileColor = tile.color(); cell.Slope = tile.slope(); cell.HalfBrick = tile.halfBrick();
                        cell.WireMask = (byte)((tile.wire() ? 1 : 0) | (tile.wire2() ? 2 : 0) | (tile.wire3() ? 4 : 0) | (tile.wire4() ? 8 : 0));
                        cell.Actuated = tile.actuator(); cell.IsActuated = tile.inActive();
                        cell.Tree = StructurePreviewRenderer.CaptureTree(cell, minX + x, minY + y);
                    }
                }
                cells[y * width + x] = cell;
            }
            string error;
            if (Save(new Structure { Name = "未命名结构", Width = width, Height = height, Cells = cells }, out error))
                Main.NewText("结构复制：已保存结构 " + width + "×" + height, 120, 255, 150);
            else Main.NewText("结构复制失败：" + error, 255, 160, 100);
        }

        private static bool IsContainer(int tileType)
        { return Main.tileContainer != null && tileType >= 0 && tileType < Main.tileContainer.Length && Main.tileContainer[tileType]; }
        private static bool HasIndependentEntity(int x, int y)
        {
            return TileEntity.ByPosition != null && TileEntity.ByPosition.ContainsKey(new Point16(x, y));
        }

        internal static bool Select(int index)
        {
            if (index < 0 || index >= Items.Count) return false;
            _selectedIndex = index; _missingPasteNotified = false; return true;
        }

        internal static bool BeginPasteAt(int x, int y)
        {
            EnsureInitialized();
            if (SelectedStructure == null)
            {
                if (!_missingPasteNotified)
                {
                    Main.NewText("结构粘贴：请先在 Ctrl+P 面板选择结构", 255, 180, 100);
                    _missingPasteNotified = true;
                }
                return false;
            }
            _previewX = x; _previewY = y; _pastePreview = true;
            return true;
        }

        internal static void UpdatePreview(int x, int y)
        { if (_pastePreview) { _previewX = x; _previewY = y; } }

        internal static void CancelPastePreview() { _pastePreview = false; }
        internal static void DrawPreview()
        {
            Structure structure = SelectedStructure;
            if (!_pastePreview || structure == null) return;
            Rectangle rect = WandsAndTools.GetTileWorldToScreenRect(_previewX, _previewY, _previewX + structure.Width - 1, _previewY + structure.Height - 1);
            UIRenderer.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, 35, 115, 180, 38);
            UIRenderer.DrawRectOutline(rect.X, rect.Y, rect.Width, rect.Height, 100, 220, 255, 235, 2);
            StructurePreviewRenderer.Draw(structure, new Vector2(rect.X, rect.Y), new Vector2(rect.Width / (structure.Width * 16f), rect.Height / (structure.Height * 16f)),
                Color.White * 0.62f, new Rectangle(0, 0, PanelLayout.ScreenWidth, PanelLayout.ScreenHeight));
        }

        internal static string SerializeSelected()
        {
            Structure structure = SelectedStructure;
            if (structure == null) return null;
            using (var raw = new MemoryStream())
            {
                using (var writer = new BinaryWriter(raw, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(-2); writer.Write(structure.Width); writer.Write(structure.Height);
                    foreach (Cell cell in structure.Cells) WriteCell(writer, cell);
                }
                raw.Position = 0;
                using (var compressed = new MemoryStream())
                {
                    using (var deflate = new DeflateStream(compressed, CompressionMode.Compress, true)) raw.CopyTo(deflate);
                    string payload = Convert.ToBase64String(compressed.ToArray());
                    return payload.Length <= 44000 ? payload : null;
                }
            }
        }

        internal static bool PasteSerialized(Player player, string payload, int x, int y, bool consumeMaterials, bool destroyTerrain, out int placed)
            => PasteSerialized(player, payload, x, y, consumeMaterials, destroyTerrain, out placed, out _, out _);

        internal static bool PasteSerialized(Player player, string payload, int x, int y, bool consumeMaterials, bool destroyTerrain, out int placed, out int width, out int height)
        {
            placed = 0; width = height = 0;
            Structure structure;
            if (!TryDeserialize(payload, out structure)) return false;
            width = structure.Width; height = structure.Height;
            return PasteStructure(player, structure, x, y, consumeMaterials, destroyTerrain, out placed);
        }

        private static bool TryDeserialize(string payload, out Structure structure)
        {
            structure = null;
            try
            {
                byte[] bytes = Convert.FromBase64String(payload ?? "");
                using (var input = new MemoryStream(bytes))
                using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                using (var raw = new MemoryStream())
                {
                    deflate.CopyTo(raw); raw.Position = 0;
                    using (var reader = new BinaryReader(raw))
                    {
                        int first = reader.ReadInt32();
                        bool extended = first == -2;
                        int width = extended ? reader.ReadInt32() : first, height = reader.ReadInt32();
                        if (width <= 0 || height <= 0 || (long)width * height > MaxTiles) return false;
                        var cells = new Cell[width * height];
                        for (int i = 0; i < cells.Length; i++) cells[i] = ReadCell(reader, extended);
                        if (reader.BaseStream.Position != reader.BaseStream.Length) return false;
                        structure = new Structure { Name = "联机结构", Width = width, Height = height, Cells = cells };
                        return IsValid(structure);
                    }
                }
            }
            catch { return false; }
        }

        private static void WriteCell(BinaryWriter writer, Cell cell)
        {
            writer.Write(cell.TileType); writer.Write(cell.WallType); writer.Write(cell.MaterialItemType); writer.Write(cell.WallMaterialItemType);
            writer.Write(cell.FrameX); writer.Write(cell.FrameY); writer.Write(cell.Active); writer.Write(cell.TileColor); writer.Write(cell.WallColor);
            writer.Write(cell.WireMask); writer.Write(cell.Actuated); writer.Write(cell.IsActuated); writer.Write(cell.Slope); writer.Write(cell.HalfBrick);
            writer.Write(cell.WallFrameX); writer.Write(cell.WallFrameY); writer.Write(cell.WallFrameNumber); writer.Write(cell.TileFrameNumber);
        }

        private static Cell ReadCell(BinaryReader reader, bool extended)
        {
            var cell = new Cell {
                TileType = reader.ReadInt32(), WallType = reader.ReadInt32(), MaterialItemType = reader.ReadInt32(), WallMaterialItemType = reader.ReadInt32(),
                FrameX = reader.ReadInt16(), FrameY = reader.ReadInt16(), Active = reader.ReadBoolean(), TileColor = reader.ReadByte(), WallColor = reader.ReadByte(),
                WireMask = reader.ReadByte(), Actuated = reader.ReadBoolean(), IsActuated = reader.ReadBoolean(), Slope = reader.ReadByte(), HalfBrick = reader.ReadBoolean()
            };
            if (extended) { cell.WallFrameX = reader.ReadInt32(); cell.WallFrameY = reader.ReadInt32(); cell.WallFrameNumber = reader.ReadByte(); cell.TileFrameNumber = reader.ReadByte(); }
            if (cell.TileType < 0 || cell.TileType >= TileID.Count || cell.WallType < 0 || cell.WallType >= WallID.Count ||
                cell.MaterialItemType < 0 || cell.MaterialItemType >= ItemID.Count || cell.WallMaterialItemType < 0 || cell.WallMaterialItemType >= ItemID.Count)
                throw new InvalidDataException("结构单元数据无效");
            return cell;
        }
        internal static bool PasteAt(Player player, int x, int y, bool consumeMaterials, bool destroyTerrain, out int placed)
        {
            placed = 0;
            Structure structure = SelectedStructure;
            if (structure == null || player == null) return false;
            if (Main.netMode == 1) { Main.NewText("结构粘贴：请通过服务器请求执行", 255, 180, 100); return false; }
            return PasteStructure(player, structure, x, y, consumeMaterials, destroyTerrain, out placed);
        }

        private static bool PasteStructure(Player player, Structure structure, int x, int y, bool consumeMaterials, bool destroyTerrain, out int placed)
        {
            placed = 0;
            if (structure == null || player == null) return false;
            var needed = RequiredMaterialItems(structure);
            var available = new Dictionary<int, int>();
            for (int i = 0; i < 59; i++) { Item item = player.inventory[i]; if (item != null && !item.IsAir) available[item.type] = available.ContainsKey(item.type) ? available[item.type] + item.stack : item.stack; }
            if (consumeMaterials && needed.Any(pair => !available.ContainsKey(Math.Abs(pair.Key)) || available[Math.Abs(pair.Key)] < pair.Value)) return false;
            var used = new Dictionary<int, int>();
            // 必须先清理完整目标范围，再恢复结构；逐格清理会连带破坏前一格刚写入的树或家具。
            if (destroyTerrain)
                for (int sx = 0; sx < structure.Width; sx++) for (int sy = 0; sy < structure.Height; sy++)
                {
                    int tx = x + sx, ty = y + sy;
                    if (tx < 10 || ty < 10 || tx >= Main.maxTilesX - 10 || ty >= Main.maxTilesY - 10) continue;
                    Tile tile = Main.tile[tx, ty];
                    if (tile == null) continue;
                    if (tile.active()) WorldGen.KillTile(tx, ty, false, false, false);
                    if (tile.wall != 0) WorldGen.KillWall(tx, ty, false);
                }
            for (int sx = 0; sx < structure.Width; sx++) for (int sy = 0; sy < structure.Height; sy++)
            {
                Cell cell = structure.Cells[sy * structure.Width + sx]; int tx = x + sx, ty = y + sy;
                if (tx < 10 || ty < 10 || tx >= Main.maxTilesX - 10 || ty >= Main.maxTilesY - 10) continue;
                Tile tile = Main.tile[tx, ty]; if (tile == null) { tile = new Tile(); Main.tile[tx, ty] = tile; }
                if (cell.Active && !tile.active())
                {
                    // 此处恢复已捕获的对象格，而不是把家具/树的每一格当成一个新的完整物品放置。
                    tile.type = (ushort)cell.TileType; tile.active(true);
                    tile.frameX = cell.FrameX; tile.frameY = cell.FrameY; tile.color(cell.TileColor);
                    tile.slope(cell.Slope); tile.halfBrick(cell.HalfBrick); tile.frameNumber(cell.TileFrameNumber);
                    tile.wire((cell.WireMask & 1) != 0); tile.wire2((cell.WireMask & 2) != 0);
                    tile.wire3((cell.WireMask & 4) != 0); tile.wire4((cell.WireMask & 8) != 0);
                    tile.actuator(cell.Actuated); tile.inActive(cell.IsActuated);
                    Add(used, cell.MaterialItemType > 0 ? cell.MaterialItemType : FindMaterialItem(cell.TileType, false), 1);
                    placed++;
                }
                if (cell.WallType > 0 && tile.wall == 0)
                {
                    tile.wall = (ushort)cell.WallType;
                    tile.wallFrameX(cell.WallFrameX); tile.wallFrameY(cell.WallFrameY); tile.wallFrameNumber(cell.WallFrameNumber);
                    tile.wallColor(cell.WallColor);
                    Add(used, cell.WallMaterialItemType > 0 ? cell.WallMaterialItemType : FindMaterialItem(cell.WallType, true), 1);
                }
            }
            if (consumeMaterials) foreach (var pair in used) Consume(player, pair.Key, pair.Value);
            _pastePreview = false; return true;
        }

        private static void Consume(Player player, int type, int amount)
        {
            for (int i = 0; i < 59 && amount > 0; i++) { Item item = player.inventory[i]; if (item == null || item.IsAir || item.type != type) continue; int used = Math.Min(amount, item.stack); item.stack -= used; amount -= used; if (item.stack <= 0) item.TurnToAir(); }
        }

        internal static Dictionary<int, int> CountTileMaterials(Structure structure)
        {
            var result = new Dictionary<int, int>();
            if (!IsValid(structure)) return result;
            foreach (Cell cell in structure.Cells)
            {
                if (cell == null) continue;
                if (cell.Active && cell.MaterialItemType > 0) Add(result, cell.MaterialItemType, 1);
                if (cell.WallType > 0 && cell.WallMaterialItemType > 0) Add(result, -cell.WallMaterialItemType, 1);
            }
            return result;
        }

        private static Dictionary<int, int> RequiredMaterialItems(Structure structure)
        {
            var result = new Dictionary<int, int>();
            if (!IsValid(structure)) return result;
            foreach (Cell cell in structure.Cells)
            {
                if (cell == null) continue;
                if (cell.Active) Add(result, FindMaterialItem(cell.TileType, false), 1);
                if (cell.WallType > 0) Add(result, FindMaterialItem(cell.WallType, true), 1);
            }
            return result.Where(pair => pair.Key > 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        internal static string FormatMaterialSummary(Structure structure)
        {
            Dictionary<int, int> materials = CountTileMaterials(structure);
            return materials.Count == 0 ? "不需要方块或墙材料" : string.Join("、", materials.OrderBy(p => p.Key).Select(p => (p.Key > 0 ? "方块" : "墙") + Math.Abs(p.Key) + "×" + p.Value));
        }

        private static int FindMaterialItem(int type, bool wall)
        {
            string key = (wall ? "w" : "t") + type;
            int cached;
            if (MaterialItems.TryGetValue(key, out cached)) return cached;
            for (int itemType = 1; itemType < ItemID.Count; itemType++)
            {
                var item = new Item(); item.SetDefaults(itemType);
                if (item.maxStack <= 1) continue;
                if ((!wall && item.createTile == type) || (wall && item.createWall == type)) { MaterialItems[key] = itemType; return itemType; }
            }
            MaterialItems[key] = 0;
            return 0;
        }

        private static void Add(Dictionary<int, int> result, int key, int amount)
        { result[key] = result.ContainsKey(key) ? result[key] + amount : amount; }

        private static bool SaveFile(out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(_path)) { error = "结构存储尚未初始化"; return false; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var serializer = new DataContractJsonSerializer(typeof(FileModel));
                string temp = _path + ".tmp";
                using (var stream = File.Create(temp)) serializer.WriteObject(stream, new FileModel { Version = FormatVersion, Structures = Items.Select(Clone).ToList() });
                if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak"); else File.Move(temp, _path);
                return true;
            }
            catch (Exception exception) { error = "结构存储保存失败"; Mod.LogError(error, exception); return false; }
        }

        private static FileModel Read(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(FileModel));
            using (var stream = File.OpenRead(path)) return (FileModel)serializer.ReadObject(stream);
        }

        private static bool IsValid(Structure value)
        { return value != null && value.Width > 0 && value.Height > 0 && (long)value.Width * value.Height <= MaxTiles && value.Cells != null && value.Cells.Length == value.Width * value.Height && value.Cells.All(cell => cell != null); }

        private static Structure Clone(Structure value)
        {
            return new Structure { Name = value.Name, Width = value.Width, Height = value.Height, Cells = value.Cells.Select(cell => new Cell { Active = cell.Active, TileType = cell.TileType, MaterialItemType = cell.MaterialItemType, WallType = cell.WallType, WallMaterialItemType = cell.WallMaterialItemType, WallFrameX = cell.WallFrameX, WallFrameY = cell.WallFrameY, WallFrameNumber = cell.WallFrameNumber, TileFrameNumber = cell.TileFrameNumber, Tree = cell.Tree, FrameX = cell.FrameX, FrameY = cell.FrameY, TileColor = cell.TileColor, WallColor = cell.WallColor, WireMask = cell.WireMask, Actuated = cell.Actuated, IsActuated = cell.IsActuated, Slope = cell.Slope, HalfBrick = cell.HalfBrick }).ToArray() };
        }

        private static string NormalizeName(string value, string fallback)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0) value = fallback;
            if (value == null || value.Length > 32 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            return value;
        }

        [DataContract]
        internal sealed class FileModel { [DataMember(Order = 0)] internal int Version; [DataMember(Order = 1)] internal List<Structure> Structures; }
        [DataContract]
        internal sealed class Structure
        {
            [DataMember(Order = 0)] internal string Name;
            [DataMember(Order = 1)] internal int Width;
            [DataMember(Order = 2)] internal int Height;
            [DataMember(Order = 3)] internal Cell[] Cells;
        }
        [DataContract]
        internal sealed class Cell
        {
            [DataMember(Order = 0)] internal int TileType;
            [DataMember(Order = 1)] internal int WallType;
            [DataMember(Order = 12)] internal int MaterialItemType;
            [DataMember(Order = 13)] internal int WallMaterialItemType;
            [DataMember(Order = 2)] internal short FrameX;
            [DataMember(Order = 3)] internal short FrameY;
            [DataMember(Order = 4)] internal bool Active;
            [DataMember(Order = 5)] internal byte TileColor;
            [DataMember(Order = 6)] internal byte WallColor;
            [DataMember(Order = 7)] internal byte WireMask;
            [DataMember(Order = 8)] internal bool Actuated;
            [DataMember(Order = 9)] internal bool IsActuated;
            [DataMember(Order = 10)] internal byte Slope;
            [DataMember(Order = 11)] internal bool HalfBrick;
            [DataMember(Order = 14)] internal int WallFrameX;
            [DataMember(Order = 15)] internal int WallFrameY;
            [DataMember(Order = 16)] internal byte WallFrameNumber;
            [DataMember(Order = 17)] internal byte TileFrameNumber;
            [DataMember(Order = 18, EmitDefaultValue = false)] internal StructurePreviewRenderer.TreeVisual Tree;
        }
    }
}
