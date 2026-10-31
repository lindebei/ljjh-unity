using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Data
{
    /// <summary>
    /// 三只出战友的装备穿戴情况（跨场景共享）。战斗场景只读，换装界面只写。
    /// 现在没有背包概念：所有已拥有的装备都能直接穿，所以这里只存「谁穿了什么」。
    /// 落盘用 PlayerPrefs，格式一行一件："兽id|槽位|装备id"，没穿就不写。
    /// </summary>
    public static class EquipmentPresets
    {
        const string Key = "ljjh.equip.v1";
        static Dictionary<string, EquipmentLoadout> map;

        static Dictionary<string, EquipmentLoadout> Map
        {
            get
            {
                if (map == null) Load();
                return map;
            }
        }

        public static EquipmentLoadout Get(string beastId)
        {
            EquipmentLoadout l;
            if (!Map.TryGetValue(beastId, out l))
            {
                l = new EquipmentLoadout();
                Map[beastId] = l;
            }
            return l;
        }

        public static void Set(string beastId, EquipmentDefinition def)
        {
            Get(beastId).Set(def);
            Save();
        }

        public static void Unequip(string beastId, EquipSlot slot)
        {
            var l = Get(beastId);
            l.slotItems[(int)slot] = null;
            Save();
        }

        /// <summary>五件全穿（调试与「一键满配」用）。</summary>
        public static void EquipAllFive(string beastId, string[] ids)
        {
            var l = new EquipmentLoadout();
            for (int i = 0; i < ids.Length; i++)
            {
                var eq = LingJing.Battle.BattleDatabase.GetEquipment(ids[i]);
                if (eq != null) l.Set(eq);
            }
            Map[beastId] = l;
            Save();
        }

        public static bool IsEmpty(string beastId)
        {
            return Get(beastId).IsEmpty();
        }

        public static void Save()
        {
            var lines = new List<string>();
            foreach (var kv in Map)
            {
                var slots = kv.Value.slotItems;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    lines.Add(kv.Key + "|" + i + "|" + slots[i].id);
                }
            }
            PlayerPrefs.SetString(Key, string.Join("\n", lines.ToArray()));
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            map = new Dictionary<string, EquipmentLoadout>();
            string raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) return;

            var lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('|');
                if (parts.Length != 3) continue;
                int slot;
                if (!int.TryParse(parts[1], out slot)) continue;
                var eq = LingJing.Battle.BattleDatabase.GetEquipment(parts[2]);
                if (eq == null) continue;
                Get(parts[0]).slotItems[slot] = eq;
            }
        }

        public static void ResetAll()
        {
            map = new Dictionary<string, EquipmentLoadout>();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
