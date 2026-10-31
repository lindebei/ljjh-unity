using System;
using UnityEngine;

namespace LingJing.Data
{
    /// <summary>四属性。克制环：水→火→风→土→水（水克火，火克风，风克土，土克水）。</summary>
    public enum Element { Water = 0, Fire = 1, Wind = 2, Earth = 3 }

    /// <summary>技能槽。顺序固定：普攻 / 主动 / 辅助 / 奥义。</summary>
    public enum SkillType { Basic = 0, Active = 1, Support = 2, Ultimate = 3 }

    public enum TargetSide { Enemy, Ally, Self, AllEnemies, AllAllies }

    /// <summary>
    /// 状态。友方增益（湿润/风蚀/岩化/治愈）与敌方减益（灼烧/中毒）共用一套容器，
    /// 靠 IsDebuff() 区分结算方向，避免状态栏写两遍逻辑。
    /// </summary>
    public enum StatusType
    {
        None = 0,
        Wet = 1,          // 湿润：受到的治疗与护盾 +25%，2 回合（自身增益）
        WindErosion = 2,  // 风蚀：速度 +20%，2 回合（自身增益）
        Petrify = 3,      // 岩化：防御 +30%，2 回合（自身增益）
        HealMark = 4,     // 治愈：下一回合开始回第一段的一半（自身增益，1 段）
        Burn = 5,         // 灼烧：每层每回合 0.5% 最大生命，最多 15 层，3 回合（敌方减益）
        Poison = 6        // 中毒：同上，与灼烧独立叠层
    }

    public enum Quality { Normal = 0, Elite = 1, MiniBoss = 2, MidBoss = 3, BigBoss = 4 }

    /// <summary>战斗阵营。</summary>
    public enum Side { Player = 0, Enemy = 1 }

    public static class ElementRules
    {
        /// <summary>水→火→风→土→水。攻击方 a 打防守方 d。</summary>
        public static bool Beats(Element a, Element d)
        {
            switch (a)
            {
                case Element.Water: return d == Element.Fire;
                case Element.Fire: return d == Element.Wind;
                case Element.Wind: return d == Element.Earth;
                case Element.Earth: return d == Element.Water;
            }
            return false;
        }
    }

    public static class StatusRules
    {
        public static bool IsDebuff(StatusType t)
        {
            return t == StatusType.Burn || t == StatusType.Poison;
        }

        public static Color ToColor(StatusType t)
        {
            switch (t)
            {
                case StatusType.Wet: return new Color(0.35f, 0.65f, 1f);
                case StatusType.WindErosion: return new Color(0.55f, 0.95f, 0.65f);
                case StatusType.Petrify: return new Color(0.85f, 0.72f, 0.35f);
                case StatusType.HealMark: return new Color(0.45f, 1f, 0.7f);
                case StatusType.Burn: return new Color(1f, 0.45f, 0.25f);
                case StatusType.Poison: return new Color(0.65f, 0.35f, 0.95f);
            }
            return Color.white;
        }
    }
}
