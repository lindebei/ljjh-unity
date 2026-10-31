using UnityEngine;

namespace LingJing.Data
{
    /// <summary>
    /// 掉落表的一行：某只怪有 chance 的概率掉某件装备。
    /// 一境只有 5 件装备（每槽一件），所以这张表很小，不做品质与随机词条。
    /// </summary>
    [CreateAssetMenu(menuName = "灵境/掉落项", fileName = "drop_new")]
    public class LootEntry : ScriptableObject
    {
        public string enemyId;
        public string itemId;
        [Range(0f, 1f)] public float chance = 0.1f;
    }
}
