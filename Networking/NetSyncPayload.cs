using System;
using System.Collections.Generic;
using System.Text;
using TerrariaModder.Core.Net;

namespace QualityOfTerraria.Networking
{
    // Core NetSync 的字符串字段只有一个字节长度前缀；长 Base64 必须在模组层分片。
    internal static class NetSyncPayload
    {
        private const string Prefix = "~qot1|";
        private const int MaxWireBytes = 255;
        private const int ChunkChars = 180;
        private const int MaxChunks = 256;
        private const int MaxPayloadChars = ChunkChars * MaxChunks;
        private sealed class Partial
        {
            internal readonly string[] Parts;
            internal int Count;
            internal Partial(int total) { Parts = new string[total]; }
        }

        private static readonly Dictionary<string, Partial> Requests = new Dictionary<string, Partial>();
        private static readonly Dictionary<string, Partial> Responses = new Dictionary<string, Partial>();
        private static long _nextId;

        internal static void SendRequest(string operation, string payload)
            => Send(payload, part => NetSync.SendStorageRequest(operation, part));

        internal static void SendResponseTo(int client, bool success, string operation, string payload)
            => Send(payload, part => NetSync.SendStorageResponseTo(client, success, operation, part));

        private static void Send(string payload, Action<string> send)
        {
            payload = payload ?? "";
            if (System.Text.Encoding.UTF8.GetByteCount(payload) <= MaxWireBytes)
            {
                send(payload);
                return;
            }
            if (payload.Length > MaxPayloadChars) throw new ArgumentException("网络载荷过大");
            string id = (++_nextId).ToString("x") + Guid.NewGuid().ToString("N").Substring(0, 8);
            int total = (payload.Length + ChunkChars - 1) / ChunkChars;
            if (total > MaxChunks) throw new ArgumentException("网络载荷分片过多");
            for (int index = 0; index < total; index++)
            {
                int offset = index * ChunkChars;
                int length = Math.Min(ChunkChars, payload.Length - offset);
                string part = Prefix + id + "|" + index + "|" + total + "|" + payload.Substring(offset, length);
                if (System.Text.Encoding.UTF8.GetByteCount(part) > MaxWireBytes) throw new InvalidOperationException("网络分片超出 NetSync 限制");
                send(part);
            }
        }

        internal static bool TryAcceptRequest(int client, string operation, string payload, out string complete)
            => TryAccept(Requests, "r|" + client + "|" + operation, payload, out complete);

        internal static bool TryAcceptResponse(string operation, string payload, out string complete)
            => TryAccept(Responses, "s|" + operation, payload, out complete);

        private static bool TryAccept(Dictionary<string, Partial> store, string keyPrefix, string payload, out string complete)
        {
            complete = payload;
            if (payload == null || !payload.StartsWith(Prefix, StringComparison.Ordinal)) return true;
            string[] fields = payload.Split(new[] { '|' }, 5);
            int index, total;
            if (fields.Length != 5 || fields[0] != "~qot1" || fields[1].Length < 2 || fields[4].Length > ChunkChars ||
                !int.TryParse(fields[2], out index) || !int.TryParse(fields[3], out total) ||
                total < 1 || total > MaxChunks || index < 0 || index >= total)
                return false;
            string key = keyPrefix + "|" + fields[1];
            Partial partial;
            if (!store.TryGetValue(key, out partial) || partial.Parts.Length != total)
            {
                partial = new Partial(total);
                store[key] = partial;
            }
            if (partial.Parts[index] == null)
            {
                partial.Parts[index] = fields[4];
                partial.Count++;
            }
            if (partial.Count != total) return false;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < total; i++) builder.Append(partial.Parts[i]);
            store.Remove(key);
            complete = builder.ToString();
            return true;
        }

        internal static void Reset()
        {
            Requests.Clear();
            Responses.Clear();
            _nextId = 0;
        }
    }
}
