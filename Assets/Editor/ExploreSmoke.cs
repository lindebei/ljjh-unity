// 一境地图数据自检：生态区命中、遇敌生成、Boss 地标、雾障门槛。菜单「灵境/一境地图自检」。
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using LingJing.Battle;
using LingJing.Data;

public static class ExploreSmoke
{
    const string Out = "D:/ljjh/tmp/explore_smoke.txt";

    [MenuItem("灵境/一境地图自检")]
    public static void Run()
    {
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(string name, bool ok, string detail = "")
        {
            sb.AppendLine((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  " + detail : ""));
            if (ok) pass++; else fail++;
        }

        WorldMap.Reload();
        Check("生态区 5 个", WorldMap.All.Count == 5, "实际 " + WorldMap.All.Count);

        var sp = WorldMap.ZoneAt(300f, 500f);
        Check("(300,500) 命中水泽", sp != null && sp.id == "zone_spring", sp != null ? sp.id : "null");
        var altar = WorldMap.ZoneAt(400f, 400f) ;
        var z2 = WorldMap.ZoneAt(390f, 390f);
        Check("(390,390) 命中灵坛（小区域优先）", z2 != null && z2.id == "zone_altar", z2 != null ? z2.id : "null");
        var dead = WorldMap.ZoneAt(700f, 400f);
        Check("(700,400) 命中枯林", dead != null && dead.id == "zone_deadwood", dead != null ? dead.id : "null");

        var rng = new System.Random(20261107);
        int minCount = 99, maxCount = 0, maxElite = 0, zero = 0;
        for (int i = 0; i < 2000; i++)
        {
            var list = WorldMap.RollEnemies(sp, 24, rng);
            if (list == null || list.Count == 0) { zero++; continue; }
            minCount = Mathf.Min(minCount, list.Count);
            maxCount = Mathf.Max(maxCount, list.Count);
            int elite = 0;
            for (int k = 0; k < list.Count; k++)
            {
                var c = BattleDatabase.GetCreature(list[k]);
                if (c != null && c.quality >= Quality.Elite) elite++;
            }
            maxElite = Mathf.Max(maxElite, elite);
        }
        Check("遇敌只数 1–4", zero == 0 && minCount >= 1 && maxCount <= 4, "min=" + minCount + " max=" + maxCount + " zero=" + zero);
        Check("精英不超过 2 只", maxElite <= 2, "maxElite=" + maxElite);

        int lvOk = 0;
        for (int i = 0; i < 500; i++)
        {
            int lv = WorldMap.RollLevel(sp, 24, rng);
            if (lv >= sp.minLevel && lv <= sp.maxLevel) lvOk++;
        }
        Check("等级落在生态区区间内", lvOk == 500, lvOk + "/500");

        Check("Boss 地标命中", WorldMap.BossIdNear(WorldMap.MiniBossPos) == "boss_mini"
            && WorldMap.BossIdNear(WorldMap.MidBossPos) == "boss_mid"
            && WorldMap.BossIdNear(WorldMap.BigBossPos) == "boss_big");
        Vector2 which;
        Check("据点识别", WorldMap.NearSupply(new Vector2(60f, 60f), out which));
        Check("灵坛门槛 = 18", WorldMap.ZoneById("zone_altar").gateLevel == 18, "gate=" + WorldMap.ZoneById("zone_altar").gateLevel);

        sb.AppendLine("---- " + pass + " PASS / " + fail + " FAIL");
        System.IO.File.WriteAllText(Out, sb.ToString());
        Debug.Log("[ExploreSmoke] " + pass + " PASS / " + fail + " FAIL -> " + Out);
        if (fail > 0) Debug.LogError(sb.ToString());
    }
}