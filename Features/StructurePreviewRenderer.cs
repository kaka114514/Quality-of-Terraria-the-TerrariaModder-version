using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;

namespace QualityOfTerraria.Features
{
    internal static class StructurePreviewRenderer
    {
        internal struct Part
        {
            internal int Kind, Texture;
            internal Rectangle Source, Bounds;
        }

        internal sealed class Preview
        {
            internal readonly List<Part> Parts = new List<Part>();
            internal Rectangle Bounds;
        }

        [DataContract]
        internal sealed class TreeVisual
        {
            [DataMember] internal int Style;
            [DataMember] internal int Frame;
            [DataMember] internal int Width = 80;
            [DataMember] internal int Height = 80;
            [DataMember] internal int TrunkX;
            [DataMember] internal int TrunkY;
        }

        private static readonly ConditionalWeakTable<StructureClipboard.Structure, Preview> Cache = new ConditionalWeakTable<StructureClipboard.Structure, Preview>();

        internal static bool IsTree(int type)
            => type == 5 || type == 323 || (type >= 583 && type <= 589) || type == 596 || type == 616 || type == 634;

        private static TreeVisual DefaultTree(StructureClipboard.Cell cell)
        {
            var visual = new TreeVisual { TrunkX = cell.FrameX, TrunkY = cell.FrameY, Frame = cell.FrameY == 220 ? 1 : cell.FrameY == 242 ? 2 : 0 };
            if (cell.TileType >= 583 && cell.TileType <= 589) { visual.Style = 22 + cell.TileType - 583; visual.Width = 116; visual.Height = 96; }
            else if (cell.TileType == 596 || cell.TileType == 616) { visual.Style = cell.TileType == 596 ? 29 : 30; visual.Width = 118; visual.Height = 96; }
            else if (cell.TileType == 634) { visual.Style = 31; visual.Width = 116; visual.Height = 96; }
            else if (cell.TileType == 323) { visual.Style = 15; visual.Frame = (cell.FrameX - 88) / 22; visual.TrunkY = 0; }
            return visual;
        }

        internal static TreeVisual CaptureTree(StructureClipboard.Cell cell, int x, int y)
        {
            if (!cell.Active || !IsTree(cell.TileType)) return null;
            TreeVisual visual = DefaultTree(cell);
            int offset = 0;
            if (cell.TileType != 323)
            {
                if (cell.FrameY >= 198) offset = cell.FrameX == 44 ? 1 : cell.FrameX == 66 ? -1 : 0;
                else if (cell.FrameY >= 132) offset = cell.FrameX == 22 ? -1 : cell.FrameX == 44 ? 1 : 0;
                else if (cell.FrameX == 66 && cell.FrameY <= 45) offset = 1;
                else if (cell.FrameX == 88 && cell.FrameY >= 66 && cell.FrameY <= 110) offset = -1;
            }
            int groundX = x + offset, groundY = y;
            if (Main.tile == null || groundX < 0 || groundX >= Main.maxTilesX || y + 100 >= Main.maxTilesY) return visual;
            while (groundY < Main.maxTilesY - 1 && Main.tile[groundX, groundY] != null && Main.tile[groundX, groundY].active() && Main.tile[groundX, groundY].type == cell.TileType) groundY++;
            if (Main.tile[groundX, groundY] == null) return visual;
            if (cell.TileType == 5) visual.TrunkX += 176 * (TileDrawing.GetTreeVariant(groundX, groundY) + 1);
            if (cell.TileType == 323)
            {
                int groundType = Main.tile[groundX, groundY].type;
                int biome = groundType == 234 ? 1 : groundType == 116 ? 2 : groundType == 112 ? 3 : 0;
                if (WorldGen.IsPalmOasisTree(groundX)) biome += 4;
                visual.TrunkY = biome * 22;
                if (biome >= 4) { visual.Style = 21; visual.Width = 114; visual.Height = 98; }
                return visual;
            }
            WorldGen.GetTreeFoliageDataMethod foliage = WorldGen.GetCommonTreeFoliageData;
            if (cell.TileType >= 583 && cell.TileType <= 589) foliage = WorldGen.GetGemTreeFoliageData;
            else if (cell.TileType == 596 || cell.TileType == 616) foliage = WorldGen.GetVanityTreeFoliageData;
            else if (cell.TileType == 634) foliage = WorldGen.GetAshTreeFoliageData;
            int frame = visual.Frame, style = visual.Style, width, height, floor;
            if (foliage(x, y, offset, ref frame, ref style, out floor, out width, out height))
            { visual.Frame = frame; visual.Style = style; visual.Width = width; visual.Height = height; }
            return visual;
        }

        internal static Preview GetPreview(StructureClipboard.Structure structure) => Cache.GetValue(structure, Build);

        private static Preview Build(StructureClipboard.Structure structure)
        {
            var result = new Preview { Bounds = new Rectangle(0, 0, structure.Width * 16, structure.Height * 16) };
            // 所有墙先画完，再画前景，最后画超出单格范围的树叶，避免邻墙覆盖方块。
            for (int layer = 0; layer < 3; layer++)
                for (int y = 0; y < structure.Height; y++) for (int x = 0; x < structure.Width; x++)
                {
                    StructureClipboard.Cell cell = structure.Cells[y * structure.Width + x];
                    if (cell == null) continue;
                    Part part;
                    if (layer == 0)
                    {
                        if (cell.WallType <= 0) continue;
                        part = new Part { Kind = 0, Texture = cell.WallType, Source = new Rectangle(cell.WallFrameX, cell.WallFrameY, 32, 32), Bounds = new Rectangle(x * 16 - 8, y * 16 - 8, 32, 32) };
                    }
                    else
                    {
                        if (!cell.Active) continue;
                        bool tree = IsTree(cell.TileType);
                        bool foliage = tree && (cell.TileType == 323 ? cell.FrameX >= 88 && cell.FrameX <= 132 : cell.FrameY >= 198 && (cell.FrameX == 22 || cell.FrameX == 44 || cell.FrameX == 66));
                        TreeVisual visual = tree ? cell.Tree ?? DefaultTree(cell) : null;
                        if (layer == 2)
                        {
                            if (!foliage) continue;
                            if (cell.TileType == 323)
                            {
                                int biome = visual.TrunkY / 22;
                                part = new Part { Kind = 2, Texture = visual.Style, Source = new Rectangle(visual.Frame * (visual.Width + 2), biome >= 4 ? (biome - 4) * 98 : biome * 82, visual.Width, visual.Height), Bounds = new Rectangle(x * 16 + cell.FrameY - (biome >= 4 ? 48 : 32), y * 16 + 16 + (biome >= 4 ? 2 : 0) - visual.Height, visual.Width, visual.Height) };
                            }
                            else if (cell.FrameX == 22)
                                part = new Part { Kind = 2, Texture = visual.Style, Source = new Rectangle(visual.Frame * (visual.Width + 2), 0, visual.Width, visual.Height), Bounds = new Rectangle(x * 16 + 8 - visual.Width / 2, y * 16 + 16 - visual.Height, visual.Width, visual.Height) };
                            else
                                part = new Part { Kind = 3, Texture = visual.Style, Source = new Rectangle(cell.FrameX == 44 ? 0 : 42, visual.Frame * 42, 40, 40), Bounds = new Rectangle(x * 16 + (cell.FrameX == 44 ? -24 : 0), y * 16 - 12, 40, 40) };
                        }
                        else
                        {
                            if (foliage) continue;
                            int size = tree ? 20 : 16;
                            part = new Part { Kind = 1, Texture = cell.TileType, Source = new Rectangle(tree ? visual.TrunkX : cell.FrameX, tree ? visual.TrunkY : cell.FrameY, size, size), Bounds = new Rectangle(x * 16 - (tree ? 2 : 0), y * 16, size, size) };
                            if (cell.HalfBrick) { part.Source.Y += 8; part.Source.Height = 8; part.Bounds.Y += 8; part.Bounds.Height = 8; }
                        }
                    }
                    result.Parts.Add(part);
                    result.Bounds = Rectangle.Union(result.Bounds, part.Bounds);
                }
            return result;
        }

        internal static void Draw(StructureClipboard.Structure structure, Vector2 origin, Vector2 scale, Color color, Rectangle viewport)
        {
            if (Main.spriteBatch == null || structure == null) return;
            foreach (Part part in GetPreview(structure).Parts)
            {
                int left = (int)Math.Round(origin.X + part.Bounds.Left * scale.X), top = (int)Math.Round(origin.Y + part.Bounds.Top * scale.Y);
                int right = (int)Math.Round(origin.X + part.Bounds.Right * scale.X), bottom = (int)Math.Round(origin.Y + part.Bounds.Bottom * scale.Y);
                var destination = new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
                if (!viewport.Intersects(destination)) continue;
                Texture2D texture = GetTexture(part);
                if (texture == null) continue;
                Rectangle source = part.Source;
                if (part.Kind == 0 && Main.wallFrame != null && part.Texture < Main.wallFrame.Length) source.Y += Main.wallFrame[part.Texture] * 180;
                source = Rectangle.Intersect(source, new Rectangle(0, 0, texture.Width, texture.Height));
                if (source.Width == 0 || source.Height == 0) continue;
                Main.spriteBatch.Draw(texture, destination, source, color);
            }
        }

        private static Texture2D GetTexture(Part part)
        {
            Asset<Texture2D>[] assets = part.Kind == 0 ? TextureAssets.Wall : part.Kind == 1 ? TextureAssets.Tile : part.Kind == 2 ? TextureAssets.TreeTop : TextureAssets.TreeBranch;
            if (assets == null || part.Texture < 0 || part.Texture >= assets.Length || assets[part.Texture] == null) return null;
            Asset<Texture2D> asset = assets[part.Texture];
            if (!asset.IsLoaded && Main.Assets != null) Main.Assets.Request<Texture2D>(asset.Name, AssetRequestMode.ImmediateLoad);
            return asset.Value;
        }
    }
}
