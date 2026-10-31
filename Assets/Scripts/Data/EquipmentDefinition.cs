using System.Collections.Generic;
using UnityEngine;

namespace LingJing.Data
{
    /// <summary>装备槽位。每只兽独立 5 件，全队 15 件。</summary>
    public enum EquipSlot
    {
        Weapon = 0,     // 武器 —— 主属性倾向 攻击
        Armor = 1,      // 护甲 —— 防御 / 生命
        Trinket = 2,    // 灵饰 —— 精神 / 天赋
        Boots = 3,      // 足具 —— 速度
        Totem = 4,      // 图腾 —— 任意单维（唯一可洗主属性的槽）
    }

    /// <summary>六维键位。装备的主属性直接加在对应六维上，副属性按百分比乘。</summary>
    public enum StatKind { Atk = 0, Def = 1, Spd = 2, Vit = 3, Spr = 4, Tal = 5 }

    /// <summary>一条词条：对某一维的百分比加成（0.08 = +8%）。</summary>
    [System.Serializable]
    public class StatMod
    {
        public StatKind stat;
        public float percent;
    }

    /// <summary>
    /// 一件装备的定义。数值锚见 E:\TT\装备与战力系统设计.md：
    /// 主属性 = CSV 基础值 × 品质系数 × (1 + 0.08 × 强化等级)。
    /// 词条只做百分比乘法，不引入暴击 / 闪避 / 抗性穿透等新战斗机制。
    /// </summary>
    [CreateAssetMenu(menuName = "灵境/装备", fileName = "eq_new")]
    public class EquipmentDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public EquipSlot slot;
        [Range(0, 4)] public int quality;
        public StatKind mainStat;
        public float mainValue;          // 已含品质系数与强化
        [Range(0, 5)] public int enhance; // 强化等级，仅用于展示与战力
        public List<StatMod> subs = new List<StatMod>();
        public int spiritMaxBonus;        // 灵能上限 +N

        /// <summary>品质系数：白/绿/蓝/紫/金。</summary>
        public static readonly float[] QualityCoeff = { 1.00f, 1.30f, 1.60f, 2.00f, 2.60f };

        public float MainValueAt(int enhanceLevel)
        {
            return mainValue * (1f + 0.08f * enhanceLevel);
        }
    }
}
