using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    /// <summary>
    /// 伤害管线：倍率 × 攻击 × 3.6 × 减伤 × 克制 × 等级差 × 浮动。
    /// 期望值模式下把命中率折进伤害（不掷骰），保证结果可复现、能跟 sim/ 对账。
    /// </summary>
    public class DamageCalculator
    {
        readonly BattleConfig config;

        public DamageCalculator(BattleConfig config)
        {
            this.config = config;
        }

        public float ElementMultiplier(Element attacker, Element defender)
        {
            if (!config.useElementalAdvantage) return 1f;
            if (ElementRules.Beats(attacker, defender)) return config.elementalAdvantage;
            if (ElementRules.Beats(defender, attacker)) return config.elementalDisadvantage;
            return 1f;
        }

        /// <summary>单段伤害（已含期望命中）。</summary>
        public float ComputeHit(BattleUnit attacker, BattleUnit target, float multiplier)
        {
            int diff = attacker.level - target.level;
            float hit = config.HitChance(diff);
            float dmg = multiplier
                        * attacker.EffectiveAtk
                        * config.attackK
                        * BattleFormulas.DamageReduction(target.EffectiveDef, config.defConstant)
                        * ElementMultiplier(attacker.element, target.element)
                        * config.LevelDiffDamage(diff);

            if (config.useExpectedValue) return dmg * hit;

            // 随机模式：先判命中，再叠浮动
            if (Random.value > hit) return 0f;
            float v = 1f + Random.Range(-config.damageVariance, config.damageVariance);
            return dmg * v;
        }

        /// <summary>扣血并返回实际伤害。</summary>
        public float Apply(BattleUnit attacker, BattleUnit target, float multiplier)
        {
            float dmg = ComputeHit(attacker, target, multiplier);
            target.TakeDamage(dmg);
            return dmg;
        }

        /// <summary>治疗第一段系数：天赋/(天赋+250)，湿润再 ×1.25。</summary>
        public float HealFirstRatio(BattleUnit healer)
        {
            float coef = healer.stats.tal / (healer.stats.tal + config.healTalentConstant);
            if (healer.HasStatus(StatusType.Wet)) coef *= 1.25f;
            return coef;
        }
    }
}
