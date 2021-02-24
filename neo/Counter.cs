using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Neo
{
    public static class Counter
    {
        private static ConcurrentDictionary<string, (int, int)> dict = new ConcurrentDictionary<string, (int, int)>();

        public static void Reset()
        {
            var info = Info();
            if (0 < info.Length) Logger.Write(nameof(Counter), Info());
            dict.Clear();
        }

        public static void Increase(string name, int count = 1)
        {
            if (dict.TryGetValue(name, out var t))
            {
                dict[name] = (t.Item1++, t.Item2 + count);
                return;
            }
            dict[name] = (1, count);
        }

        private static string Info()
        {
            string str = "";
            foreach (var t in dict)
            {
                str += $" {t.Key}:({t.Value.Item1}, {t.Value.Item2}),";
            }
            return str;
        }
    }
}
