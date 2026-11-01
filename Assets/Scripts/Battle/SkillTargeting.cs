// 辅助技能的「对谁」判定：同一类 Support 里既有给己方加 buff 的，也有给敌人挂 debuff 的，
// 所以不能只看 type，要看技能的实际效果。数据写错了也在这里被纠正（并在导入时报警）。
using LingJing.Data;

namespace LingJing.Battle
{
    public static class SkillTargeting
    {
        /// <summary>对己方生效：治疗、护盾、增益（攻击/速度）、群体治疗。</summary>
        public static bool IsFriendlyEffect(SkillDefinition s)
        {
            if (s == null) return false;
            return s.isHeal || s.isShield || s.healAllAllies || s.buffPercent > 0f;
        }

        /// <summary>对敌方生效：有伤害倍率，或挂的是减益状态（灼烧/中毒/减疗…）。</summary>
        public static bool IsHostileEffect(SkillDefinition s)
        {
            if (s == null) return false;
            if (s.multiplier > 0f) return true;
            return s.appliedStatus != StatusType.None && StatusRules.IsDebuff(s.appliedStatus);
        }

        /// <summary>
        /// 实际应该打谁：数据里的 target 只是提示，效果语义优先。
        /// 纯增益却写成 Enemy → 当 Ally；纯减益却写成 Ally → 当 Enemy；两边都占（伤害+治疗）保持原样。
        /// </summary>
        public static TargetSide EffectiveSide(SkillDefinition s)
        {
            if (s == null) return TargetSide.Enemy;
            bool friendly = IsFriendlyEffect(s);
            bool hostile = IsHostileEffect(s);
            if (friendly == hostile) return s.targetSide;   // 都真或都假：按数据

            if (friendly)
            {
                if (s.targetSide == TargetSide.Enemy) return TargetSide.Ally;
                if (s.targetSide == TargetSide.AllEnemies) return TargetSide.AllAllies;
                return s.targetSide;
            }

            // hostile only
            if (s.targetSide == TargetSide.Ally) return TargetSide.Enemy;
            if (s.targetSide == TargetSide.Self) return TargetSide.Enemy;
            if (s.targetSide == TargetSide.AllAllies) return TargetSide.AllEnemies;
            return s.targetSide;
        }

        /// <summary>是否需要玩家点一个目标（单体）。</summary>
        public static bool NeedsPick(SkillDefinition s)
        {
            var side = EffectiveSide(s);
            return side == TargetSide.Enemy || side == TargetSide.Ally;
        }

        /// <summary>点目标时点的是哪一边。</summary>
        public static bool PickAlly(SkillDefinition s)
        {
            return EffectiveSide(s) == TargetSide.Ally;
        }
    }
}