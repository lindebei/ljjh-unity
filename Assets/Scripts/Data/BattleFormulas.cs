using System;
using UnityEngine;

namespace LingJing.Data
{
    /// <summary>六维原始值（1 级基础值 + 成长）。换算公式集中在 BattleFormulas。</summary>
    [Serializable]
    public struct SixStats
    {
        public int atk;  // 攻击
        public int def;  // 防御
        public int spd;  // 速度
        public int vit;  // 体力（100–200 量程）
        public int spr;  // 精神
        public int tal;  // 天赋

        public SixStats(int atk, int def, int spd, int vit, int spr, int tal)
        {
            this.atk = atk; this.def = def; this.spd = spd; this.vit = vit; this.spr = spr; this.tal = tal;
        }

        public SixStats Plus(int v)
        {
            return new SixStats(atk + v, def + v, spd + v, vit + v, spr + v, tal + v);
        }

        public override string ToString()
        {
            return string.Format("攻{0} 防{1} 速{2} 体{3} 精{4} 天{5}", atk, def, spd, vit, spr, tal);
        }
    }

    /// <summary>成长潜力（小数）。1 点自由点 = 该项潜力值，所以潜力必须是 0.80 这种小数，不能塞进 int 的 SixStats。</summary>
    [Serializable]
    public struct SixStatsF
    {
        public float atk;
        public float def;
        public float spd;
        public float vit;
        public float spr;
        public float tal;

        public SixStatsF(float atk, float def, float spd, float vit, float spr, float tal)
        {
            this.atk = atk; this.def = def; this.spd = spd; this.vit = vit; this.spr = spr; this.tal = tal;
        }

        public float Sum() { return atk + def + spd + vit + spr + tal; }

        public override string ToString()
        {
            return string.Format("P 攻{0:F2} 防{1:F2} 速{2:F2} 体{3:F2} 精{4:F2} 天{5:F2}", atk, def, spd, vit, spr, tal);
        }
    }

    /// <summary>六维 → 战斗属性的换算。所有公式只在这里出现一次，别在别处重抄。</summary>
    public static class BattleFormulas
    {
        /// <summary>生命值 = 体力 × 8 × (1 + 0.03 × (等级 − 1))</summary>
        public static float MaxHp(int vit, int level)
        {
            return vit * 8f * (1f + 0.03f * (level - 1));
        }

        /// <summary>减伤 = 300 / (300 + 防御)</summary>
        public static float DamageReduction(float def, float defConstant)
        {
            return defConstant / (defConstant + def);
        }

        /// <summary>归一化后的出手间隔（秒）。fac = (100 + 我方开场速度均值) / 155，整场锁定。</summary>
        public static float AttackInterval(float spd, float normalizationFactor)
        {
            return 2000f / (100f + spd) * normalizationFactor;
        }

        /// <summary>行动条每节拍增量：4 × (100 + 速度) / (100 + 速度均值)。</summary>
        public static float GaugeGainPerTick(float spd, float meanSpd, float gainPerTick)
        {
            return gainPerTick * (100f + spd) / (100f + meanSpd);
        }

        /// <summary>成长潜力：归一总值 = 攻 + 防 + 速 + 体/2 + 精 + 天；P = 该项 / 归一总值 × 10。</summary>
        public static float Potential(float itemValue, SixStats s)
        {
            float total = s.atk + s.def + s.spd + s.vit * 0.5f + s.spr + s.tal;
            if (total <= 0f) return 0f;
            return itemValue / total * 10f;
        }

        /// <summary>敌人六维 = 原型 1 级六维 + 每级 +1，无自由点。</summary>
        public static SixStats EnemyStats(SixStats baseStats, int level)
        {
            return baseStats.Plus(level - 1);
        }

        /// <summary>玩家兽六维 = 1 级六维 + 每级 +1 + 自由点 × 潜力。</summary>
        public static SixStats PlayerStats(SixStats baseStats, int level, SixStatsF potentials, int[] freePoints)
        {
            var s = baseStats.Plus(level - 1);
            return new SixStats(
                s.atk + Mathf.RoundToInt(freePoints[0] * potentials.atk),
                s.def + Mathf.RoundToInt(freePoints[1] * potentials.def),
                s.spd + Mathf.RoundToInt(freePoints[2] * potentials.spd),
                s.vit + Mathf.RoundToInt(freePoints[3] * potentials.vit),
                s.spr + Mathf.RoundToInt(freePoints[4] * potentials.spr),
                s.tal + Mathf.RoundToInt(freePoints[5] * potentials.tal));
        }

        /// <summary>升级经验 = round_to_10(80 + 8 × 当前等级 + 1.5 × 当前等级²)</summary>
        public static int ExpToNext(int level)
        {
            double raw = 80 + 8.0 * level + 1.5 * level * level;
            return (int)(Math.Round(raw / 10.0) * 10);
        }

        /// <summary>初始灵能 = 4 + floor((三兽精神均值 − 60) / 30)，上限 6。</summary>
        public static int SpiritStart(float meanSpr)
        {
            int v = 4 + (int)Math.Floor((meanSpr - 60f) / 30f);
            return Mathf.Clamp(v, 0, 6);
        }

        /// <summary>每 3 回合自然回复 floor(三兽精神均值 / 100)。</summary>
        public static int SpiritRegen(float meanSpr)
        {
            return (int)Math.Floor(meanSpr / 100f);
        }
    }
}
