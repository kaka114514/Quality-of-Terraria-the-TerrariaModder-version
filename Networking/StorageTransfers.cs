using System;
using System.Collections.Generic;
using Terraria;

namespace QualityOfTerraria.Networking
{
    internal static class StorageTransfers
    {
        internal static int Move(Item source, IEnumerable<Item[]> destinations, bool stackOnly = false, int amount = int.MaxValue)
        {
            if (source == null || source.IsAir || amount <= 0) return 0;
            int remaining = Math.Min(source.stack, amount), moved = 0;
            // 先补足同类型、同前缀的堆叠，最后才使用空格。
            for (int pass = 0; pass < (stackOnly ? 1 : 2) && remaining > 0; pass++)
                foreach (Item[] slots in destinations)
                {
                    for (int i = 0; i < slots.Length && remaining > 0; i++)
                    {
                        Item target = slots[i];
                        if (ReferenceEquals(source, target)) continue;
                        bool empty = target == null || target.IsAir;
                        if (pass == 0 && (empty || target.type != source.type || target.prefix != source.prefix)) continue;
                        if (pass == 1 && !empty) continue;
                        int capacity = empty ? source.maxStack : target.maxStack - target.stack;
                        int count = Math.Min(remaining, Math.Max(0, capacity));
                        if (count == 0) continue;
                        if (empty) { target = source.Clone(); target.stack = 0; slots[i] = target; }
                        target.stack += count;
                        source.stack -= count;
                        remaining -= count;
                        moved += count;
                    }
                    if (remaining == 0) break;
                }
            if (source.stack <= 0) source.TurnToAir();
            return moved;
        }

        internal static void Merge(IEnumerable<Item[]> boxes)
        {
            var partial = new Dictionary<long, Queue<Item>>();
            foreach (Item[] slots in boxes)
                foreach (Item item in slots)
                {
                    if (item == null || item.IsAir) continue;
                    long key = ((long)item.type << 8) | item.prefix;
                    Queue<Item> targets;
                    if (!partial.TryGetValue(key, out targets)) { targets = new Queue<Item>(); partial.Add(key, targets); }
                    while (item.stack > 0 && targets.Count > 0)
                    {
                        Item target = targets.Peek();
                        int count = Math.Min(item.stack, target.maxStack - target.stack);
                        target.stack += count;
                        item.stack -= count;
                        if (target.stack >= target.maxStack) targets.Dequeue();
                    }
                    if (item.stack == 0) item.TurnToAir();
                    else if (item.stack < item.maxStack) targets.Enqueue(item);
                }
        }
    }
}
