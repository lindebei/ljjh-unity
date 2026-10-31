using System.Collections.Generic;
using UnityEngine;

namespace LingJing.Data
{
    /// <summary>装备槽位。每只兽独立 5 件，全队 15 件。</summary>
    public enum EquipSlot
    {
        Weapon = 0,     // 武器
        Armor = 1,      // 护甲
        Trinket = 2,    // 灵饰
        Boots = 3,      // 足具
        Totem = 4,      // 图腾
    }

    /// <summary>六维键位。装备只在这六维上做百分比加成。</summary>
    public enum StatKind { Atk = 0, Def = 1, Spd = 2, Vit = 3, Spr = 4, Tal = 5 }

    /// <summary>一条词条：对某一维的百分比加成（0.08 = +8%）。</summary>
    [System.Serializable]
    public class StatMod
    {
        public StatKind stat;
        public float percent;
    }

    /// <summary>
    /// 一件装备的定义。口径（2026-11-01 用户定稿）：
    /// 每件装备只加六维里的 **3 种**，全用百分比；品质暂时不做，先只保留字段。
    /// 数值别处不要重抄，全部走 equipment.csv → 导入器。
    /// </summary>
    [CreateAssetMenu(menuName = "灵境/装备", fileName = "eq_new")]
    public class EquipmentDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public EquipSlot slot;
        public int quality;                 // 预备字段，v0.2 不做品质
        public List<StatMod> mods = new List<StatMod>();   // 固定 3 条

        public float Percent(StatKind stat)
        {
            float sum = 0f;
            if (mods == null) return 0f;
            for (int i = 0; i < mods.Count; i++)
                if (mods[i] != null && mods[i].stat == stat) sum += mods[i].percent;
            return sum;
        }
    }
}
