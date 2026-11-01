using UnityEngine;

namespace LingJing.Data
{
    /// <summary>
    /// 一个类覆盖全部技能，靠字段组合表达，不为每种技能写子类。
    /// 口径：multiplier 是"每段"倍率；spiritCost = -1 表示普攻回复 1 点灵能。
    /// </summary>
    [CreateAssetMenu(menuName = "灵境/Skill", fileName = "skill")]
    public class SkillDefinition : ScriptableObject
    {
        [Header("标识")]
        [Header("技能池（每兽 8 个，随等级解锁）")]
        public int tier = 2;           // 1=普攻 2=一段 3=二段 4=三段 5=第二奥义
        public int unlockLevel = 1;
        public int maxRank = 3;

        public string id;
        public string displayName;
        public SkillType type;
        public Element element;

        [Header("消耗与冷却")]
        public int spiritCost;      // -1 = 普攻，回复 1 点
        public int cooldown;        // 0 = 无冷却，单位=回合

        [Header("伤害")]
        public float multiplier;
        public int segments = 1;
        public bool spreadSegments; // 多段优先分摊到不同目标

        [Header("范围与目标")]
        public TargetSide targetSide = TargetSide.Enemy;
        public int targetCount = 1;

        [Header("命中与状态")]
        public bool requiresHitCheck = true;
        public StatusType appliedStatus;
        public int dotStacks;
        public float controlChance;
        public int controlDuration;
        public float gaugePushbackPercent;   // 击退：削减目标 x% 行动值
        public bool taunt;
        public bool selfAppliesStatus;       // 状态挂自己（水灵【潮湿】那种）

        [Header("治疗 / 护盾 / 增益")]
        public bool isHeal;
        public bool isShield;
        public float shieldPercent;          // 0.20 = 目标最大生命 20%
        public bool dispelOne;
        public bool healAllAllies;           // 奥义：全队各挂治愈标记
        public float buffPercent;            // 0.25 = +25%
        public int buffDuration;
        public StatusType buffStatus;        // 增益走状态还是走数值
        public bool buffIsAttack;            // true=攻击 buff，false=速度 buff

        [Header("表现")]
        public string vfxKey;
    }
}
