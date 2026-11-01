using System;
using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    /// <summary>运行时状态实例。DoT 用 stacks 叠层，增益用 roundsLeft 计时。</summary>
    public class StatusInstance
    {
        public StatusType type;
        public int stacks;
        public int roundsLeft;
        public float value;      // 治愈标记的待结算量

        public StatusInstance(StatusType type, int stacks, int roundsLeft, float value = 0f)
        {
            this.type = type; this.stacks = stacks; this.roundsLeft = roundsLeft; this.value = value;
        }
    }

    /// <summary>战斗中的一只单位。静态数据在 CreatureDefinition，这里只放会变的东西。</summary>
    public class BattleUnit
    {
        /// <summary>
        /// 血量变化的全局广播：UI 靠它弹飘字（伤害/治疗）。
        /// 用静态事件是因为所有伤害都收口在 TakeDamage/Heal 两个方法上，
        /// 从这里广播比在每个调用点手工记录少漏。
        /// </summary>
        public static System.Action<BattleUnit, float, bool> HpChanged;

        public CreatureDefinition def;
        public Side side;
        public int level;
        public int index;
        public string id;
        public string displayName;
        public Element element;
        public Quality quality;

        // 六维（已含等级成长）。品质/等级带倍率用小数字段挂在这里，保证攻击可以是小数。
        public SixStats stats;
        public float atkMul = 1f;
        public float defMul = 1f;
        public float spdMul = 1f;

        // 嘲讽：>0 时敌方普通攻击必须打这只
        public int tauntRounds;

        /// <summary>战力：只用于展示与遇敌匹配，不参与任何结算。</summary>
        public float power;
        public float equipPower;

        public float maxHp;
        public float hp;
        public float shield;

        public float gauge;
        public int mp;             // 自己的蓝（每只兽独立，上限按精神算）
        public int mpMax;
        public int actionsTaken;

        // 增益（不进状态列表，直接算进有效属性）
        public float atkBuff;
        public int atkBuffRounds;
        public float spdBuff;
        public int spdBuffRounds;

        public readonly List<StatusInstance> statuses = new List<StatusInstance>();
        public readonly Dictionary<string, int> cooldowns = new Dictionary<string, int>();

        public bool IsAlive { get { return hp > 0f; } }

        /// <summary>是否已经入场。多波战斗里，后面的批次先建好但不参战（行动条不动、不可被选为目标）。</summary>
        public bool inField = true;

        /// <summary>上阵的 4 个技能（不含普攻）。普攻单独在 SkillAt(0)。</summary>
        public LingJing.Data.SkillDefinition[] equipped = new LingJing.Data.SkillDefinition[4];

        /// <summary>技能栏第 i 个按钮对应的技能：0 = 普攻，1..4 = 上阵技能。</summary>
        public LingJing.Data.SkillDefinition SkillAt(int i)
        {
            if (i <= 0) return def != null ? def.BasicSkill() : null;
            int k = i - 1;
            if (equipped == null || k < 0 || k >= equipped.Length) return null;
            return equipped[k];
        }

        /// <summary>掉落是否已经掷过（同一只怪只掷一次）。</summary>
        public bool lootRolled;

        public float HpPercent { get { return maxHp <= 0f ? 0f : Mathf.Clamp01(hp / maxHp); } }

        /// <summary>有效攻击：含攻击增益与品质倍率。</summary>
        public float EffectiveAtk { get { return stats.atk * atkMul * (1f + atkBuff); } }

        /// <summary>有效速度：含速度增益与风蚀。</summary>
        public float EffectiveSpd
        {
            get
            {
                float s = stats.spd * spdMul * (1f + spdBuff);
                if (HasStatus(StatusType.WindErosion)) s *= 1.20f;
                return s;
            }
        }

        /// <summary>有效防御：含岩化。</summary>
        public float EffectiveDef
        {
            get
            {
                float d = stats.def * defMul;
                if (HasStatus(StatusType.Petrify)) d *= 1.30f;
                return d;
            }
        }

        public bool HasStatus(StatusType t)
        {
            for (int i = 0; i < statuses.Count; i++) if (statuses[i].type == t) return true;
            return false;
        }

        public StatusInstance GetStatus(StatusType t)
        {
            for (int i = 0; i < statuses.Count; i++) if (statuses[i].type == t) return statuses[i];
            return null;
        }

        public int DotStacks(StatusType t)
        {
            var s = GetStatus(t);
            return s == null ? 0 : s.stacks;
        }

        /// <summary>加/刷状态。DoT 叠层，其余刷新持续回合。</summary>
        public void ApplyStatus(StatusType t, int stacks, int rounds, float value = 0f, int maxDotStacks = 15)
        {
            if (t == StatusType.None) return;
            var existing = GetStatus(t);
            if (existing == null)
            {
                statuses.Add(new StatusInstance(t, Mathf.Max(stacks, 1), rounds, value));
                return;
            }

            if (StatusRules.IsDebuff(t))
            {
                existing.stacks = Mathf.Min(existing.stacks + stacks, maxDotStacks);
            }
            else
            {
                existing.value = value > 0f ? value : existing.value;
            }
            existing.roundsLeft = Mathf.Max(existing.roundsLeft, rounds);
        }

        public void RemoveStatus(StatusType t)
        {
            for (int i = statuses.Count - 1; i >= 0; i--)
                if (statuses[i].type == t) statuses.RemoveAt(i);
        }

        public void ClearOneDebuff()
        {
            for (int i = 0; i < statuses.Count; i++)
            {
                if (StatusRules.IsDebuff(statuses[i].type))
                {
                    statuses.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>扣血。护盾先吃，返回实际进血条的伤害。</summary>
        public float TakeDamage(float raw)
        {
            if (raw <= 0f) return 0f;
            float absorbed = Mathf.Min(shield, raw);
            shield -= absorbed;
            float toHp = raw - absorbed;
            hp = Mathf.Max(0f, hp - toHp);
            if (toHp > 0f && HpChanged != null) HpChanged(this, toHp, false);
            return toHp;
        }

        public float Heal(float amount)
        {
            if (amount <= 0f || !IsAlive) return 0f;
            float before = hp;
            hp = Mathf.Min(maxHp, hp + amount);
            float healed = hp - before;
            if (healed > 0f && HpChanged != null) HpChanged(this, healed, true);
            return healed;
        }

        public float MissingHp { get { return Mathf.Max(0f, maxHp - hp); } }

        public float Cooldown(string skillId)
        {
            int v;
            return cooldowns.TryGetValue(skillId, out v) ? v : 0;
        }

        public void SetCooldown(string skillId, int rounds)
        {
            if (rounds > 0) cooldowns[skillId] = rounds;
        }

        public void TickCooldowns()
        {
            if (cooldowns.Count == 0) return;
            var keys = new List<string>(cooldowns.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                cooldowns[keys[i]] -= 1;
                if (cooldowns[keys[i]] <= 0) cooldowns.Remove(keys[i]);
            }
        }

        public override string ToString()
        {
            return string.Format("{0}({1}) hp {2:F0}/{3:F0} 盾{4:F0} 速{5:F0} 行动值{6:F0}",
                displayName, side, hp, maxHp, shield, EffectiveSpd, gauge);
        }
    }
}
