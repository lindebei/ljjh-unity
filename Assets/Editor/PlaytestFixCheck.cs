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

        // ---- ④ 辅助技能按效果分「己方 / 敌方」----
        var debuff = ScriptableObject.CreateInstance<SkillDefinition>();
        debuff.id = "test_debuff"; debuff.displayName = "测试压制";
        debuff.type = SkillType.Support;
        debuff.targetSide = TargetSide.Ally;              // 数据故意写错：说是给友方
        debuff.appliedStatus = StatusType.HealDown;       // 实际是给敌人挂「减疗」
        debuff.dotStacks = 1; debuff.multiplier = 0f;
        debuff.spiritCost = 1; debuff.cooldown = 0;
        var effSide = SkillTargeting.EffectiveSide(debuff);
        bool isHostile = SkillTargeting.IsHostileEffect(debuff);
        bool isFriendly = SkillTargeting.IsFriendlyEffect(debuff);
        log.AppendLine(string.Format("④ 辅助判定：写的是 {0}，实际效果 敌对={1}/友方={2} → 运行时按 {3}  {4}",
            debuff.targetSide, isHostile, isFriendly, effSide,
            (effSide == TargetSide.Enemy && isHostile && !isFriendly) ? "PASS" : "FAIL"));

        var buff2 = BattleDatabase.GetSkill("f_support");
        log.AppendLine(string.Format("⑤ 灼心（攻击 buff）→ 按 {0}  {1}",
            SkillTargeting.EffectiveSide(buff2),
            SkillTargeting.EffectiveSide(buff2) == TargetSide.Ally ? "PASS" : "FAIL"));
        var shield2 = BattleDatabase.GetSkill("e_support");
        log.AppendLine(string.Format("⑥ 磐石（自盾+嘲讽）→ 按 {0}  {1}",
            SkillTargeting.EffectiveSide(shield2),
            SkillTargeting.EffectiveSide(shield2) == TargetSide.Self ? "PASS" : "FAIL"));
        // ---- ⑦ 减益辅助：潮噬（HealDown）打到敌人 ----
        var sim3 = new BattleSimulator(cfg, 7);
        for (int i = 0; i < 3; i++) sim3.AddPlayer(BattleDatabase.GetCreature(i == 0 ? "beast_water" : (i == 1 ? "beast_fire" : "beast_wind")), 24, new[] { 23, 23, 23, 23, 23, 23 });
        sim3.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
        sim3.Begin();
        var debuffSkill = BattleDatabase.GetSkill("w_debuff");
        sim3.awaitingPlayer = sim3.units[0];
        sim3.ResolvePlayerTurn(debuffSkill, sim3.units[3]);
        bool downApplied = sim3.units[3].HasStatus(StatusType.HealDown);
        log.AppendLine(string.Format("⑦ 潮噬（减益辅助）→ 目标 HealDown={0}  {1}", downApplied, downApplied ? "PASS" : "FAIL"));

        // ---- ⑧ 岩缚：35% 眩晕，多试几次至少要中一次 ----
        var rock = BattleDatabase.GetSkill("e_debuff");
        int stunHits = 0;
        for (int k = 0; k < 30; k++)
        {
            var simS = new BattleSimulator(cfg, 100 + k);
            for (int i = 0; i < 3; i++) simS.AddPlayer(BattleDatabase.GetCreature("beast_earth"), 24, new[] { 23, 23, 23, 23, 23, 23 });
            simS.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
            simS.Begin();
            simS.awaitingPlayer = simS.units[0];
            simS.ResolvePlayerTurn(rock, simS.units[3]);
            if (simS.units[3].HasStatus(StatusType.Stun)) stunHits++;
        }
        log.AppendLine(string.Format("⑧ 岩缚（35% 眩晕）30 次里中了 {0} 次  {1}", stunHits, stunHits > 0 ? "PASS" : "FAIL"));

        // ---- ⑨ 缠风：削行动值 ----
        var wind = BattleDatabase.GetSkill("a_debuff");
        var simW = new BattleSimulator(cfg, 9);
        for (int i = 0; i < 3; i++) simW.AddPlayer(BattleDatabase.GetCreature(i == 0 ? "beast_wind" : (i == 1 ? "beast_fire" : "beast_water")), 24, new[] { 23, 23, 23, 23, 23, 23 });
        simW.AddEnemy(BattleDatabase.GetCreature("en_bonefish"), 10, 1);
        simW.Begin();
        simW.units[3].gauge = 80f;
        simW.awaitingPlayer = simW.units[0];
        simW.ResolvePlayerTurn(wind, simW.units[3]);
        bool pushed = simW.units[3].gauge < 80f;
        log.AppendLine(string.Format("⑨ 缠风削行动值：80 → {0:0}  {1}", simW.units[3].gauge, pushed ? "PASS" : "FAIL"));

        Object.DestroyImmediate(debuff);
        Object.DestroyImmediate(cfg);
        System.IO.File.WriteAllText("D:/ljjh/tmp/playtest_fix.txt", log.ToString());
        Debug.Log(log.ToString());
    }
}