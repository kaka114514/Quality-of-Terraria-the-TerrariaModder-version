using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Tile_Entities;
using TerrariaModder.Core.Net;

namespace QualityOfTerraria.Networking
{
    internal static class PylonSync
    {
        private const string Operation = "quality-of-terraria.pylons.v1";
        private static readonly List<TeleportPylonInfo> ClientPylons = new List<TeleportPylonInfo>();
        private static bool _initialized, _ready;
        private static ulong _lastRequest;
        private static Action<int, string, string> _requestHandler;
        private static Action<bool, string, string> _responseHandler;

        internal static bool Ready => Main.netMode != 1 || _ready;
        internal static IReadOnlyList<TeleportPylonInfo> Pylons => ClientPylons;

        internal static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _ready = Main.netMode != 1;
            _requestHandler = (client, operation, payload) =>
            {
                if (operation != Operation || Main.netMode != 2) return;
                NetworkRuntime.Enqueue(() => SendServerList(client, payload));
            };
            _responseHandler = (success, operation, payload) =>
            {
                if (operation != Operation || Main.netMode != 1) return;
                NetworkRuntime.Enqueue(() => ReceiveClientList(success, payload));
            };
            NetSync.OnStorageRequest += _requestHandler;
            NetSync.OnStorageResponse += _responseHandler;
        }

        internal static void ResetSession()
        {
            if (_initialized)
            {
                NetSync.OnStorageRequest -= _requestHandler;
                NetSync.OnStorageResponse -= _responseHandler;
            }
            _initialized = false;
            _ready = false;
            _lastRequest = 0;
            ClientPylons.Clear();
        }

        internal static void Update()
        {
            if (!_initialized || Main.netMode != 1 || !Mod.WorldActive) return;
            if (_ready && ClientPylons.Count == TileEntity.ByPosition.Values.Count(entity => entity is TETeleportationPylon)) return;
            if (Main.GameUpdateCount - _lastRequest < 120) return;
            _lastRequest = Main.GameUpdateCount;
            NetSyncPayload.SendRequest(Operation, "request");
        }

        internal static void ReplaceClientPylons(List<TeleportPylonInfo> destination, IEnumerable<TeleportPylonInfo> pylons)
        {
            destination.Clear();
            destination.AddRange(pylons);
        }

        private static void SendServerList(int client, string payload)
        {
            if (payload != "request" || client < 0 || client >= Main.maxPlayers || !Main.player[client].active) return;
            NetSyncPayload.SendResponseTo(client, true, Operation, EncodePylons());
        }

        private static string EncodePylons()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                var pylons = new List<TeleportPylonInfo>();
                foreach (TileEntity entity in TileEntity.ByPosition.Values)
                {
                    var pylon = entity as TETeleportationPylon;
                    TeleportPylonType type;
                    if (pylon == null || !pylon.TryGetPylonType(out type)) continue;
                    pylons.Add(new TeleportPylonInfo { PositionInTiles = pylon.Position, TypeOfPylon = type });
                }
                writer.Write(pylons.Count);
                foreach (var pylon in pylons)
                {
                    writer.Write((short)pylon.PositionInTiles.X);
                    writer.Write((short)pylon.PositionInTiles.Y);
                    writer.Write((int)pylon.TypeOfPylon);
                }
                return Convert.ToBase64String(stream.ToArray());
            }
        }

        private static void ReceiveClientList(bool success, string payload)
        {
            if (!success || payload == null) return;
            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                using (var reader = new BinaryReader(new MemoryStream(bytes)))
                {
                    int count = reader.ReadInt32();
                    if (count < 0 || count > Main.maxChests) throw new InvalidDataException("服务器晶塔列表数量无效");
                    var received = new List<TeleportPylonInfo>(count);
                    for (int i = 0; i < count; i++)
                    {
                        short x = reader.ReadInt16(), y = reader.ReadInt16();
                        int type = reader.ReadInt32();
                        if (x < 0 || y < 0 || type < 0 || type > 32) throw new InvalidDataException("服务器晶塔列表内容无效");
                        received.Add(new TeleportPylonInfo { PositionInTiles = new Point16(x, y), TypeOfPylon = (TeleportPylonType)type });
                    }
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("服务器晶塔列表含多余数据");
                    ReplaceClientPylons(ClientPylons, received);
                }
                _ready = true;
                Main.refreshMap = true;
            }
            catch (Exception error) { Mod.LogError("读取服务器晶塔列表失败", error); }
        }
    }
}
