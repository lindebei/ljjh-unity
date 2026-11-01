using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using LingJing.Battle;
using LingJing.Data;

namespace LingJing.EditorTools
{
    /// <summary>
    /// 品质强度扫描：每兽独立蓝条上线后，玩家技能释放次数比以前多了一大截，
    /// 之前那组 qualityPowerScale（1.0 | 2.15 | 2.30 | 2.70 | 3.10）已经偏软。
    /// 这里固定场景、只动精英/小 Boss 两个系数，扫出一张「系数 → 胜率」表，
    /// 目标是用户定的口径：精英无装备 70–80%、Boss 无装备 40% 左右。
    /// 菜单：灵境/品质强度扫描
    /// </summary>
    public static class QualitySweep
    {
        const string OutPath = "D:/ljjh/tmp/quality_sweep.txt";
        const int Runs = 80;

        static readonly string[] Players = { "beast_water", "beast_fire", "beast_wind" };
        // 自由点：1–24 级每级 6 点、六项各 1，所以到 level 级每项恰好 level-1 点。
        // 之前这里写死 23（= 24 级的点数），同级 11 级场景等于白送 130 多点，胜率自然全是 100%。
        static int[] FreeFor(int level)
        {
            int v = Mathf.Max(0, level - 1);
            return new[] { v, v, v, v, v, v };
        }

        [MenuItem("灵境/品质强度扫描 (精英/小Boss)")]
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 品质血量扫描 · 每场景 " + Runs + " 场随机 · 同级 · 无装备 · 攻系数固定 2.15/2.30");
            sb.AppendLine("# 精英系数\taverage 精英x2 lv11 胜率\t小Boss系数\t小Boss lv16 胜率");

            var cfgBase = BattleDatabase.Config;
            if (cfgBase == null) { Debug.LogError("[QualitySweep] 没有 BattleConfig"); return; }

            var elites = new[] { 2.15f, 3f, 4f, 5f, 6f };
            var bosses = new[] { 2.3f, 4f, 6f, 8f, 10f };

            var scElite = new[] { new[] { "en_bird", "en_fox" } };
            int eliteLevel = 11;
            var scBoss = new[] { new[] { "en_jelly" } };
            int bossLevel = 16;

            foreach (var e in elites)
            {
                var cfg = Object.Instantiate(cfgBase);
                var atk = (float[])cfg.qualityAtkScale.Clone();
                atk[(int)Quality.Elite] = e;
                cfg.qualityAtkScale = atk;
                float wr = WinRate(cfg, scElite, eliteLevel);
                Object.DestroyImmediate(cfg);
                sb.AppendLine("elite " + e.ToString("0.00") + "\t" + wr.ToString("0.0") + "%");
                Debug.Log("[QualitySweep] elite " + e + " → " + wr.ToString("0.0") + "%");
            }

            foreach (var b in bosses)
            {
                var cfg = Object.Instantiate(cfgBase);
                var atk = (float[])cfg.qualityAtkScale.Clone();
                atk[(int)Quality.MiniBoss] = b;
                cfg.qualityAtkScale = atk;
                float wr = WinRate(cfg, scBoss, bossLevel);
                Object.DestroyImmediate(cfg);
                sb.AppendLine("miniboss " + b.ToString("0.00") + "\t" + wr.ToString("0.0") + "%");
                Debug.Log("[QualitySweep] miniboss " + b + " → " + wr.ToString("0.0") + "%");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[QualitySweep] 写好：" + OutPath);
        }

        static float WinRate(BattleConfig cfg, string[][] waves, int level)
        {
            int wins = 0;
            for (int i = 0; i < Runs; i++)
            {
                var sim = new BattleSimulator(cfg, 5000 + i);
                for (int p = 0; p < Players.Length; p++)
                    sim.AddPlayer(BattleDatabase.GetCreature(Players[p]), level, FreeFor(level), null);

                int band = cfg.BandIndex(level);
                for (int w = 0; w < waves.Length; w++)
                {
                    for (int k = 0; k < waves[w].Length; k++)
                    {
                        var def = BattleDatabase.GetCreature(waves[w][k]);
                        if (def != null) sim.AddEnemy(def, level, band);
                    }
                    sim.EndWave();
                }
                sim.Begin();
                var r = sim.Run(600f);
                if (r.playersAlive > 0 && !r.timedOut) wins++;
            }
            return wins * 100f / Runs;
        }
    }
}
