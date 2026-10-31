using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    /// <summary>
    /// 掉落。规则很简单：怪死的一瞬间按 drops.csv 掷一次骰子，
    /// 掷中就把装备 id 记进本场战果。重复掉同一件不合并——交给背包层去堆叠。
    /// </summary>
    public static class Loot
    {
        static Dictionary<string, List<LootEntry>> cache;

        static Dictionary<string, List<LootEntry>> Table
        {
            get
            {
                if (cache == null)
                {
                    cache = new Dictionary<string, List<LootEntry>>();
                    var all = Resources.LoadAll<LootEntry>("BattleData/Drops");
                    for (int i = 0; i < all.Length; i++)
                    {
                        var e = all[i];
                        if (e == null || string.IsNullOrEmpty(e.enemyId)) continue;
                        List<LootEntry> list;
                        if (!cache.TryGetValue(e.enemyId, out list))
                        {
                            list = new List<LootEntry>();
                            cache[e.enemyId] = list;
                        }
                        list.Add(e);
                    }
                }
                return cache;
            }
        }

        public static void ClearCache() { cache = null; }

        /// <summary>掷一次。命中就往 into 里加装备 id。</summary>
        public static void Roll(string enemyId, System.Random rng, List<string> into)
        {
            if (string.IsNullOrEmpty(enemyId) || into == null) return;
            List<LootEntry> list;
            if (!Table.TryGetValue(enemyId, out list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e == null || string.IsNullOrEmpty(e.itemId)) continue;
                if (rng == null) { if (Random.value <= e.chance) into.Add(e.itemId); continue; }
                if (rng.NextDouble() <= e.chance) into.Add(e.itemId);
            }
        }
    }
}
