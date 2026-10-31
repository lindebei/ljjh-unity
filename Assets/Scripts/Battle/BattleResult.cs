using System.Collections.Generic;
using UnityEngine;
using LingJing.Data;

namespace LingJing.Battle
{
    /// <summary>
    /// 战斗结果。字段刻意跟 sim/ 的输出对齐（时长 / 我方剩血% / 存活数 / 敌方剩血%），
    /// 这样 Unity 端和 Python 端能直接比数字。
    /// </summary>
    public struct BattleResult
    {
        public float durationSeconds;
        public float playerHpPercent;
        public int playersAlive;
        public float enemyHpPercent;
        public int rounds;
        public int playerActions;
        public bool timedOut;

        /// <summary>本场掉到的装备 id（按掉落顺序，重复不合并）。</summary>
        public List<string> loot;

        public override string ToString()
        {
            return string.Format("{0,6:F1}s  剩血 {1,5:F1}%  存活 {2}/3  敌方剩 {3,5:F1}%  回合 {4}",
                durationSeconds, playerHpPercent * 100f, playersAlive, enemyHpPercent * 100f, rounds);
        }
    }
}
