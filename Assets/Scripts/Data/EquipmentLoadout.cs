using UnityEngine;

namespace LingJing.Data
{
    /// <summary>
    /// 一只兽身上 5 件装备。用普通类（不是 SO）是因为它是运行时状态：
    /// 掉落、换装、强化都会改它，写回 SO 会污染资产。
    /// </summary>
    public class EquipmentLoadout
    {
        public readonly EquipmentDefinition[] slotItems = new EquipmentDefinition[5];

        public void Set(EquipmentDefinition def)
        {
            if (def == null) return;
            slotItems[(int)def.slot] = def;
        }

        public EquipmentDefinition Get(EquipSlot slot) { return slotItems[(int)slot]; }

        public bool IsEmpty()
        {
            for (int i = 0; i < slotItems.Length; i++) if (slotItems[i] != null) return false;
            return true;
        }

        /// <summary>把装备加成套到六维上：主属性加法（已含品质与强化），词条按百分比乘。</summary>
        public SixStats Apply(SixStats baseStats, out int spiritMaxBonus)
        {
            var s = baseStats;
            spiritMaxBonus = 0;
            float[] pct = new float[6];

            for (int i = 0; i < slotItems.Length; i++)
            {
                var e = slotItems[i];
                if (e == null) continue;
                AddFlat(ref s, e.mainStat, e.MainValueAt(e.enhance));
                spiritMaxBonus += e.spiritMaxBonus;
                if (e.subs != null)
                {
                    for (int k = 0; k < e.subs.Count; k++)
                    {
                        var m = e.subs[k];
                        if (m == null) continue;
                        pct[(int)m.stat] += m.percent;
                    }
                }
            }

            s.atk = Mathf.RoundToInt(s.atk * (1f + pct[(int)StatKind.Atk]));
            s.def = Mathf.RoundToInt(s.def * (1f + pct[(int)StatKind.Def]));
            s.spd = Mathf.RoundToInt(s.spd * (1f + pct[(int)StatKind.Spd]));
            s.vit = Mathf.RoundToInt(s.vit * (1f + pct[(int)StatKind.Vit]));
            s.spr = Mathf.RoundToInt(s.spr * (1f + pct[(int)StatKind.Spr]));
            s.tal = Mathf.RoundToInt(s.tal * (1f + pct[(int)StatKind.Tal]));
            return s;
        }

        /// <summary>装备战力（不含六维换算），用于 UI 展示与遇敌匹配。</summary>
        public float Power()
        {
            float p = 0f;
            for (int i = 0; i < slotItems.Length; i++)
            {
                var e = slotItems[i];
                if (e == null) continue;
                p += e.MainValueAt(e.enhance) * PowerWeight(e.mainStat);
                if (e.subs != null)
                    for (int k = 0; k < e.subs.Count; k++)
                    {
                        var m = e.subs[k];
                        if (m == null) continue;
                        p += m.percent * 100f * PowerWeight(m.stat) * 0.5f;
                    }
            }
            return p;
        }

        static float PowerWeight(StatKind s)
        {
            switch (s)
            {
                case StatKind.Atk: return 6f;
                case StatKind.Def: return 4f;
                case StatKind.Spd: return 3f;
                case StatKind.Spr: return 2f;
                case StatKind.Tal: return 2f;
                default: return 2.4f;   // Vit：生命已由体力×8 放大，权重按折算后给
            }
        }

        static void AddFlat(ref SixStats s, StatKind k, float v)
        {
            int iv = Mathf.RoundToInt(v);
            switch (k)
            {
                case StatKind.Atk: s.atk += iv; break;
                case StatKind.Def: s.def += iv; break;
                case StatKind.Spd: s.spd += iv; break;
                case StatKind.Vit: s.vit += iv; break;
                case StatKind.Spr: s.spr += iv; break;
                case StatKind.Tal: s.tal += iv; break;
            }
        }
    }
}
