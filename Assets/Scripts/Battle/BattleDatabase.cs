using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    /// <summary>
    /// 运行时数据入口。所有资源从 Assets/Resources/BattleData/ 加载，
    /// 里面的 SO 由 Editor 的 BattleCsvImporter 从 CSV 生成——手改 SO 会在下次导入时被覆盖，
    /// 唯一真源是 Assets/Data/*.csv。
    /// </summary>
    public static class BattleDatabase
    {
        const string Root = "BattleData/";

        static Dictionary<string, SkillDefinition> skillCache;
        static Dictionary<string, CreatureDefinition> creatureCache;
        static Dictionary<Element, EnemySkillSet> setCache;
        static BattleConfig configCache;

        public static void ClearCache()
        {
            skillCache = null;
            creatureCache = null;
            setCache = null;
            configCache = null;
        }

        public static BattleConfig Config
        {
            get
            {
                if (configCache == null)
                {
                    configCache = Resources.Load<BattleConfig>(Root + "BattleConfig");
                    if (configCache == null)
                        Debug.LogError("[BattleDatabase] 找不到 Resources/BattleData/BattleConfig.asset，请先执行 菜单 灵境/导入战斗数据。");
                }
                return configCache;
            }
        }

        static Dictionary<string, SkillDefinition> Skills
        {
            get
            {
                if (skillCache == null)
                {
                    skillCache = new Dictionary<string, SkillDefinition>();
                    var all = Resources.LoadAll<SkillDefinition>(Root + "Skills");
                    for (int i = 0; i < all.Length; i++) skillCache[all[i].id] = all[i];
                }
                return skillCache;
            }
        }

        static Dictionary<string, CreatureDefinition> Creatures
        {
            get
            {
                if (creatureCache == null)
                {
                    creatureCache = new Dictionary<string, CreatureDefinition>();
                    var all = Resources.LoadAll<CreatureDefinition>(Root + "Creatures");
                    for (int i = 0; i < all.Length; i++) creatureCache[all[i].id] = all[i];
                }
                return creatureCache;
            }
        }

        static Dictionary<Element, EnemySkillSet> Sets
        {
            get
            {
                if (setCache == null)
                {
                    setCache = new Dictionary<Element, EnemySkillSet>();
                    var all = Resources.LoadAll<EnemySkillSet>(Root + "EnemySkillSets");
                    for (int i = 0; i < all.Length; i++) setCache[all[i].element] = all[i];
                }
                return setCache;
            }
        }

        public static SkillDefinition GetSkill(string id)
        {
            SkillDefinition s;
            return Skills.TryGetValue(id, out s) ? s : null;
        }

        public static CreatureDefinition GetCreature(string id)
        {
            CreatureDefinition c;
            return Creatures.TryGetValue(id, out c) ? c : null;
        }

        public static EnemySkillSet GetEnemySkillSet(Element element)
        {
            EnemySkillSet s;
            if (Sets.TryGetValue(element, out s)) return s;
            Debug.LogWarning("[BattleDatabase] 没有 " + element + " 属性的敌方技能组，敌人将只会普攻。");
            return null;
        }

        public static List<CreatureDefinition> GetEnemies()
        {
            var list = new List<CreatureDefinition>();
            foreach (var kv in Creatures)
                if (!kv.Value.isPlayerBeast) list.Add(kv.Value);
            return list;
        }

        public static List<CreatureDefinition> GetPlayers()
        {
            var list = new List<CreatureDefinition>();
            foreach (var kv in Creatures)
                if (kv.Value.isPlayerBeast) list.Add(kv.Value);
            return list;
        }
    }
}
