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
            new Scenario("普通x2 · lv10", W(new[] { "en_bonefish", "en_stonelizard" }), 10),
            new Scenario("普通x3 · lv10", W(new[] { "en_bonefish", "en_stonelizard", "en_rat" }), 10),
            new Scenario("普通x2 · lv24", W(new[] { "en_bonefish", "en_stonelizard" }), 24),
            new Scenario("精英x1 · lv10", W(new[] { "en_bird" }), 10),
            new Scenario("精英x2 · lv10", W(new[] { "en_bird", "en_fox" }), 10),
            new Scenario("小Boss · lv16", W(new[] { "en_jelly" }), 16),
            // 这一条是 BattlePrototype 场景里实际用的编成（2 普通 + 1 精英）
            new Scenario("UI同款 普2+精1 · lv10", W(new[] { "en_bonefish", "en_stonelizard", "en_bird" }), 10),

            // 设计上限：一境地图文档「敌群规模 1–4 只」「同场最多 2 只精英」
            new Scenario("上限 普通x4 · lv10", W(new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" }), 10),
            new Scenario("上限 普2+精2 · lv10", W(new[] { "en_rat", "en_bear", "en_bird", "en_fox" }), 10),

            // 多波：一境战斗流程文档的 WaveEnter（一波清完不判胜利，直接入下一波）。
            // 「可能遇到 10 个怪」按这个口径理解：每波 ≤4，总数十几只。
            new Scenario("波次 4+4 (总8) · lv10", W(
                new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                new[] { "en_firetiger", "en_graywolf", "en_bonefish", "en_rat" }), 10),
            new Scenario("波次 4+3+3 (总10) · lv10", W(
                new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                new[] { "en_firetiger", "en_graywolf", "en_bonefish" },
                new[] { "en_stonelizard", "en_rat", "en_firetiger" }), 10),

            // ── 同级口径：真实一境是同级推进，只有这组数才能拿来定「胜率该是多少」 ──
            new Scenario("同级 普通x2 · lv10", W(new[] { "en_bonefish", "en_stonelizard" }), 10, 10),
            new Scenario("同级 普通x3 · lv10", W(new[] { "en_bonefish", "en_stonelizard", "en_rat" }), 10, 10),
            new Scenario("同级 上限 普通x4 · lv10", W(new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" }), 10, 10),
            new Scenario("同级 精英x1 · lv11", W(new[] { "en_bird" }), 11, 11),
            new Scenario("同级 精英x2 · lv11", W(new[] { "en_bird", "en_fox" }), 11, 11),
            new Scenario("同级 普2+精2 · lv12", W(new[] { "en_rat", "en_bear", "en_bird", "en_fox" }), 12, 12),
            new Scenario("同级 小Boss · lv16", W(new[] { "en_jelly" }), 16, 16),
            new Scenario("同级 波次 4+3+3 (总10) · lv10", W(
                new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                new[] { "en_firetiger", "en_graywolf", "en_bonefish" },
                new[] { "en_stonelizard", "en_rat", "en_firetiger" }), 10, 10),
            // ── 带装备口径：满配（5 件紫 + 强化 3）对照，验收标准是「同级小 Boss ≥90%」 ──
            new Scenario("同级 普通x4 · lv10 +满配", W(new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" }), 10, 10, true),
            new Scenario("同级 精英x2 · lv11 +满配", W(new[] { "en_bird", "en_fox" }), 11, 11, true),
            new Scenario("同级 小Boss · lv16 +满配", W(new[] { "en_jelly" }), 16, 16, true),

            // 超出设计的压力档：同屏 10 只（没有波次），只用来确认护栏有效
            new Scenario("压力 同屏x10 · lv10", W(new[] {
                "en_bonefish", "en_stonelizard", "en_rat", "en_firetiger", "en_graywolf",
                "en_bear", "en_bonefish", "en_stonelizard", "en_rat", "en_firetiger" }), 10),
        };

        var report = new StringBuilder();
        report.AppendLine("# 对账报告 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("# 我方：水/火/风，等级随场景（默认 24 级满自由点）；期望值模式 1 场，随机模式 " + Runs + " 场");
        report.AppendLine("场景\t模式\t场次\t胜率\t平均回合\t平均时长s\t我方剩血%\t敌方剩血%\t平均存活");

        var lootTally = new Dictionary<string, int>();
        foreach (var sc in scenarios)
        {
            var expected = Agg(Batch(baseCfg, sc, true, 1));
            var rr = Batch(baseCfg, sc, false, Runs);
            var random = Agg(rr);
            report.AppendLine(Row(sc.name, "期望值", expected));
            report.AppendLine(Row(sc.name, "随机", random));

            for (int i = 0; i < rr.Count; i++)
            {
                if (rr[i].loot == null) continue;
                for (int k = 0; k < rr[i].loot.Count; k++)
                {
                    string id = rr[i].loot[k];
                    int n;
                    lootTally.TryGetValue(id, out n);
                    lootTally[id] = n + 1;
                }
            }
        }

        report.AppendLine();
        report.AppendLine("# 掉落统计（所有随机场次合计，含满配场）");
        foreach (var kv in lootTally) report.AppendLine(kv.Key + "\t" + kv.Value);

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(false));
        Debug.Log("[BattleReconcile] 报告写入 " + ReportPath);
    }

    struct Scenario
    {
        public string name;
        public string[][] waves;      // 每个元素是一波（每波 ≤4 只的设计上限由数据决定）
        public int enemyLevel;
        public int playerLevel;       // 我方等级（真实一境是同级推进，只有同级的胜率才有意义）
        public int[] freePoints;      // 按 playerLevel 给：每级 6 点，六项均分
        public bool geared;           // 是否穿满配紫装（5 件 + 强化 3）
        public int rank;              // 技能等级：0=用存档，1/2/3=强制 I/II/III

        public Scenario(string name, string[][] waves, int enemyLevel, int playerLevel)
        {
            this.name = name;
            this.waves = waves;
            this.enemyLevel = enemyLevel;
            this.playerLevel = playerLevel;
            int per = (playerLevel - 1) * 6 / 6;      // 每级 6 点自由点，均分到六项
            this.freePoints = new[] { per, per, per, per, per, per };
            this.geared = false;
            this.rank = 0;
        }

        public Scenario(string name, string[][] waves, int enemyLevel, int playerLevel, bool geared)
            : this(name, waves, enemyLevel, playerLevel) { this.geared = geared; }

        public Scenario(string name, string[][] waves, int enemyLevel)
            : this(name, waves, enemyLevel, PlayerLevel) { }
    }

    static string[][] W(params string[][] waves) { return waves; }
    static string[][] W(params string[] ids) { return new[] { ids }; }

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
        return Batch(baseCfg, sc, expected, runs, baseCfg.aoeDamageScale);
    }

    /// <summary>扫 aoeDamageScale：只跑指定的几个场景，输出胜率/回合/时长，用来给清场效率选值。</summary>
    public static void SweepAoe()
    {
        float[] scales = { 1.0f, 1.5f, 2.0f, 2.5f, 3.0f };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# aoeDamageScale 扫描（同级 lv10，随机 200 场，上限 30 回合）");

        var configs = new List<Scenario>
        {
            new Scenario("波次 4+4 (总8)", W(new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                                            new[] { "en_firetiger", "en_graywolf", "en_bonefish", "en_rat" }), 10, 10),
            new Scenario("波次 4+3+3 (总10)", W(new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                                                new[] { "en_firetiger", "en_graywolf", "en_bonefish" },
                                                new[] { "en_fox", "en_bird", "en_rat" }), 10, 10),
            new Scenario("同级 精英x2 · lv11", W(new[] { "en_bird", "en_fox" }), 11, 11),
            new Scenario("同级 小Boss · lv16", W(new[] { "en_jelly" }), 16, 16),
        };

        var baseCfg = BattleDatabase.Config;
        for (int s = 0; s < scales.Length; s++)
        {
            for (int c = 0; c < configs.Count; c++)
            {
                var res = Batch(baseCfg, configs[c], false, 200, scales[s]);
                var ag = Aggregate2(res);
                sb.AppendLine(string.Format("scale={0:F2}\taoe次数={7}\t{1}\t胜率 {2:P0}\t回合 {3:F1}\t时长 {4:F0}s\t我方剩血 {5:P0}\t敌方剩血 {6:P0}",
                    scales[s], configs[c].name, ag.winRate, ag.avgRounds, ag.avgDuration, ag.avgPlayerHp, ag.avgEnemyHp, BattleSimulator.aoeCastCount));
            }
            sb.AppendLine();
        }

        System.IO.File.WriteAllText("D:/ljjh/tmp/sweep_aoe.txt", sb.ToString(), new System.Text.UTF8Encoding(false));
        Debug.Log("[BattleReconcile] aoe 扫描完成 -> D:/ljjh/tmp/sweep_aoe.txt");
    }


    /// <summary>扫「每回合我方行动次数」：清场效率的第二个旋钮。</summary>
    public static void SweepActions()
    {
        int[] acts = { 3, 4, 5 };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# playerActionsPerRound 扫描（aoeDamageScale 取配置值，随机 200 场，上限 30 回合）");

        var configs = new List<Scenario>
        {
            new Scenario("波次 4+4 (总8)", W(new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                                            new[] { "en_firetiger", "en_graywolf", "en_bonefish", "en_rat" }), 10, 10),
            new Scenario("波次 4+3+3 (总10)", W(new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                                                new[] { "en_firetiger", "en_graywolf", "en_bonefish" },
                                                new[] { "en_fox", "en_bird", "en_rat" }), 10, 10),
            new Scenario("上限 普2+精2 · lv10", W(new[] { "en_rat", "en_bear", "en_bird", "en_fox" }), 10),
            new Scenario("同级 精英x2 · lv11", W(new[] { "en_bird", "en_fox" }), 11, 11),
            new Scenario("同级 小Boss · lv16", W(new[] { "en_jelly" }), 16, 16),
        };

        var baseCfg = BattleDatabase.Config;
        for (int a = 0; a < acts.Length; a++)
        {
            for (int c = 0; c < configs.Count; c++)
            {
                var res = Batch2(baseCfg, configs[c], 200, acts[a]);
                var ag = Aggregate2(res);
                sb.AppendLine(string.Format("行动={0}\t{1}\t胜率 {2:P0}\t回合 {3:F1}\t时长 {4:F0}s\t我方剩血 {5:P0}\t敌方剩血 {6:P0}",
                    acts[a], configs[c].name, ag.winRate, ag.avgRounds, ag.avgDuration, ag.avgPlayerHp, ag.avgEnemyHp));
            }
            sb.AppendLine();
        }

        System.IO.File.WriteAllText("D:/ljjh/tmp/sweep_actions.txt", sb.ToString(), new System.Text.UTF8Encoding(false));
        Debug.Log("[BattleReconcile] 行动次数扫描完成 -> D:/ljjh/tmp/sweep_actions.txt");
    }

    static List<BattleResult> Batch2(BattleConfig baseCfg, Scenario sc, int runs, int actions)
    {
        var results = new List<BattleResult>(runs);
        for (int i = 0; i < runs; i++)
        {
            var cfg = UnityEngine.Object.Instantiate(baseCfg);
            cfg.useExpectedValue = false;
            cfg.playerActionsPerRound = actions;
            try
            {
                var r = RunOne(cfg, sc, 1000 + i);
                if (r != null) results.Add(r.Value);
            }
            finally { UnityEngine.Object.DestroyImmediate(cfg); }
        }
        return results;
    }

    /// <summary>临时诊断：同一队敌人、玩家 24 级 vs 10 级，把双方属性与逐条日志打出来。</summary>
    public static void DebugLevel()
    {
        var sb = new System.Text.StringBuilder();
        string[][] waves = new string[][] {
            new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
            new[] { "en_firetiger", "en_graywolf", "en_bonefish", "en_rat" } };
        int[] levels = { 24, 10 };
        for (int L = 0; L < levels.Length; L++)
        {
            var sc = new Scenario("dbg", waves, 10, levels[L]);
            var cfg = UnityEngine.Object.Instantiate(BattleDatabase.Config);
            cfg.useExpectedValue = false;
            var sim = new BattleSimulator(cfg, 1000);
            for (int i = 0; i < Players.Length; i++)
                sim.AddPlayer(BattleDatabase.GetCreature(Players[i]), sc.playerLevel, sc.freePoints, null);
            int band = cfg.BandIndex(sc.enemyLevel);
            for (int w = 0; w < sc.waves.Length; w++)
            {
                for (int i = 0; i < sc.waves[w].Length; i++)
                    sim.AddEnemy(BattleDatabase.GetCreature(sc.waves[w][i]), 10, band);
                sim.EndWave();
            }
            sim.Begin();
            sim.verbose = true;
            sb.AppendLine("===== 玩家等级 " + levels[L] + " =====");
            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                sb.AppendLine(string.Format("{0}\tL{1}\tHP {2:F0}\tatk {3:F1}\tdef {4:F1}\tspd {5:F1}\t命中基准 {6:F2}",
                    u.displayName, u.level, u.maxHp, u.EffectiveAtk, u.EffectiveDef, u.stats.spd, 0));
            }
            var r = sim.Run(600f);
            sb.AppendLine("结果: " + r.ToString());
            int n = Mathf.Min(40, sim.log.Count);
            for (int i = 0; i < n; i++) sb.AppendLine("  " + sim.log[i]);
            UnityEngine.Object.DestroyImmediate(cfg);
        }
        System.IO.File.WriteAllText("D:/ljjh/tmp/dbg_level.txt", sb.ToString(), new System.Text.UTF8Encoding(false));
        Debug.Log("[BattleReconcile] 等级诊断 -> D:/ljjh/tmp/dbg_level.txt");
    }
    static Aggregate Aggregate2(List<BattleResult> results)
    {
        var ag = new Aggregate();
        if (results.Count == 0) return ag;
        int wins = 0;
        float sumRounds = 0f, sumDur = 0f, sumPhp = 0f, sumEhp = 0f;
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.playerHpPercent > 0f && r.enemyHpPercent <= 0.0001f) wins++;
            sumRounds += r.rounds; sumDur += r.durationSeconds;
            sumPhp += r.playerHpPercent; sumEhp += r.enemyHpPercent;
        }
        ag.runs = results.Count;
        ag.winRate = (float)wins / results.Count;
        ag.avgRounds = sumRounds / results.Count;
        ag.avgDuration = sumDur / results.Count;
        ag.avgPlayerHp = sumPhp / results.Count;
        ag.avgEnemyHp = sumEhp / results.Count;
        return ag;
    }

    static List<BattleResult> Batch(BattleConfig baseCfg, Scenario sc, bool expected, int runs, float aoeScale)
    {
        var results = new List<BattleResult>(runs);
        for (int i = 0; i < runs; i++)
        {
            // BattleConfig 是 ScriptableObject，直接改会污染资源；这里用副本+克隆字段的方式避免。
            var cfg = UnityEngine.Object.Instantiate(baseCfg);
            cfg.useExpectedValue = expected;
            cfg.aoeDamageScale = aoeScale;
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

    /// <summary>技能等级 × 装备 的二维扫描：看「升级」到底把胜率推了多少。</summary>
    [MenuItem("灵境/技能等级与装备扫描")]
    public static void SweepRanks()
    {
        var cfg = BattleDatabase.Config;
        if (cfg == null) { Debug.LogError("[RankSweep] BattleConfig 缺失"); return; }

        var scs = new List<Scenario>
        {
            new Scenario("同级 普通x3 · lv10", W(new[] { "en_bonefish", "en_stonelizard", "en_rat" }), 10, 10),
            new Scenario("同级 精英x2 · lv11", W(new[] { "en_bird", "en_fox" }), 11, 11),
            new Scenario("同级 普2+精2 · lv12", W(new[] { "en_rat", "en_bear", "en_bird", "en_fox" }), 12, 12),
            new Scenario("同级 小Boss · lv16", W(new[] { "en_jelly" }), 16, 16),
            new Scenario("同级 波次 4+3+3 · lv10", W(
                new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
                new[] { "en_bird", "en_bird", "en_rat" },
                new[] { "en_fox", "en_fox", "en_bear" }), 10, 10),
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("场景\t技能等级\t装备\t胜率\t平均回合\t平均时长s\t我方剩血%\t存活");
        foreach (var baseSc in scs)
        {
            for (int rank = 1; rank <= 3; rank++)
            {
                for (int g = 0; g < 2; g++)
                {
                    var sc = new Scenario(baseSc.name, baseSc.waves, baseSc.enemyLevel, baseSc.playerLevel, g == 1);
                    sc.rank = rank;
                    var list = Batch(cfg, sc, false, 100);
                    var a = Agg(list);
                    sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3:F0}%\t{4:F1}\t{5:F0}\t{6:F0}%\t{7:F2}",
                        sc.name, LingJing.Data.SkillRanks.RankLabel(rank), g == 1 ? "满配" : "裸装",
                        a.winRate * 100f, a.avgRounds, a.avgDuration, a.avgPlayerHp * 100f, a.avgAlive));
                }
            }
        }
        System.IO.File.WriteAllText(@"D:\ljjh\tmp\rank_sweep.txt", sb.ToString());
        Debug.Log("[RankSweep]\n" + sb);
    }

    static BattleResult? RunOne(BattleConfig cfg, Scenario sc, int seed)
    {
        LingJing.Data.SkillRanks.forcedRank = sc.rank;   // 0 = 不强制
        var sim = new BattleSimulator(cfg, seed);

        for (int i = 0; i < Players.Length; i++)
        {
            var def = BattleDatabase.GetCreature(Players[i]);
            if (def == null) { Debug.LogError("[BattleReconcile] 缺 " + Players[i]); return null; }
            var equipped = LingJing.Data.SkillLoadout.Suggest(def, sc.playerLevel);   // 按等级自动配满 4 格
            sim.AddPlayer(def, sc.playerLevel, sc.freePoints, sc.geared ? FullGear() : null, equipped);
        }

        int band = cfg.BandIndex(sc.enemyLevel);
        for (int w = 0; w < sc.waves.Length; w++)
        {
            var ids = sc.waves[w];
            for (int i = 0; i < ids.Length; i++)
            {
                var def = BattleDatabase.GetCreature(ids[i]);
                if (def == null) { Debug.LogError("[BattleReconcile] 缺 " + ids[i]); return null; }
                sim.AddEnemy(def, sc.enemyLevel, band);
            }
            sim.EndWave();
        }

        sim.Begin();
        var res = sim.Run();
        LingJing.Data.SkillRanks.forcedRank = 0;
        return res;
    }

    static readonly string[] FullSetIds =
    {
        "eq_wpn", "eq_arm", "eq_trk", "eq_bts", "eq_tot"
    };

    /// <summary>满配：5 个槽位各一件（唯一一套，品质暂不做）。每只兽各一套。</summary>
    static EquipmentLoadout FullGear()
    {
        var lo = new EquipmentLoadout();
        for (int i = 0; i < FullSetIds.Length; i++)
        {
            var eq = BattleDatabase.GetEquipment(FullSetIds[i]);
            if (eq == null) { Debug.LogError("[BattleReconcile] 缺装备 " + FullSetIds[i] + "，先跑 灵境/导入战斗数据。"); continue; }
            lo.Set(eq);
        }
        return lo;
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
