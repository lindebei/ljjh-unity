using UnityEngine;

namespace LingJing.UI
{
    /// <summary>
    /// 受击位移：被打时立绘左右抖一下再回位。
    /// 卡片本身挂在 HorizontalLayoutGroup 下，改它的 anchoredPosition 会被布局覆盖，
    /// 所以抖的是卡片里的立绘子物体。
    /// </summary>
    public class HitShake : MonoBehaviour
    {
        Vector2 basePos;
        float amp = 6f;
        float life = 0.18f;
        float t;
        bool running;

        public void Init(Vector2 origin, float amplitude, float duration)
        {
            basePos = origin;
            amp = amplitude;
            life = Mathf.Max(0.05f, duration);
            t = 0f;
            running = true;
        }

        void Update()
        {
            if (!running) return;
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / life);
            // 三次往返，幅度线性收紧
            float offset = Mathf.Sin(t / life * Mathf.PI * 6f) * amp * k;
            var rt = transform as RectTransform;
            if (rt != null) rt.anchoredPosition = basePos + new Vector2(offset, 0f);

            if (t >= life)
            {
                if (rt != null) rt.anchoredPosition = basePos;
                running = false;
                Destroy(this);
            }
        }
    }
}
