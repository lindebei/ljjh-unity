using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    /// <summary>一场遭遇：若干波敌人 + 怪物等级。</summary>
    public class Encounter
    {
        public int level;                          // 怪物等级（整场统一）
        public List<string[]> waves = new List<string[]>();
        public int eliteCount;
        public int TotalCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < waves.Count; i++) n += waves[i].Length;
                return n;
            }
        }
    }

    /// <summary>
    /// 一境随机遭遇生成器。
    /// 规则全部来自 `E:\TT\一境地图与遇敌结构.md` 第四节：
    ///   · 怪物等级 = clamp(玩家等级 ± 2, 生态下限, 生态上限)
    ///   · 敌群规模 1–4 只，按等级带加权（1–5 级偏低、13 级以上偏高）
    ///   · 每场 12% 概率把 1 只普通替换成精英，同场最多 2 只
    ///   · 多波合计最多 10 只（每波 ≤ 4）
    /// 这里是纯函数：给定种子就一定出同一场，便于复现与批测。
    /// </summary>
    public static class EncounterGenerator
    {
        public const int MaxPerWave = 4;      // 单波上限
        public const int MaxTotal = 10;       // 多波合计上限（4+3+3）
        public const float EliteChance = 0.12f;
        public const int MaxElite = 2;

        /// <summary>玩家等级 ±2，再夹到 1–24（一境封顶）。</summary>
        public static int RollLevel(int playerLevel, System.Random rng, int minLevel = 1, int maxLevel = 24)
        {
            int lv = playerLevel + rng.Next(-2, 3);
            return Mathf.Clamp(lv, minLevel, maxLevel);
        }

        /// <summary>按等级带决定基础只数：1–5 级 1–2 只，6–12 级 2–3 只，13 级以上 3–4 只。</summary>
        public static int RollBaseSize(int bandIndex, System.Random rng)
        {
            switch (bandIndex)
            {
                case 0: return rng.Next(1, 3);     // 1–2
                case 1: return rng.Next(2, 4);     // 2–3
                default: return rng.Next(3, 5);    // 3–4
            }
        }

        /// <summary>生成一场遭遇。</summary>
        public static Encounter Roll(int playerLevel, int bandIndex, System.Random rng)
        {
            var enc = new Encounter();
            enc.level = RollLevel(playerLevel, rng);

            var normals = BattleDatabase.EnemiesOfQuality(Quality.Normal);
            var elites = BattleDatabase.EnemiesOfQuality(Quality.Elite);
            if (normals.Count == 0)
            {
                Debug.LogError("[EncounterGenerator] 普通怪池是空的，检查 enemies.csv");
                return enc;
            }

            int size = Mathf.Clamp(RollBaseSize(bandIndex, rng), 1, MaxPerWave);

            // 大场次偶尔再来一波：13 级以上 18% 概率补一波（4+3 / 3+3），总只数不超 MaxTotal
            var sizes = new List<int> { size };
            if (bandIndex >= 2 && size + 3 <= MaxTotal && rng.NextDouble() < 0.18)
            {
                int extra = Mathf.Min(MaxPerWave, MaxTotal - size);
                if (extra >= 2) sizes.Add(extra);
            }

            // 精英：一场最多 2 次 12% 判定，替换掉普通位
            int total = 0;
            for (int i = 0; i < sizes.Count; i++) total += sizes[i];

            int eliteSlots = 0;
            if (elites.Count > 0)
            {
                for (int i = 0; i < MaxElite; i++)
                    if (rng.NextDouble() < EliteChance) eliteSlots++;
                eliteSlots = Mathf.Min(eliteSlots, total - 1);   // 至少留一只普通
            }

            int cursor = 0;   // 已分配的怪数，用来决定哪几个位置是精英
            for (int w = 0; w < sizes.Count; w++)
            {
                var ids = new string[sizes[w]];
                for (int i = 0; i < ids.Length; i++)
                {
                    bool isElite = cursor < eliteSlots;
                    var pool = isElite ? elites : normals;
                    ids[i] = pool[rng.Next(pool.Count)].id;
                    cursor++;
                }
                enc.waves.Add(ids);
            }

            enc.eliteCount = eliteSlots;
            return enc;
        }

        /// <summary>把遭遇摊成一句人话，方便日志与批测报告。</summary>
        public static string Describe(Encounter enc)
        {
            if (enc == null) return "(空)";
            var sb = new System.Text.StringBuilder();
            sb.Append("lv").Append(enc.level).Append(" · ").Append(enc.TotalCount).Append(" 只");
            if (enc.waves.Count > 1) sb.Append(" · ").Append(enc.waves.Count).Append(" 波");
            if (enc.eliteCount > 0) sb.Append(" · 精英 ").Append(enc.eliteCount);
            sb.Append(" [");
            for (int w = 0; w < enc.waves.Count; w++)
            {
                if (w > 0) sb.Append(" | ");
                sb.Append(string.Join(",", enc.waves[w]));
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
