using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LingJing.Data;
using LingJing.Battle;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 无头对账：批量跑几场战斗，把结果写成一行行文本，方便跟 E:\TT\sim\ 的期望值比。
/// 命令行：Unity.exe -batchmode -quit -nographics -projectPath D:\ljjh\unity
///         -executeMethod BattleBatchTest.RunAll -logFile D:\ljjh\tmp\battle.log
/// </summary>
public static class BattleBatchTest
{
    const int PlayerLevel = 24;
    const string ReportPath = "D:/ljjh/tmp/battle-report.txt";

    [MenuItem("灵境/跑无头对账 (batchmode 用)")]
    public static void RunAll()
    {
        var cfg = BattleDatabase.Config;
        if (cfg == null)
        {
            Debug.LogError("[BattleBatchTest] BattleConfig 缺失，先跑 灵境/导入战斗数据。");
            return;
        }

        var report = new StringBuilder();
        report.AppendLine("场景\t结果\t时长(s)\t我方剩余HP%\t敌方剩余HP%\t回合\t我方行动数\t备注");

        var scenarios = new List<Scenario>
        {
            new Scenario("普通x2 · lv10", new[] { "en_bonefish", "en_stonelizard" }, 10),
            new Scenario("普通x3 · lv10", new[] { "en_bonefish", "en_stonelizard", "en_rat" }, 10),
            new Scenario("普通x2 · lv24", new[] { "en_bonefish", "en_stonelizard" }, 24),
            new Scenario("精英x1 · lv10", new[] { "en_bird" }, 10),
            new Scenario("精英x2 · lv10", new[] { "en_bird", "en_fox" }, 10),
            new Scenario("小Boss · lv16", new[] { "en_jelly" }, 16),
        };

        for (int i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            string line = RunOne(cfg, s);
            report.AppendLine(line);
            Debug.Log("[BattleBatchTest] " + line);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(false));
        Debug.Log("[BattleBatchTest] 报告写入 " + ReportPath);
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

    static string RunOne(BattleConfig cfg, Scenario sc)
    {
        var sim = new BattleSimulator(cfg);
        int[] free = { 23, 23, 23, 23, 23, 23 };   // 138 点，六项均分（v0.1 口径：满配、单项不超 60）

        string[] players = { "beast_water", "beast_fire", "beast_wind" };
        for (int i = 0; i < players.Length; i++)
        {
            var def = BattleDatabase.GetCreature(players[i]);
            if (def == null) return sc.name + "\t数据缺失\t-\t-\t-\t-\t-\t缺 " + players[i];
            sim.AddPlayer(def, PlayerLevel, free);
        }

        int band = cfg.BandIndex(sc.enemyLevel);
        for (int i = 0; i < sc.enemyIds.Length; i++)
        {
            var def = BattleDatabase.GetCreature(sc.enemyIds[i]);
            if (def == null) return sc.name + "\t数据缺失\t-\t-\t-\t-\t-\t缺 " + sc.enemyIds[i];
            sim.AddEnemy(def, sc.enemyLevel, band);
        }

        sim.Begin();
        var r = sim.Run();

        string outcome;
        if (r.playersAlive == 0) outcome = "失败";
        else if (r.enemyHpPercent <= 0.001f) outcome = "胜利";
        else if (r.timedOut) outcome = "超时";
        else outcome = "未结束";

        return string.Format("{0}\t{1}\t{2:F1}\t{3:P1}\t{4:P1}\t{5}\t{6}\t我方存活{7}",
            sc.name, outcome, r.durationSeconds, r.playerHpPercent, r.enemyHpPercent,
            r.rounds, r.playerActions, r.playersAlive);
    }
}
