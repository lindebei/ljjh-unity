// 一境地图：生态区查表 + 暗雷遇敌 + 地标。纯逻辑，不碰 UI。
using System;
using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    public static class WorldMap
    {
        public const float Size = 800f;            // 地图边长（米）
        public const float PxPerMeter = 1f;        // UI 换算用不到，留给需要的地方

        // 三个 Boss 是「可见地标」，位置固定（见《一境地图与遇敌结构》第六节）
        public static readonly Vector2 MiniBossPos = new Vector2(520f, 260f);   // 水泽深处洞口
        public static readonly Vector2 MidBossPos = new Vector2(90f, 130f);     // 赤土裂口
        public static readonly Vector2 BigBossPos = new Vector2(400f, 400f);    // 灵坛中心
        public const float BossRadius = 26f;

        // 据点与四个补给点（踩上去回满血、补满药、存档）
        public static readonly Vector2[] SupplyPoints = new Vector2[]
        {
            new Vector2(60f, 60f),      // 据点（起点）
            new Vector2(330f, 150f),    // 枯林入口
            new Vector2(150f, 330f),    // 赤土入口
            new Vector2(400f, 620f),    // 风口入口
            new Vector2(400f, 310f),    // 灵坛外圈
        };
        public const float SupplyRadius = 16f;

        static List<ZoneDefinition> zones;

        public static void Reload()
        {
            zones = new List<ZoneDefinition>(Resources.LoadAll<ZoneDefinition>("BattleData/Zones"));
            zones.Sort((a, b) => (a.w * a.h).CompareTo(b.w * b.h));   // 小的优先命中（灵坛压在水泽上）
        }

        public static List<ZoneDefinition> All
        {
            get { if (zones == null) Reload(); return zones; }
        }

        /// <summary>点所在的生态区；灵坛这类小区域压在大的上面，靠尺寸排序保证优先命中。</summary>
        public static ZoneDefinition ZoneAt(float x, float y)
        {
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Contains(x, y)) return all[i];
            return null;
        }

        public static ZoneDefinition ZoneById(string id)
        {
            var all = All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].id == id) return all[i];
            return null;
        }

        /// <summary>怪物等级 = clamp(玩家等级 ± 2, 该生态下限, 上限)。</summary>
        public static int RollLevel(ZoneDefinition zone, int playerLevel, System.Random rng)
        {
            int lv = playerLevel + rng.Next(-2, 3);
            if (zone == null) return Mathf.Clamp(lv, 1, 24);
            return Mathf.Clamp(lv, zone.minLevel, zone.maxLevel);
        }

        /// <summary>按生态区抽一批敌人：1–4 只，12% 概率把一只普通换成精英（最多 2 只）。</summary>
        public static List<string> RollEnemies(ZoneDefinition zone, int playerLevel, System.Random rng)
        {
            var result = new List<string>();
            if (zone == null) return result;

            int band = playerLevel <= 5 ? 0 : (playerLevel <= 12 ? 1 : 2);
            int min = band == 0 ? 1 : (band == 1 ? 2 : 3);
            int max = band == 0 ? 2 : (band == 1 ? 3 : 4);
            int count = rng.Next(min, max + 1);

            for (int i = 0; i < count; i++)
            {
                var el = zone.RollElement(rng);
                var id = PickEnemy(el, Quality.Normal, rng);
                if (id == null) id = PickEnemy(el, Quality.Normal, rng, true);
                if (id != null) result.Add(id);
            }

            // 精英替换：每场 12% 判定，最多 2 只，且至少保留一只普通
            int elites = 0;
            for (int i = 0; i < result.Count && elites < 2; i++)
            {
                if (rng.NextDouble() >= 0.12) continue;
                var el = zone.RollElement(rng);
                var eid = PickEnemy(el, Quality.Elite, rng);
                if (eid != null) { result[i] = eid; elites++; }
            }
            return result;
        }

        static string PickEnemy(Element el, Quality q, System.Random rng, bool anyElement = false)
        {
            var pool = BattleDatabase.EnemiesOfQuality(q);
            var hits = new List<string>();
            for (int i = 0; i < pool.Count; i++)
            {
                if (!anyElement && pool[i].element != el) continue;
                hits.Add(pool[i].id);
            }
            if (hits.Count == 0) return null;
            return hits[rng.Next(0, hits.Count)];
        }

        public static bool NearSupply(Vector2 p, out Vector2 which)
        {
            for (int i = 0; i < SupplyPoints.Length; i++)
            {
                if (Vector2.Distance(p, SupplyPoints[i]) <= SupplyRadius) { which = SupplyPoints[i]; return true; }
            }
            which = default(Vector2);
            return false;
        }

        public static string BossIdNear(Vector2 p)
        {
            if (Vector2.Distance(p, MiniBossPos) <= BossRadius) return "boss_mini";
            if (Vector2.Distance(p, MidBossPos) <= BossRadius) return "boss_mid";
            if (Vector2.Distance(p, BigBossPos) <= BossRadius) return "boss_big";
            return null;
        }
    }
}
