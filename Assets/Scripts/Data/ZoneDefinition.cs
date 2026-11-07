// 生态区定义：地图上的一块矩形区域，决定遇敌率、怪物等级区间与属性倾向。
using System;
using UnityEngine;

namespace LingJing.Data
{
    [CreateAssetMenu(fileName = "Zone", menuName = "灵境/生态区")]
    public class ZoneDefinition : ScriptableObject
    {
        [Header("标识")]
        public string id = "zone_spring";
        public string displayName = "水泽";

        [Header("地图矩形（米，左下角原点）")]
        public float x;
        public float y;
        public float w = 200f;
        public float h = 200f;

        [Header("遇敌")]
        [Tooltip("每移动 1 米触发遇敌的概率")]
        public float ratePerMeter = 0.008f;
        public int minLevel = 1;
        public int maxLevel = 8;

        [Header("属性倾向（权重，不需要归一化）")]
        public float water = 60f;
        public float fire = 10f;
        public float wind = 15f;
        public float earth = 15f;

        [Header("进入门槛（0 = 无门槛）")]
        [Tooltip("低于这个等级会被雾障推出来")]
        public int gateLevel = 0;

        public string note = string.Empty;

        public bool Contains(float px, float py)
        {
            return px >= x && px <= x + w && py >= y && py <= y + h;
        }

        public Vector2 Center { get { return new Vector2(x + w * 0.5f, y + h * 0.5f); } }

        /// <summary>按权重挑一个属性。</summary>
        public Element RollElement(System.Random rng)
        {
            float total = water + fire + wind + earth;
            if (total <= 0f) return Element.Water;
            float r = (float)rng.NextDouble() * total;
            if ((r -= water) < 0f) return Element.Water;
            if ((r -= fire) < 0f) return Element.Fire;
            if ((r -= wind) < 0f) return Element.Wind;
            return Element.Earth;
        }

        /// <summary>属性占比最高的是哪个（给 UI 显示「火属性活跃」）。</summary>
        public Element Dominant()
        {
            float max = Mathf.Max(Mathf.Max(water, fire), Mathf.Max(wind, earth));
            if (max == water) return Element.Water;
            if (max == fire) return Element.Fire;
            if (max == wind) return Element.Wind;
            return Element.Earth;
        }
    }
}
