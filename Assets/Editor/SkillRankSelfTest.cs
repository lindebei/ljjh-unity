// 技能等级自检：打印有效数值 + 打一场看倍率是否真的生效（菜单「灵境/技能等级自检」）。
using System.Collections.Generic;
using System.Text;
using LingJing.Battle;
using LingJing.Data;
using UnityEditor;
using UnityEngine;

public static class SkillRankSelfTest
{
    [MenuItem("灵境/技能等级自检")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var sk = BattleDatabase.GetSkill("a_t4");
        if (sk == null) { Debug.LogError("[RankTest] 找不到 a_t4"); return; }

        int oldRank = SkillRanks.Get(sk.id);
        for (int r = 1; r <= 3; r++)
        {
            SkillRanks.Set(sk.id, r);
            sb.AppendLine(string.Format("{0} 等级 {1}: 倍率x{2:F2} 蓝耗 {3} 冷却 {4}",
                sk.displayName, SkillRanks.RankLabel(r), SkillRanks.MultiplierMul(sk), SkillRanks.Cost(sk), SkillRanks.Cooldown(sk)));
        }

        // 实际打一场，看平均回合数是否随等级变化
        var cfg = BattleDatabase.Config;
        var def = BattleDatabase.GetCreature("beast_wind");
        for (int r = 1; r <= 3; r++)
        {
            SkillRanks.Set(sk.id, r);
            float rounds = 0f; int runs = 12; int win = 0;
            for (int i = 0; i < runs; i++)
            {
                var c = Object.Instantiate(cfg);
                var sim = new BattleSimulator(c, 1000 + i);
                var eq = new SkillLoadoutBuilder(def, 12).Build();
                sim.AddPlayer(def, 12, new int[6], null, eq);
                sim.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 12, 2);
                sim.AddEnemy(BattleDatabase.GetCreature("en_rat"), 12, 2);
                sim.EndWave();
                sim.Begin();
                var res = sim.Run(600f);
                rounds += res.rounds;
                if (res.playersAlive > 0 && !res.timedOut) win++;
                Object.DestroyImmediate(c);
            }
            sb.AppendLine(string.Format("等级 {0}: 平均 {1:F1} 回合, 胜 {2}/{3}", SkillRanks.RankLabel(r), rounds / runs, win, runs));
        }

        SkillRanks.Set(sk.id, oldRank);
        System.IO.File.WriteAllText(@"D:\ljjh\tmp\rank_test.txt", sb.ToString());
        Debug.Log("[RankTest]\n" + sb);
    }

    // 一个临时小工具：按野兽 12 级能带的 4 格
    class SkillLoadoutBuilder
    {
        CreatureDefinition def; int level;
        public SkillLoadoutBuilder(CreatureDefinition d, int lv) { def = d; level = lv; }
        public SkillDefinition[] Build()
        {
            var pool = def.skillPool;
            var res = new List<SkillDefinition>();
            for (int i = 0; i < pool.Length && res.Count < 4; i++)
                if (pool[i] != null && pool[i].tier > 1 && pool[i].unlockLevel <= level) res.Add(pool[i]);
            return res.ToArray();
        }
    }
}