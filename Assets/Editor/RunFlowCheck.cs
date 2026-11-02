// 一境循环的逻辑自检：RunState 存档往返 + 战斗结算写回。
using System.Collections.Generic;
using System.IO;
using LingJing.Battle;
using UnityEditor;
using UnityEngine;

public static class RunFlowCheck
{
    const string Out = "D:/ljjh/tmp/runflow.txt";

    [MenuItem("灵境/一境循环自检")]
    public static void Run()
    {
        var log = new List<string>();
        int pass = 0, fail = 0;
        System.Action<string, bool, string> T = (name, ok, detail) =>
        {
            log.Add(string.Format("{0}\t{1}\t{2}", name, ok ? "PASS" : "FAIL", detail));
            if (ok) pass++; else fail++;
        };

        // ① 新建 → 全队满血、第 1 层
        RunState.Reset();
        T("① 新建探索", RunState.Active && RunState.Floor == 1 && RunState.PartyIds.Count == 3 && Mathf.Approximately(RunState.HpPct[0], 1f), "floor=" + RunState.Floor);

        // ② 存档往返（模拟跨场景）
        RunState.HpPct[0] = 0.42f;
        RunState.HpPct[1] = 0.73f;
        RunState.Save();
        RunState.Load();
        T("② 存档往返", Mathf.Abs(RunState.HpPct[0] - 0.42f) < 0.01f && Mathf.Abs(RunState.HpPct[1] - 0.73f) < 0.01f, "hp=" + RunState.HpPct[0].ToString("F2") + "/" + RunState.HpPct[1].ToString("F2"));

        // ③ 战斗结算写回：血量按百分比、掉落累加、赢了层数 +1
        var cfg = BattleDatabase.Config;
        var sim = new BattleSimulator(cfg);
        var a = sim.AddPlayer(BattleDatabase.GetCreature("beast_water"), 24, new[] { 23, 23, 23, 23, 23, 23 });
        var b = sim.AddPlayer(BattleDatabase.GetCreature("beast_fire"), 24, new[] { 23, 23, 23, 23, 23, 23 });
        var c = sim.AddPlayer(BattleDatabase.GetCreature("beast_wind"), 24, new[] { 23, 23, 23, 23, 23, 23 });
        a.hp = a.maxHp * 0.5f;
        int floorBefore = RunState.Floor;
        RunState.Capture(new List<BattleUnit> { a, b, c }, true, new List<string> { "eq_wpn" });
        bool hpOk = Mathf.Abs(RunState.HpPct[0] - 0.5f) < 0.02f;
        T("③ 结算写回", hpOk && RunState.Floor == floorBefore + 1 && RunState.Loot.Count == 1,
            "hp=" + RunState.HpPct[0].ToString("F2") + " floor=" + RunState.Floor + " loot=" + RunState.Loot.Count);

        // ④ 休整节点：全队 +30%，层数 +1
        RunState.HpPct[0] = 0.2f;
        int f2 = RunState.Floor;
        RunState.Rest();
        T("④ 休整回血", Mathf.Abs(RunState.HpPct[0] - 0.5f) < 0.01f && RunState.Floor == f2 + 1, "hp=" + RunState.HpPct[0].ToString("F2"));

        // ⑤ 结束探索后不再 Active
        RunState.End();
        RunState.Load();
        T("⑤ 结束探索", !RunState.Active, "active=" + RunState.Active);

        log.Add(string.Format("通过 {0} / 失败 {1} / 共 {2}", pass, fail, pass + fail));
        File.WriteAllText(Out, string.Join("\n", log.ToArray()));
        foreach (var l in log) Debug.Log("[RunFlowCheck] " + l);
    }
}