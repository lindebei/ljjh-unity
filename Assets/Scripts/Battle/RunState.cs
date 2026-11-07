// 一境探索的跨场景状态：队伍、剩余血量、掉落、当前层数。
// 存 PlayerPrefs，地图场景和战斗场景都读它 —— 这样「连续遭遇血量继承」才成立。
using System.Collections.Generic;
using UnityEngine;

namespace LingJing.Battle
{
    public static class RunState
    {
        const string Key = "ljjh.run.v1";

        public static bool Active { get; private set; }
        public static int Floor { get; private set; } = 1;
        public static readonly List<string> PartyIds = new List<string>();
        public static readonly List<float> HpPct = new List<float>();
        public static readonly List<string> Loot = new List<string>();
        /// <summary>本次进战斗的节点类型：normal / elite / boss / rest</summary>
        public static string PendingNode = "normal";

        /// <summary>待战遭遇的生态区 id（暗雷遇敌时写进来）。</summary>
        public static string PendingZone = "";
        /// <summary>待战遭遇的怪物等级。</summary>
        public static int PendingLevel = 24;
        /// <summary>队伍等级（三兽统一，一境先按同一级处理）。</summary>
        public static int PlayerLevel = 24;
        /// <summary>玩家在地图上的位置（米）。</summary>
        public static float PosX = 60f;
        public static float PosY = 60f;

        public static void Reset()
        {
            Active = true;
            Floor = 1;
            PartyIds.Clear(); HpPct.Clear(); Loot.Clear();
            foreach (var id in new[] { "beast_water", "beast_fire", "beast_wind" })
            {
                PartyIds.Add(id);
                HpPct.Add(1f);
            }
            PendingNode = "normal";
            Save();
        }

        public static void End() { Active = false; PlayerPrefs.DeleteKey(Key); PlayerPrefs.Save(); }

        /// <summary>用一场战斗的结果更新状态（血量按百分比继承，掉落累加）。</summary>
        public static void Capture(List<BattleUnit> players, bool victory, List<string> loot)
        {
            HpPct.Clear();
            for (int i = 0; i < players.Count && i < PartyIds.Count; i++)
                HpPct.Add(players[i].maxHp <= 0f ? 0f : Mathf.Clamp01(players[i].hp / players[i].maxHp));
            while (HpPct.Count < PartyIds.Count) HpPct.Add(1f);
            if (loot != null) Loot.AddRange(loot);
            if (victory) Floor++;
            Save();
        }

        /// <summary>休整节点：全队回 30% 最大生命，层数 +1。</summary>
        public static void Rest()
        {
            for (int i = 0; i < HpPct.Count; i++) HpPct[i] = Mathf.Clamp01(HpPct[i] + 0.30f);
            Floor++;
            Save();
        }

        public static void Save()
        {
            var hp = new List<string>();
            for (int i = 0; i < HpPct.Count; i++) hp.Add(HpPct[i].ToString("F3"));
            var data = string.Join(";", new[]
            {
                Active ? "1" : "0",
                Floor.ToString(),
                string.Join(",", PartyIds),
                string.Join(",", hp),
                string.Join(",", Loot),
                PendingNode ?? "normal"
            });
            PlayerPrefs.SetString(Key, data);
            PlayerPrefs.Save();
        }

        public static void Load()
        {
            var raw = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(raw)) { Active = false; return; }
            var parts = raw.Split(';');
            if (parts.Length < 6) { Active = false; return; }
            Active = parts[0] == "1";
            int f; Floor = int.TryParse(parts[1], out f) ? f : 1;
            PartyIds.Clear(); HpPct.Clear(); Loot.Clear();
            if (parts[2].Length > 0) PartyIds.AddRange(parts[2].Split(','));
            if (parts[3].Length > 0)
                foreach (var s in parts[3].Split(','))
                {
                    float v;
                    HpPct.Add(float.TryParse(s, out v) ? Mathf.Clamp01(v) : 1f);
                }
            if (parts[4].Length > 0) Loot.AddRange(parts[4].Split(','));
            PendingNode = parts[5];
        }
    }
}