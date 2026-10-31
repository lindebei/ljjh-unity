using System;
using UnityEngine;

namespace LingJing.Data
{
    /// <summary>兽/怪物的静态定义。玩家兽与怪物共用同一个类型，靠 isPlayerBeast 区分。</summary>
    [CreateAssetMenu(menuName = "灵境/Creature", fileName = "creature")]
    public class CreatureDefinition : ScriptableObject
    {
        [Header("标识")]
        public string id;
        public string displayName;
        public Element element;
        public bool isPlayerBeast;

        [Header("六维（1 级基础值）")]
        public SixStats baseStats = new SixStats(50, 50, 50, 150, 50, 50);

        [Header("成长潜力（每兽合计 10.00，仅玩家兽有意义）")]
        public SixStatsF potentials = new SixStatsF(1f, 1f, 1f, 1f, 1f, 1f);

        [Header("技能（普攻 / 主动 / 辅助 / 奥义）")]
        public SkillDefinition[] skills = new SkillDefinition[4];

        [Header("敌方属性")]
        public Quality quality = Quality.Normal;
        public string enemySkillSet;   // 水/火/风/土 —— 敌方三招按属性共用

        [Header("表现占位")]
        public Color placeholderColor = Color.white;

        public SkillDefinition Skill(SkillType type)
        {
            int i = (int)type;
            if (skills == null || i < 0 || i >= skills.Length) return null;
            return skills[i];
        }

        void OnValidate()
        {
            if (isPlayerBeast)
            {
                float sum = potentials.Sum();
                if (Mathf.Abs(sum - 10f) > 0.02f)
                    Debug.LogError(string.Format("[{0}] 潜力合计 {1}，必须等于 10.00", id, sum));

                for (int i = 0; i < 4; i++)
                    if (skills == null || skills.Length < 4 || skills[i] == null)
                        Debug.LogError(string.Format("[{0}] 玩家兽必须有 4 个技能，槽位 {1} 为空", id, i));
            }

            if (baseStats.vit < 100 || baseStats.vit > 200)
                Debug.LogError(string.Format("[{0}] 体力 {1} 超出 100–200", id, baseStats.vit));
        }
    }
}
