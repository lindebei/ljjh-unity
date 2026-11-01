using System.Collections.Generic;
using UnityEngine;
using LingJing.Battle;

namespace LingJing.Data
{
    /// <summary>每只玩家兽上阵的 4 个技能（不含普攻；普攻永远在技能栏最左，不占格）。
    /// 存 PlayerPrefs，格式：beastId|slot|skillId，和 EquipmentPresets 同一套路。</summary>
    public static class SkillLoadout
    {
        const string Key = "ljjh.loadout.v1";
        const int Slots = 4;

        static Dictionary<string, string[]> cache;

        static void EnsureCache()
        {
            if (cache != null) return;
            cache = new Dictionary<string, string[]>();
            string raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var line in raw.Split('\n'))
            {
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split('|');
                if (parts.Length != 3) continue;
                string beastId = parts[0];
                int slot;
                if (!int.TryParse(parts[1], out slot)) continue;
                if (slot < 0 || slot >= Slots) continue;
                if (!cache.ContainsKey(beastId)) cache[beastId] = new string[Slots];
                cache[beastId][slot] = parts[2];
            }
        }

        public static void Save()
        {
            EnsureCache();
            var sb = new System.Text.StringBuilder();
            foreach (var kv in cache)
                for (int i = 0; i < Slots; i++)
                {
                    if (string.IsNullOrEmpty(kv.Value[i])) continue;
                    sb.Append(kv.Key).Append('|').Append(i).Append('|').Append(kv.Value[i]).Append('\n');
                }
            PlayerPrefs.SetString(Key, sb.ToString());
            PlayerPrefs.Save();
        }

        public static string[] Get(string beastId)
        {
            EnsureCache();
            if (cache.ContainsKey(beastId)) return (string[])cache[beastId].Clone();
            return new string[Slots];
        }

        public static void Set(string beastId, int slot, string skillId)
        {
            EnsureCache();
            if (!cache.ContainsKey(beastId)) cache[beastId] = new string[Slots];
            if (slot < 0 || slot >= Slots) return;
            cache[beastId][slot] = skillId;
        }

        public static void ResetAll()
        {
            cache = new Dictionary<string, string[]>();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        public static bool IsEmpty(string beastId)
        {
            var ids = Get(beastId);
            for (int i = 0; i < Slots; i++) if (!string.IsNullOrEmpty(ids[i])) return false;
            return true;
        }

        /// <summary>给这只兽自动装满 4 格：按 unlockLevel 取已解锁里最靠前的 4 个（跳过普攻）。</summary>
        public static void AutoFill(CreatureDefinition def, int level)
        {
            if (def == null || def.skillPool == null) return;
            int slot = 0;
            foreach (var sk in def.skillPool)
            {
                if (slot >= Slots) break;
                if (sk == null || sk.tier <= 1) continue;
                if (sk.unlockLevel > level) continue;
                Set(def.id, slot, sk.id);
                slot++;
            }
            Save();
        }

        /// <summary>上阵技能（已解析成 SO，缺的格子返回 null）。</summary>
        public static SkillDefinition[] Resolve(CreatureDefinition def, int level)
        {
            var ids = Get(def.id);
            var list = new SkillDefinition[Slots];
            bool any = false;
            for (int i = 0; i < Slots; i++)
            {
                if (string.IsNullOrEmpty(ids[i])) continue;
                var sk = BattleDatabase.GetSkill(ids[i]);
                if (sk != null && sk.unlockLevel <= level) { list[i] = sk; any = true; }
            }
            if (!any)
            {
                AutoFill(def, level);   // 一格都没配：按等级自动装满
                ids = Get(def.id);      // 自动装满后要重新读，否则下面拿到的还是空数组（踩过）
            }
            for (int i = 0; i < Slots; i++)
            {
                if (list[i] != null) continue;
                if (string.IsNullOrEmpty(ids[i])) continue;
                var sk = BattleDatabase.GetSkill(ids[i]);
                if (sk != null && sk.unlockLevel <= level) list[i] = sk;
            }
            return list;
        }
    }
}
