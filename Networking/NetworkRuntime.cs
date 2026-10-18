using System;
using System.Collections.Generic;

namespace QualityOfTerraria.Networking
{
    internal static class NetworkRuntime
    {
        private static readonly object Gate = new object();
        private static readonly Queue<Tuple<int, Action>> Pending = new Queue<Tuple<int, Action>>();
        private static int _epoch;

        internal static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (Gate)
                if (Pending.Count < 512) Pending.Enqueue(Tuple.Create(_epoch, action));
        }

        internal static void Update()
        {
            for (int i = 0; i < 64; i++)
            {
                Tuple<int, Action> next;
                lock (Gate)
                {
                    if (Pending.Count == 0) return;
                    next = Pending.Dequeue();
                    if (next.Item1 != _epoch) continue;
                }
                next.Item2();
            }
        }

        internal static void ResetSession()
        {
            lock (Gate) { _epoch++; Pending.Clear(); }
        }
    }
}
