using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using LingJing.Battle;
using LingJing.Data;

namespace LingJing.EditorTools
{
    /// <summary>
    /// 遭遇系统批测：按一境规则生成 N 场遭遇，统计只数 / 精英 / 波次分布，
    /// 再把每一场真的打一遍（无头、期望值），看胜率与平均回合。
    /// 菜单：灵境/遭遇批测
    /// </summary>
    public static class EncounterReport
    {
        const string OutPath = "D:/ljjh/tmp/encounter.txt";

        [MenuItem("灵境/遭遇批测 (200 场)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            var cfg = BattleDatabase.Config;
            if (cfg == null) { Debug.LogError("[EncounterReport] 没有 BattleConfig"); return; }

            const int samples = 200;
            int[] sizeHist = new int[11];
            int[] waveHist = new int[4];
            int[] eliteHist = new int[3];
            int[] levelHist = new int[25];
            int totalElite = 0, totalCount = 0;

            var rng = new System.Random(20261101);
            var encounters = new System.Collections.Generic.List<Encounter>();
            for (int i = 0; i < samples; i++)
            {
                // 24 级玩家、band = 2（13 级以上），最贴近当前 v0.1 的满配状态
                var enc = EncounterGenerator.Roll(24, 2, rng);
                encounters.Add(enc);
                sizeHist[Mathf.Clamp(enc.TotalCount, 0, 10)]++;
                waveHist[Mathf.Clamp(enc.waves.Count, 0, 3)]++;
                eliteHist[Mathf.Clamp(enc.eliteCount, 0, 2)]++;
                levelHist[Mathf.Clamp(enc.level, 0, 24)]++;
                totalElite += enc.eliteCount;
                totalCount += enc.TotalCount;
            }

            sb.AppendLine("# 一境遭遇生成 · 200 场（玩家 24 级，band=2）");
            sb.AppendLine("平均只数\t" + (totalCount / (float)samples).ToString("0.00"));
            sb.AppendLine("平均精英\t" + (totalElite / (float)samples).ToString("0.00"));
            sb.AppendLine();
            sb.AppendLine("# 总只数分布");
            for (int i = 1; i <= 10; i++) if (sizeHist[i] > 0) sb.AppendLine(i + " 只\t" + sizeHist[i]);
            sb.AppendLine();
            sb.AppendLine("# 波数分布");
            for (int i = 1; i <= 3; i++) if (waveHist[i] > 0) sb.AppendLine(i + " 波\t" + waveHist[i]);
            sb.AppendLine();
            sb.AppendLine("# 精英数分布");
            for (int i = 0; i <= 2; i++) if (eliteHist[i] > 0) sb.AppendLine(i + " 精英\t" + eliteHist[i]);
            sb.AppendLine();
            sb.AppendLine("# 等级分布");
            for (int i = 1; i <= 24; i++) if (levelHist[i] > 0) sb.AppendLine("lv" + i + "\t" + levelHist[i]);
            sb.AppendLine();

            // 前 10 场原样打印，肉眼校验格式
            sb.AppendLine("# 前 10 场");
            for (int i = 0; i < 10; i++) sb.AppendLine(EncounterGenerator.Describe(encounters[i]));
            sb.AppendLine();

            // 真打一遍：每场都跑，看胜率
            sb.AppendLine("# 实战（期望值模式 · 我方 24 级水/火/风 + 满配装备）");
            int wins = 0, sumRounds = 0;
            for (int i = 0; i < encounters.Count; i++)
            {
                var sim = new BattleSimulator(cfg);
                for (int p = 0; p < 3; p++) sim.AddPlayer(BattleDatabase.GetCreature(Players[p]), 24, Free, FullSet());
                for (int w = 0; w < encounters[i].waves.Count; w++)
                {
                    int band = cfg.BandIndex(encounters[i].level);
                    for (int k = 0; k < encounters[i].waves[w].Length; k++)
                    {
                        var def = BattleDatabase.GetCreature(encounters[i].waves[w][k]);
                        if (def != null) sim.AddEnemy(def, encounters[i].level, band);
                    }
                    sim.EndWave();
                }
                sim.Begin();
                var r = sim.Run(600f);
                if (r.playersAlive > 0 && !r.timedOut) wins++;
                sumRounds += r.rounds;
            }
            sb.AppendLine("胜率\t" + (wins * 100f / encounters.Count).ToString("0.0") + "%");
            sb.AppendLine("平均回合\t" + (sumRounds / (float)encounters.Count).ToString("0.00"));

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[EncounterReport] 写好：" + OutPath);
        }

        static readonly string[] Players = { "beast_water", "beast_fire", "beast_wind" };
        static readonly int[] Free = { 23, 23, 23, 23, 23, 23 };

        static EquipmentLoadout FullSet()
        {
            var gear = new EquipmentLoadout();
            string[] ids = { "eq_wpn", "eq_arm", "eq_trk", "eq_bts", "eq_tot" };
            for (int i = 0; i < ids.Length; i++)
            {
                var eq = BattleDatabase.GetEquipment(ids[i]);
                if (eq != null) gear.Set(eq);
            }
            return gear;
        }
    }
}
