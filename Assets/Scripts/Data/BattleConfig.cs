using UnityEngine;

namespace LingJing.Data
{
    /// <summary>
    /// 全局战斗参数。全部暴露到 Inspector，v0.1 的任务就是调参，改一个数不用重编译。
    /// 数值口径一律"百分比用 0–1 小数"，避免有的地方 ×100 有的没乘。
    /// </summary>
    [CreateAssetMenu(menuName = "灵境/BattleConfig", fileName = "BattleConfig")]
    public class BattleConfig : ScriptableObject
    {
        [Header("伤害")]
        public float attackK = 3.6f;
        public float defConstant = 300f;
        public float elementalAdvantage = 1.25f;     // 水→火→风→土→水
        public float elementalDisadvantage = 0.85f;  // 被克
        public bool useElementalAdvantage = true;

        [Header("命中（等级差 -3..+3）")]
        public float[] hitByLevelDiff = { 1.00f, 1.00f, 0.95f, 0.85f, 0.75f, 0.65f, 0.55f };
        public float[] dmgByLevelDiff = { 1.30f, 1.20f, 1.10f, 1.00f, 0.90f, 0.80f, 0.70f };
        public float levelDiffDamagePinch = 0.30f;   // 超出 ±3 级后每级 ±10%，封顶 ±30%

        [Header("期望值模式（对账 sim/ 用）")]
        public bool useExpectedValue = true;   // true：命中率折进期望伤害、无浮动、无暴击
        public float damageVariance = 0.05f;   // 随机模式下的浮动幅度（±5%）

        [Header("DoT")]
        public int dotMaxStacks = 15;
        public float dotPercentPerStack = 0.005f;  // 每层每回合 0.5% 最大生命
        public int dotDuration = 3;

        [Header("灵能")]
        public int spiritMax = 8;
        public int spiritRegenEveryRounds = 3;

        [Header("行动条")]
        public float tickSeconds = 0.5f;
        public float gaugeGainPerTick = 4f;
        public float gaugeThreshold = 100f;

        [Header("药品")]
        public int potionsPerBattle = 8;
        public float potionHealPercent = 0.35f;
        public float potionTriggerHpPercent = 0.35f;

        [Header("回合（我方累计 3 次行动 = 1 回合）")]
        public int playerActionsPerRound = 3;

        [Header("治疗（两段式）")]
        public float healTalentConstant = 250f;
        public float healSecondRatio = 0.5f;

        [Header("护盾")]
        public float shieldTempDuration = 3f;

        [Header("品质倍率：生命 / 防御 / 攻击 / 速度")]
        public float[] qualityHpMul   = { 1.90f, 2.60f, 4.20f, 6.00f, 12.0f };
        public float[] qualityDefMul  = { 0.75f, 1.15f, 1.30f, 1.45f, 1.60f };
        public float[] qualityAtkMul  = { 0.60f, 1.05f, 1.15f, 1.25f, 1.35f };
        public float[] qualitySpdMul  = { 0.85f, 1.00f, 1.05f, 1.10f, 1.15f };

        [Header("难度旋钮")]
        // 敌人统一强度系数：只乘「攻击」与「生命」，不动设计文档里的品质倍率。
        // 用途：v0.1 只要「胜率区间」对（普通 92–96%、满编波次 30–40%），不动原数值表。
        // 调这个值不用改 6 组品质倍率，也就不会让设计文档与实现打架。
        public float enemyPowerScale = 1f;

        // 波次喘息：清完一波、下一波入场时给我方的补给。
        // 没有这个，10 只怪（3 波）是纯堆数量——我方 3 次行动换敌方 10 次，数学上必输；
        // 有了它，多波变成「资源管理」而不是「必死」。
        // 品质级难度系数（普通/精英/小Boss/中Boss/大Boss），在品质倍率之上再乘一层。
        // 用户口径：普通怪满波次可以 100%；精英满波次无装备 70–80%；Boss 无装备 40%。
        public float[] qualityPowerScale = { 1.0f, 1.70f, 3.20f, 3.60f, 4.00f };

        /// <summary>全体技能（AoE）的伤害额外倍率。提清场效率用，不动 skills.csv 的设计倍率。</summary>
        public float aoeDamageScale = 1f;

        public float waveClearHealPercent = 0.12f;
        public int waveClearSpirit = 2;

        [Header("等级带系数（1–5 / 6–12 / 13–24）")]
        public int[] levelBandUpper = { 5, 12, 24 };
        public float[] levelBandHpMul = { 0.70f, 0.85f, 1.00f };
        public float[] levelBandAtkMul = { 0.80f, 0.90f, 1.00f };
        public int[] levelBandMaxEnemies = { 2, 3, 4 };

        [Header("战斗时长上限（秒，防死循环）")]
        public float timeCapSeconds = 900f;

        /// <summary>回合硬上限：打到第 maxRounds 回合结束还没清完所有怪，直接判负。</summary>
        public int maxRounds = 30;

        /// <summary>等级差 → 命中率（超范围按两端取）。</summary>
        public float HitChance(int levelDiff)
        {
            int idx = levelDiff + 3;
            idx = Mathf.Clamp(idx, 0, hitByLevelDiff.Length - 1);
            return hitByLevelDiff[idx];
        }

        /// <summary>等级差 → 伤害修正（超范围按两端取）。</summary>
        public float LevelDiffDamage(int levelDiff)
        {
            int idx = levelDiff + 3;
            idx = Mathf.Clamp(idx, 0, dmgByLevelDiff.Length - 1);
            return dmgByLevelDiff[idx];
        }

        public int BandIndex(int level)
        {
            for (int i = 0; i < levelBandUpper.Length; i++)
                if (level <= levelBandUpper[i]) return i;
            return levelBandUpper.Length - 1;
        }
    }
}
