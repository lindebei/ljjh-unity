using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LingJing.Data;
using LingJing.Battle;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 对账用批测：同一批场景同时跑「期望值」与「随机（固定 seed）」两种口径。
///
/// 为什么要有这个文件：UI 自动战斗与 batchmode 批测此前用的是两套随机源
/// （DamageCalculator 里的 UnityEngine.Random 不受种子控制），导致同一场战斗
/// 一个折一员、一个满血通关，任何「怪太软」的结论都不成立。
///
/// 命令行：
///   Unity.exe -batchmode -quit -nographics -projectPath D:\ljjh\unity
///            -executeMethod BattleReconcile.RunAll -logFile D:\ljjh\tmp\reconcile.log
/// </summary>
public static class BattleReconcile
{
    const int PlayerLevel = 24;
    const int Runs = 200;                       // 随机模式每场景跑多少场
    const string ReportPath = "D:/ljjh/tmp/reconcile.txt";

    static readonly int[] FreePoints = { 23, 23, 23, 23, 23, 23 };   // 138 点，六项均分
    static readonly string[] Players = { "beast_water", "beast_fire", "beast_wind" };

    [MenuItem("灵境/对账批测 (期望值 vs 随机)")]
    public static void RunAll()
    {
        var baseCfg = BattleDatabase.Config;
        if (baseCfg == null)
        {
            Debug.LogError("[BattleReconcile] BattleConfig 缺失，先跑 灵境/导入战斗数据。");
            return;
        }

        var scenarios = new List<Scenario>
        {
            new Scenario("普通x2 · lv10", new[] { "en_bonefish", "en_stonelizard" }, 10),
            new Scenario("普通x3 · lv10", new[] { "en_bonefish", "en_stonelizard", "en_rat" }, 10),
            new Scenario("普通x2 · lv24", new[] { "en_bonefish", "en_stonelizard" }, 24),
            new Scenario("精英x1 · lv10", new[] { "en_bird" }, 10),
            new Scenario("精英x2 · lv10", new[] { "en_bird", "en_fox" }, 10),
            new Scenario("小Boss · lv16", new[] { "en_jelly" }, 16),
            // 这一条是 BattlePrototype 场景里实际用的编成（2 普通 + 1 精英），
            // 之前拿 UI 的 14 回合去比批测的「精英x1」是拿两个不同场景比，结论作废。
            new Scenario("UI同款 普2+精1 · lv10", new[] { "en_bonefish", "en_stonelizard", "en_bird" }, 10),
            // 设计上限：一境地图文档写「敌群规模 1–4 只」「同场最多 2 只精英」，
            // 之前只测到 3 只，上限这一档没覆盖。
            new Scenario("上限 普通x4 · lv10", new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" }, 10),
            new Scenario("上限 普2+精2 · lv10", new[] { "en_rat", "en_bear", "en_bird", "en_fox" }, 10),
            // 压力档：用户提到「可能遇到 10 个怪的阵容」。文档里没有这条，先量一下会发生什么。
            new Scenario("压力 普通x10 · lv10", new[] {
                "en_bonefish", "en_stonelizard", "en_rat", "en_firetiger", "en_graywolf",
                "en_bear", "en_bonefish", "en_stonelizard", "en_rat", "en_firetiger" }, 10),
        };

        var report = new StringBuilder();
        report.AppendLine("# 对账报告 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("# 我方：水/火/风 24 级，自由点 138 均分六项；期望值模式 1 场，随机模式 " + Runs + " 场");
        report.AppendLine("场景\t模式\t场次\t胜率\t平均回合\t平均时长s\t我方剩血%\t敌方剩血%\t平均存活");

        foreach (var sc in scenarios)
        {
            var expected = Agg(Batch(baseCfg, sc, true, 1));
            var random = Agg(Batch(baseCfg, sc, false, Runs));
            report.AppendLine(Row(sc.name, "期望值", expected));
            report.AppendLine(Row(sc.name, "随机", random));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(false));
        Debug.Log("[BattleReconcile] 报告写入 " + ReportPath);
    }

    struct Scenario
    {
        public string name;
        public string[] enemyIds;
        public int enemyLevel;

        public Scenario(string name, string[] ids, int level)
        {
            this.name = name;
            this.enemyIds = ids;
            this.enemyLevel = level;
        }
    }

    struct Aggregate
    {
        public int runs;
        public float winRate;
        public float avgRounds;
        public float avgDuration;
        public float avgPlayerHp;
        public float avgEnemyHp;
        public float avgAlive;
    }

    static List<BattleResult> Batch(BattleConfig baseCfg, Scenario sc, bool expected, int runs)
    {
        var results = new List<BattleResult>(runs);
        for (int i = 0; i < runs; i++)
        {
            // BattleConfig 是 ScriptableObject，直接改会污染资源；这里用副本+克隆字段的方式避免。
            var cfg = UnityEngine.Object.Instantiate(baseCfg);
            cfg.useExpectedValue = expected;
            try
            {
                var r = RunOne(cfg, sc, 1000 + i);
                if (r != null) results.Add(r.Value);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cfg);
            }
        }
        return results;
    }

    static BattleResult? RunOne(BattleConfig cfg, Scenario sc, int seed)
    {
        var sim = new BattleSimulator(cfg, seed);

        for (int i = 0; i < Players.Length; i++)
        {
            var def = BattleDatabase.GetCreature(Players[i]);
            if (def == null) { Debug.LogError("[BattleReconcile] 缺 " + Players[i]); return null; }
            sim.AddPlayer(def, PlayerLevel, FreePoints);
        }

        int band = cfg.BandIndex(sc.enemyLevel);
        for (int i = 0; i < sc.enemyIds.Length; i++)
        {
            var def = BattleDatabase.GetCreature(sc.enemyIds[i]);
            if (def == null) { Debug.LogError("[BattleReconcile] 缺 " + sc.enemyIds[i]); return null; }
            sim.AddEnemy(def, sc.enemyLevel, band);
        }

        sim.Begin();
        return sim.Run();
    }

    static Aggregate Agg(List<BattleResult> rs)
    {
        var a = new Aggregate { runs = rs.Count };
        if (rs.Count == 0) return a;

        int wins = 0;
        for (int i = 0; i < rs.Count; i++)
        {
            var r = rs[i];
            if (r.playersAlive > 0 && r.enemyHpPercent <= 0.001f) wins++;
            a.avgRounds += r.rounds;
            a.avgDuration += r.durationSeconds;
            a.avgPlayerHp += r.playerHpPercent;
            a.avgEnemyHp += r.enemyHpPercent;
            a.avgAlive += r.playersAlive;
        }
        float n = rs.Count;
        a.winRate = wins / n;
        a.avgRounds /= n;
        a.avgDuration /= n;
        a.avgPlayerHp /= n;
        a.avgEnemyHp /= n;
        a.avgAlive /= n;
        return a;
    }

    static string Row(string name, string mode, Aggregate a)
    {
        return string.Format("{0}\t{1}\t{2}\t{3:P0}\t{4:F1}\t{5:F1}\t{6:P0}\t{7:P0}\t{8:F2}",
            name, mode, a.runs, a.winRate, a.avgRounds, a.avgDuration, a.avgPlayerHp, a.avgEnemyHp, a.avgAlive);
    }
}
