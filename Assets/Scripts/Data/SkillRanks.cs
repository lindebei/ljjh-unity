// 技能等级（I/II/III）：每只兽 8 个技能池里的技能各自有等级，用技能点升级。
// 只加数值不改机制：每级 +15% 倍率；到 III 再额外 −1 蓝耗、−1 冷却（最低 1）。
// 技能点：每 4 级 1 点（24 级 = 6 点），一境内不可跨兽挪用。
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LingJing.Data
{
    public static class SkillRanks
    {
        const string Key = "ljjh.skillrank.v1";

        static Dictionary<string, int> cache;

        /// <summary>测试/批测用：>0 时忽略存档，所有技能按这个等级算。</summary>
        public static int forcedRank;

        static Dictionary<string, int> Map
        {
            get
            {
                if (cache != null) return cache;
                cache = new Dictionary<string, int>();
                string raw = PlayerPrefs.GetString(Key, string.Empty);
                foreach (var line in raw.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;
                    var parts = t.Split('|');
                    if (parts.Length < 2) continue;
                    int r;
                    if (int.TryParse(parts[1], out r) && r >= 1 && r <= 3) cache[parts[0]] = r;
                }
                return cache;
            }
        }

        public static int Get(string skillId)
        {
            if (forcedRank > 0) return Mathf.Clamp(forcedRank, 1, 3);
            int r;
            return Map.TryGetValue(skillId, out r) ? r : 1;
        }

        public static void Set(string skillId, int rank)
        {
            Map[skillId] = Mathf.Clamp(rank, 1, 3);
            Save();
        }

        public static void Save()
        {
            var sb = new StringBuilder();
            foreach (var kv in Map) sb.Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }

        public static void ResetAll()
        {
            cache = new Dictionary<string, int>();
            PlayerPrefs.SetString(Key, string.Empty);
            PlayerPrefs.Save();
        }

        /// <summary>一境总共 6 点：每 4 级 1 点。</summary>
        public static int TotalPoints(int level) { return Mathf.Clamp(level / 4, 0, 6); }

        /// <summary>这只兽已经花掉的点数（每个技能的 rank−1 加起来）。</summary>
        public static int SpentFor(CreatureDefinition def)
        {
            if (def == null || def.skillPool == null) return 0;
            int spent = 0;
            for (int i = 0; i < def.skillPool.Length; i++)
            {
                var sk = def.skillPool[i];
                if (sk == null) continue;
                spent += Get(sk.id) - 1;
            }
            return spent;
        }

        public static int Remaining(CreatureDefinition def, int level)
        {
            return Mathf.Max(0, TotalPoints(level) - SpentFor(def));
        }

        public static bool CanUpgrade(CreatureDefinition def, SkillDefinition sk, int level)
        {
            if (def == null || sk == null) return false;
            if (Get(sk.id) >= Mathf.Max(1, sk.maxRank)) return false;
            if (level < sk.unlockLevel) return false;
            return Remaining(def, level) > 0;
        }

        /// <summary>倍率系数：I=1.00 / II=1.15 / III=1.30。</summary>
        public static float MultiplierMul(SkillDefinition sk)
        {
            if (sk == null) return 1f;
            return 1f + 0.15f * (Get(sk.id) - 1);
        }

        /// <summary>有效蓝耗：普攻（负数）原样返回；满级 −1，最少 1。</summary>
        public static int Cost(SkillDefinition sk)
        {
            if (sk == null) return 0;
            if (sk.spiritCost < 0) return sk.spiritCost;
            int lv = Get(sk.id);
            return Mathf.Max(1, sk.spiritCost - (lv >= 3 ? 1 : 0));
        }

        /// <summary>有效冷却：满级 −1，最少 1。</summary>
        public static int Cooldown(SkillDefinition sk)
        {
            if (sk == null || sk.cooldown <= 0) return 0;
            int lv = Get(sk.id);
            return Mathf.Max(1, sk.cooldown - (lv >= 3 ? 1 : 0));
        }

        public static string RankLabel(int rank)
        {
            return rank >= 3 ? "III" : (rank == 2 ? "II" : "I");
        }
    }
}