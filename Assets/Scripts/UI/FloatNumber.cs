using UnityEngine;
using UnityEngine.UI;

namespace LingJing.UI
{
    /// <summary>
    /// 一个往上飘并淡出的数字。用完自己销毁，不做对象池——
    /// v0.1 一场战斗最多几十个飘字，GC 压力可以忽略，先把「看得见」做出来。
    /// </summary>
    public class FloatNumber : MonoBehaviour
    {
        RectTransform rt;
        Text text;
        float riseSpeed;
        float life;
        float elapsed;
        Vector3 startPos;
        Color startColor;

        public void Init(RectTransform rect, Text label, float riseSpeed, float life)
        {
            rt = rect;
            text = label;
            this.riseSpeed = riseSpeed;
            this.life = life;
            startPos = rect.position;
            startColor = label.color;
        }

        void Update()
        {
            if (rt == null) { Destroy(gameObject); return; }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, life));

            // 先快后慢地上升，后半程开始淡出
            rt.position = startPos + new Vector3(0f, riseSpeed * (1f - (1f - t) * (1f - t)), 0f);

            var c = startColor;
            c.a = t < 0.55f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
            if (text != null) text.color = c;

            if (t >= 1f) Destroy(gameObject);
        }
    }
}
