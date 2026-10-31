using UnityEngine;

namespace LingJing.Data
{
    /// <summary>
    /// 一只兽身上 5 件装备。用普通类（不是 SO）是因为它是运行时状态：
    /// 掉落、换装都会改它，写回 SO 会污染资产。
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

        /// <summary>装备只做百分比加成：先把 5 件的同类词条相加，再一次性乘到六维上。</summary>
        public SixStats Apply(SixStats baseStats)
        {
            float[] pct = new float[6];
            for (int i = 0; i < slotItems.Length; i++)
            {
                var e = slotItems[i];
                if (e == null || e.mods == null) continue;
                for (int k = 0; k < e.mods.Count; k++)
                {
                    var m = e.mods[k];
                    if (m == null) continue;
                    pct[(int)m.stat] += m.percent;
                }
            }

            var s = baseStats;
            s.atk = Mathf.RoundToInt(s.atk * (1f + pct[(int)StatKind.Atk]));
            s.def = Mathf.RoundToInt(s.def * (1f + pct[(int)StatKind.Def]));
            s.spd = Mathf.RoundToInt(s.spd * (1f + pct[(int)StatKind.Spd]));
            s.vit = Mathf.RoundToInt(s.vit * (1f + pct[(int)StatKind.Vit]));
            s.spr = Mathf.RoundToInt(s.spr * (1f + pct[(int)StatKind.Spr]));
            s.tal = Mathf.RoundToInt(s.tal * (1f + pct[(int)StatKind.Tal]));
            return s;
        }

        /// <summary>装备战力（只用于展示与遇敌匹配，不参与结算）。</summary>
        public float Power()
        {
            float p = 0f;
            for (int i = 0; i < slotItems.Length; i++)
            {
                var e = slotItems[i];
                if (e == null) continue;
                p += ItemPower(e);
            }
            return p;
        }

        public static float ItemPower(EquipmentDefinition e)
        {
            if (e == null || e.mods == null) return 0f;
            float p = 0f;
            for (int k = 0; k < e.mods.Count; k++)
            {
                var m = e.mods[k];
                if (m == null) continue;
                p += m.percent * 100f * PowerWeight(m.stat);
            }
            return p;
        }

        static float PowerWeight(StatKind s)
        {
            switch (s)
            {
                case StatKind.Atk: return 1.6f;
                case StatKind.Def: return 1.2f;
                case StatKind.Spd: return 1.1f;
                case StatKind.Spr: return 0.9f;
                case StatKind.Tal: return 0.8f;
                default: return 1.0f;   // Vit
            }
        }
    }
}
