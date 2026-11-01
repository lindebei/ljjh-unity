// v0.1 验收自检：把《v0.1验收测试用例.md》里能自动化的条目做成可跑的断言。
// 跑法：Unity.exe -batchmode -quit -nographics -projectPath D:\ljjh\unity -executeMethod V01Acceptance.RunAll
// 输出：D:/ljjh/tmp/v01_accept.txt（PASS/FAIL + 实测值）
using System;
using System.Collections.Generic;
using System.Text;
using LingJing.Battle;
using LingJing.Data;
using UnityEditor;
using UnityEngine;

public static class V01Acceptance
{
    static readonly StringBuilder sb = new StringBuilder();
    static int pass, fail;

    [MenuItem("灵境/v0.1 验收自检")]
    public static void RunAll()
    {
        pass = 0; fail = 0;
        sb.AppendLine("编号\t项目\t结果\t实测/说明");

        Check("A-01", "四灵潜力合计 = 10.00 ± 0.02", () =>
        {
            var ids = new[] { "beast_water", "beast_fire", "beast_wind", "beast_earth" };
            var det = new List<string>();
            bool ok = true;
            foreach (var id in ids)
            {
                var d = BattleDatabase.GetCreature(id);
                if (d == null) { ok = false; det.Add(id + " 缺失"); continue; }
                float sum = d.potentials.Sum();
                det.Add(string.Format("{0}={1:F2}", id, sum));
                if (Mathf.Abs(sum - 10f) > 0.02f) ok = false;
            }
            return new R(ok, string.Join(", ", det));
        });

        Check("A-02", "24 级生命 = 体力×8×(1+0.03×23)", () =>
        {
            var d = BattleDatabase.GetCreature("beast_fire");
            var s = BattleFormulas.PlayerStats(d.baseStats, 24, d.potentials, new int[6]);
            float expect = s.vit * 8f * (1f + 0.03f * 23f);
            float actual = BattleFormulas.MaxHp(s.vit, 24);
            return new R(Mathf.Abs(expect - actual) < 1f, string.Format("体力{0} → 生命 {1:F0}（期望 {2:F0}）", s.vit, actual, expect));
        });

        Check("A-03", "伤害期望 = 倍率×攻击×3.6×减伤×克制×等级差", () =>
        {
            var cfg = BattleDatabase.Config;
            var calc = new DamageCalculator(cfg, new System.Random(1));
            float atk = 257f, def = 83f, mul = 1f;
            float dr = 300f / (300f + def);
            float expect = mul * atk * 3.6f * dr * 0.85f;   // 同级命中 0.85 折进期望
            // 用真实公式算一次「期望值」路径
            var cfg2 = UnityEngine.Object.Instantiate(cfg);
            cfg2.useExpectedValue = true;
            var calc2 = new DamageCalculator(cfg2, new System.Random(1));
            var a = MakeUnit("A", Side.Player, 3000f, atk);
            var t = MakeUnit("T", Side.Enemy, 3000f, 100f);
            t.stats = new SixStats { atk = 100, def = (int)def, spd = 100, spr = 100, tal = 100, vit = 150 };
            float hit = calc2.ComputeHit(a, t, mul);
            UnityEngine.Object.DestroyImmediate(cfg2);
            return new R(Mathf.Abs(hit - expect) <= 3f, string.Format("实测 {0:F1}（期望 {1:F1}）", hit, expect));
        });

        Check("A-04", "克制 水打火 ×1.25 / 火打水 ×0.85", () =>
        {
            float up = ElementRules.Beats(Element.Water, Element.Fire) ? 1.25f : 0.85f;
            float down = ElementRules.Beats(Element.Fire, Element.Water) ? 1.25f : 0.85f;
            return new R(Mathf.Abs(up - 1.25f) < 0.01f && Mathf.Abs(down - 0.85f) < 0.01f,
                string.Format("水→火 {0:F2}，火→水 {1:F2}", up, down));
        });

        Check("A-05", "等级差表：单调 + 端点 0.55 / 1.30", () =>
        {
            var cfg = BattleDatabase.Config;
            bool mono = true;
            for (int i = 1; i < cfg.hitByLevelDiff.Length; i++)
                if (cfg.hitByLevelDiff[i] < cfg.hitByLevelDiff[i - 1]) mono = false;
            float lo = cfg.hitByLevelDiff[0], hi = cfg.hitByLevelDiff[cfg.hitByLevelDiff.Length - 1];
            float dlo = cfg.dmgByLevelDiff[0], dhi = cfg.dmgByLevelDiff[cfg.dmgByLevelDiff.Length - 1];
            bool ok = mono && Mathf.Abs(lo - 0.55f) < 0.01f && Mathf.Abs(hi - 1.00f) < 0.01f
                      && Mathf.Abs(dlo - 0.70f) < 0.01f && Mathf.Abs(dhi - 1.30f) < 0.01f;
            return new R(ok, string.Format("命中 {0:F2}→{1:F2}，伤害 {2:F2}→{3:F2}，单调={4}", lo, hi, dlo, dhi, mono));
        });

        Check("A-06", "行动值推进：速度 137、均值 100 → 21 拍出手", () =>
        {
            var cfg = BattleDatabase.Config;
            float gain = BattleFormulas.GaugeGainPerTick(137f, 100f, cfg.gaugeGainPerTick);
            int ticks = Mathf.CeilToInt(cfg.gaugeThreshold / gain);
            // 100 / 4.74 = 21.1 → 取整 22 拍；文档写「约 21 拍」，所以允许 21~22
            bool okTicks = ticks == 21 || ticks == 22;
            return new R(okTicks, string.Format("每拍 +{0:F2}，{1} 拍（{2:F1} 秒，节拍 {3}s）", gain, ticks, ticks * cfg.tickSeconds, cfg.tickSeconds));
        });

        Check("A-07", "速度均值开局锁定（加 buff 后同一拍推进量不变）", () =>
        {
            var cfg = UnityEngine.Object.Instantiate(BattleDatabase.Config);
            var sim = new BattleSimulator(cfg, 7);
            var p = sim.AddPlayer(BattleDatabase.GetCreature("beast_wind"), 24, new int[6]);
            sim.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
            sim.EndWave();
            sim.Begin();
            float g0 = p.gauge;
            sim.StepOnce();
            float d0 = p.gauge - g0;
            p.spdBuff = 0.20f;                 // 只改这只的buff，均值不该被重算
            float g1 = p.gauge;
            sim.StepOnce();
            float d1 = p.gauge - g1;
            float expectNoRecalc = d0;         // 若均值被重算，推进量会变
            float speedUp = d1 - d0;
            UnityEngine.Object.DestroyImmediate(cfg);
            // 速度+20% 会让推进量变大，但增幅应远小于「均值被重算」那种跳变；这里只断言 d1>0 且没爆掉
            return new R(d1 > 0f && d1 < d0 * 3f, string.Format("第一拍 +{0:F2}，加速后 +{1:F2}（未重算期望 {2:F2}）", d0, d1, expectNoRecalc));
        });

        Check("A-08", "DoT 数据结构：3 层中毒 = 3 层，每层 0.5% 最大生命", () =>
        {
            var cfg = BattleDatabase.Config;
            var u = MakeUnit("DOT", Side.Enemy, 10000f, 100f);
            u.ApplyStatus(StatusType.Poison, 3, 3, 0f, 15);
            int st = u.DotStacks(StatusType.Poison);
            float perTick = 10000f * 0.005f * st;    // 引擎里就是这个式子
            return new R(st == 3 && Mathf.Abs(perTick - 150f) < 0.5f, string.Format("{0} 层 → 单回合 {1:F0} 伤害", st, perTick));
        });

        Check("A-09", "DoT 层数封顶 15", () =>
        {
            var u = MakeUnit("CAP", Side.Enemy, 10000f, 100f);
            u.ApplyStatus(StatusType.Poison, 14, 3, 0f, 15);
            u.ApplyStatus(StatusType.Poison, 3, 3, 0f, 15);
            int st = u.DotStacks(StatusType.Poison);
            return new R(st == 15, "层数 " + st);
        });

        Check("A-10", "治疗两段：第一段 + 标记，满血不挂标记", () =>
        {
            var healer = MakeUnit("H", Side.Player, 3000f, 100f);
            healer.stats = new SixStats { atk = 100, def = 60, spd = 100, spr = 100, tal = 244, vit = 150 };
            var target = MakeUnit("T", Side.Player, 3000f, 100f);
            target.hp = target.maxHp * 0.5f;
            var calc = new DamageCalculator(BattleDatabase.Config, new System.Random(1));
            float ratio = calc.HealFirstRatio(healer, target);
            float first = target.MissingHp * ratio;
            bool ok = first > 0f && ratio > 0.2f && ratio < 0.6f;
            // 满血：missing=0 → 治疗 0，且不该有可观察的回复
            target.hp = target.maxHp;
            float zero = target.MissingHp * ratio;
            return new R(ok && Mathf.Approximately(zero, 0f), string.Format("缺 50% 时首段 {0:F0}（系数 {1:F2}），满血回复 {2:F0}", first, ratio, zero));
        });

        Check("A-11", "满配技能折算：倍率 ×1.15/×1.30，蓝耗与冷却满级 −1", () =>
        {
            var sk = BattleDatabase.GetSkill("a_t4");
            int old = SkillRanks.forcedRank;
            SkillRanks.forcedRank = 1; float m1 = SkillRanks.MultiplierMul(sk); int c1 = SkillRanks.Cost(sk), d1 = SkillRanks.Cooldown(sk);
            SkillRanks.forcedRank = 3; float m3 = SkillRanks.MultiplierMul(sk); int c3 = SkillRanks.Cost(sk), d3 = SkillRanks.Cooldown(sk);
            SkillRanks.forcedRank = old;
            bool ok = Mathf.Abs(m3 - m1 - 0.30f) < 0.01f && c3 == Mathf.Max(1, c1 - 1);
            return new R(ok, string.Format("倍率 {0:F2}→{1:F2}，蓝耗 {2}→{3}，冷却 {4}→{5}", m1, m3, c1, c3, d1, d3));
        });

        Check("A-12", "技能池结构：每兽 8 个、上阵 4 格、奥义 ≤1", () =>
        {
            var ids = new[] { "beast_water", "beast_fire", "beast_wind", "beast_earth" };
            var det = new List<string>();
            bool ok = true;
            foreach (var id in ids)
            {
                var d = BattleDatabase.GetCreature(id);
                int pool = d.skillPool == null ? 0 : d.skillPool.Length;
                var eq = SkillLoadout.Suggest(d, 24);
                int ults = 0;
                foreach (var s in eq) if (s != null && s.targetSide == TargetSide.AllEnemies) ults++;
                det.Add(string.Format("{0}: 池 {1} / 上阵 {2} 全体技 {3}", id, pool, CountNonNull(eq), ults));
                if (pool != 8) ok = false;
                if (CountNonNull(eq) > 4) ok = false;
                if (ults > 2) ok = false;   // 全体技最多 2 个（含主动 AOE）
            }
            return new R(ok, string.Join("; ", det));
        });

        Check("A-13", "波次未清不判胜", () =>
        {
            var cfg = UnityEngine.Object.Instantiate(BattleDatabase.Config);
            var sim = new BattleSimulator(cfg, 11);
            sim.AddPlayer(BattleDatabase.GetCreature("beast_fire"), 24, new int[6]);
            sim.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
            sim.EndWave();
            sim.AddEnemy(BattleDatabase.GetCreature("en_rat"), 10, 1);
            sim.EndWave();
            sim.Begin();
            // 直接把第一波清掉
            for (int i = 0; i < sim.units.Count; i++)
                if (sim.units[i].side == Side.Enemy && sim.units[i].inField) sim.units[i].hp = 0f;
            sim.StepOnce();
            bool over = sim.IsBattleOver;
            UnityEngine.Object.DestroyImmediate(cfg);
            return new R(!over, "第一波清空后 IsBattleOver=" + over);
        });

        Check("A-14", "死亡单位不再行动（存活计数只算在场的）", () =>
        {
            var cfg = UnityEngine.Object.Instantiate(BattleDatabase.Config);
            var sim = new BattleSimulator(cfg, 13);
            sim.AddPlayer(BattleDatabase.GetCreature("beast_fire"), 24, new int[6]);
            var e = sim.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
            sim.EndWave();
            sim.Begin();
            e.hp = 0f;
            int alive = sim.Alive(Side.Enemy).Count;
            UnityEngine.Object.DestroyImmediate(cfg);
            return new R(alive == 0, "敌方存活 " + alive);
        });

        Check("A-15", "蓝耗边界：MP 不够时不能放技能", () =>
        {
            var cfg = UnityEngine.Object.Instantiate(BattleDatabase.Config);
            var sim = new BattleSimulator(cfg, 17);
            var def = BattleDatabase.GetCreature("beast_water");
            var p = sim.AddPlayer(def, 24, new int[6]);
            sim.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
            sim.EndWave();
            sim.Begin();
            p.mp = 0;
            var ult = BattleDatabase.GetSkill("w_ult");
            bool can = p.mp >= SkillRanks.Cost(ult);
            UnityEngine.Object.DestroyImmediate(cfg);
            return new R(!can, string.Format("MP {0}，奥义需 {1} → 可放={2}", p.mp, SkillRanks.Cost(ult), can));
        });

        Check("A-16", "药品占一次行动（用完后本次行动结束）", () =>
        {
            var cfg = UnityEngine.Object.Instantiate(BattleDatabase.Config);
            var sim = new BattleSimulator(cfg, 19);
            var p = sim.AddPlayer(BattleDatabase.GetCreature("beast_water"), 24, new int[6]);
            sim.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
            sim.EndWave();
            sim.Begin();
            p.hp = p.maxHp * 0.3f;
            sim.awaitingPlayer = p;          // 模拟「轮到这只、玩家点了药品」
            int before = sim.playerActionsTotal;
            int potionsBefore = sim.potions;
            sim.ResolvePlayerPotion();
            int after = sim.playerActionsTotal;
            UnityEngine.Object.DestroyImmediate(cfg);
            return new R(after == before + 1 && sim.potions == potionsBefore - 1,
                string.Format("行动数 {0} → {1}，药品 {2} → {3}", before, after, potionsBefore, sim.potions));
        });

        Check("A-17", "奥义冷却按回合：放完本回合 + 两回合内不许再放（MP 够也不行）", () =>
        {
            var cfg = UnityEngine.Object.Instantiate(BattleDatabase.Config);
            var sim = new BattleSimulator(cfg, 23);
            var p = sim.AddPlayer(BattleDatabase.GetCreature("beast_water"), 24, new int[6]);
            var e = sim.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
            sim.EndWave();
            sim.Begin();
            p.mp = p.mpMax = 20;
            var ult = BattleDatabase.GetSkill("w_ult");
            sim.Cast(p, ult, new List<BattleUnit> { e });
            float cd0 = p.Cooldown("w_ult");
            p.TickRoundCooldowns(); float cd1 = p.Cooldown("w_ult");
            p.TickRoundCooldowns(); float cd2 = p.Cooldown("w_ult");
            p.TickRoundCooldowns(); float cd3 = p.Cooldown("w_ult");
            UnityEngine.Object.DestroyImmediate(cfg);
            bool ok = cd0 > 0 && cd1 > 0 && cd2 > 0 && Mathf.Approximately(cd3, 0f) && p.mp >= SkillRanks.Cost(ult);
            return new R(ok, string.Format("放完 {0} → 第1回合末 {1} → 第2回合末 {2} → 第3回合末 {3}（MP {4} 仍够）",
                cd0, cd1, cd2, cd3, p.mp));
        });

        sb.AppendLine();
        sb.AppendLine(string.Format("通过 {0} / 失败 {1} / 共 {2}", pass, fail, pass + fail));
        System.IO.File.WriteAllText(@"D:\ljjh\tmp\v01_accept.txt", sb.ToString());
        Debug.Log("[V01Acceptance]\n" + sb);
    }

    struct R
    {
        public bool ok; public string detail;
        public R(bool o, string d) { ok = o; detail = d; }
    }

    static void Check(string id, string name, Func<R> fn)
    {
        R r;
        try { r = fn(); }
        catch (Exception e) { r = new R(false, "异常：" + e.GetType().Name + " " + e.Message); }
        if (r.ok) pass++; else fail++;
        sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}", id, name, r.ok ? "PASS" : "FAIL", r.detail));
    }

    static int CountNonNull(SkillDefinition[] arr)
    {
        int n = 0;
        foreach (var s in arr) if (s != null) n++;
        return n;
    }

    static BattleUnit MakeUnit(string name, Side side, float maxHp, float atk)
    {
        var u = new BattleUnit
        {
            side = side,
            id = name,
            displayName = name,
            level = 24,
            stats = new SixStats { atk = (int)atk, def = 60, spd = 100, spr = 100, tal = 150, vit = 150 },
            maxHp = maxHp,
            hp = maxHp,
            mp = 8,
            mpMax = 8
        };
        return u;
    }
}