// 试玩反馈三点的自动核对：① 全体技能要打到所有敌人 ② 辅助技能能给友方 ③ 护盾有数值
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using LingJing.Battle;
using LingJing.Data;

public static class PlaytestFixCheck
{
    [MenuItem("灵境/试玩反馈自检")]
    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        var cfg = Object.Instantiate(BattleDatabase.Config);

        // ---- ① 全体技能 ----
        var sim1 = new BattleSimulator(cfg, 1);
        for (int i = 0; i < 3; i++) sim1.AddPlayer(BattleDatabase.GetCreature(i == 0 ? "beast_wind" : (i == 1 ? "beast_fire" : "beast_water")), 24, new[] { 23, 23, 23, 23, 23, 23 });
        sim1.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
        sim1.AddEnemy(BattleDatabase.GetCreature("en_stonelizard"), 10, 1);
        sim1.AddEnemy(BattleDatabase.GetCreature("en_bird"), 10, 1);
        sim1.Begin();
        var aoe = BattleDatabase.GetSkill("a_ult");       // 天风回响：全体
        var before = new List<float>();
        for (int i = 0; i < sim1.units.Count; i++) if (sim1.units[i].side == Side.Enemy) before.Add(sim1.units[i].hp);
        sim1.awaitingPlayer = sim1.units[0];              // 直接指定施法者，跳过节奏
        sim1.ResolvePlayerTurn(aoe, sim1.units[3]);       // 故意带一个点击目标
        int hit = 0;
        for (int i = 0, k = 0; i < sim1.units.Count; i++)
            if (sim1.units[i].side == Side.Enemy) { if (sim1.units[i].hp < before[k]) hit++; k++; }
        log.AppendLine(string.Format("① 全体技能命中 {0}/3 个敌人  {1}", hit, hit == 3 ? "PASS" : "FAIL"));

        // ---- ② 辅助技能给友方 ----
        var sim2 = new BattleSimulator(cfg, 2);
        for (int i = 0; i < 3; i++) sim2.AddPlayer(BattleDatabase.GetCreature(i == 0 ? "beast_earth" : (i == 1 ? "beast_fire" : "beast_wind")), 24, new[] { 23, 23, 23, 23, 23, 23 });
        sim2.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
        sim2.Begin();
        var buff = BattleDatabase.GetSkill("e_support");   // 磐石：护盾 + 嘲讽（Self）
        var ally = BattleDatabase.GetSkill("f_support");   // 灼心：攻击 +25%（Ally）
        var target = sim2.units[1];                        // 火灵打火灵的灼心给风灵
        sim2.awaitingPlayer = sim2.units[1];
        float shieldBefore = sim2.units[2].shield;
        sim2.ResolvePlayerTurn(ally, sim2.units[2]);
        float allyAtkBuff = sim2.units[2].atkBuff;
        int allyBuffRounds = sim2.units[2].atkBuffRounds;
        log.AppendLine(string.Format("② 辅助给友方：风灵 atkBuff={0} 持续{1}回合（施法者是火灵，被加的是风灵）  {2}",
            allyAtkBuff.ToString("0.00"), allyBuffRounds,
            Mathf.Abs(allyAtkBuff - 0.25f) < 0.001f && allyBuffRounds > 0 ? "PASS" : "FAIL"));

        // ---- ③ 护盾有数值 ----
        sim2.awaitingPlayer = sim2.units[0];
        sim2.ResolvePlayerTurn(buff, sim2.units[0]);       // 磐石给土灵自己上盾
        log.AppendLine(string.Format("③ 护盾值：土灵 shield={0:0} （最大生命 {1:0}）  {2}",
            sim2.units[0].shield, sim2.units[0].maxHp, sim2.units[0].shield > 1f ? "PASS" : "FAIL"));

        Object.DestroyImmediate(cfg);
        System.IO.File.WriteAllText("D:/ljjh/tmp/playtest_fix.txt", log.ToString());
        Debug.Log(log.ToString());
    }
}