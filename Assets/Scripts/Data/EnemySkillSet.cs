using UnityEngine;

namespace LingJing.Data
{
    /// <summary>
    /// 敌方三招（按属性共用一套）：普攻 / 主动 / 群体，Boss 额外一个奥义。
    /// 怪物不耗灵能，用冷却做节奏限制——不给 AI 藏一个看不见的资源池。
    /// </summary>
    [CreateAssetMenu(menuName = "灵境/EnemySkillSet", fileName = "enemy_skillset")]
    public class EnemySkillSet : ScriptableObject
    {
        public Element element;
        public SkillDefinition basic;
        public SkillDefinition active;
        public SkillDefinition aoe;
        public SkillDefinition ultimate;

        [Header("奥义门槛")]
        public float ultimateUnlockHpPercent = 0.30f;
        public int ultimateCooldown = 4;

        [Header("AI 概率（期望值模式下按概率折算或强制）")]
        public float activeChanceWhenReady = 0.60f;
        public float aoeChanceWhenReady = 0.70f;
    }
}
