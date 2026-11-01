using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LingJing.UI
{
    /// <summary>
    /// v0.1 主菜单：把「换装 → 编队 → 战斗 → 结算 → 回主菜单」串成一条能反复玩的链。
    /// 和 BattleHud 一样全代码搭 UI，不依赖美术资源。
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public const string BattleScene = "BattlePrototype";
        public const string EquipScene = "EquipScreen";
        public const string SkillScene = "SkillScreen";
        public const string BossScene = "BattleBoss";

        Font font;
        RectTransform canvasRect;

        void Start()
        {
            Application.runInBackground = true;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 22);
            Build();
        }

        void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasRect = canvasGo.GetComponent<RectTransform>();
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = Panel(canvasRect, "Bg", new Color(0.06f, 0.07f, 0.09f, 1f));
            Stretch(bg.rectTransform);

            // 顶部一条竖向渐变压暗/提亮：纯色块会显成一块死板的长条，用渐变过渡才自然
            var glow = Panel(canvasRect, "Glow", Color.white);
            glow.sprite = MakeGradientSprite(new Color(0.30f, 0.24f, 0.13f, 1f), new Color(0f, 0f, 0f, 0f));
            var grt = glow.rectTransform;
            grt.anchorMin = new Vector2(0f, 0.62f);
            grt.anchorMax = new Vector2(1f, 1f);
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;

            Text(canvasRect, "Title", "灵境进化", 96, new Color(1f, 0.93f, 0.72f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(1200f, 110f), new Vector2(0.5f, 1f), new Vector2(0f, -175f));
            Text(canvasRect, "SubTitle", "v0.1 · 战斗手感验证版", 28, new Color(0.72f, 0.78f, 0.88f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(1200f, 40f), new Vector2(0.5f, 1f), new Vector2(0f, -305f));

            float y = 105f;
            Btn(canvasRect, "开始战斗（编队 4 选 3）", new Vector2(0f, y), () => Load(BattleScene));
            Btn(canvasRect, "换装", new Vector2(0f, y - 110f), () => Load(EquipScene));
            Btn(canvasRect, "技能", new Vector2(0f, y - 220f), () => Load(SkillScene));
            Btn(canvasRect, "Boss 演示战", new Vector2(0f, y - 330f), () => Load(BossScene));
            Btn(canvasRect, "退出游戏", new Vector2(0f, y - 440f), Quit);

            Text(canvasRect, "Hint", "点「开始战斗」后先选 3 只兽，开战后编队不可更改", 22,
                new Color(0.6f, 0.66f, 0.76f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(1200f, 34f), new Vector2(0.5f, 0f), new Vector2(0f, 70f));

            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        void Load(string sceneName)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName)) SceneManager.LoadScene(sceneName);
            else Debug.LogError("[MainMenu] 场景没进 Build Settings：" + sceneName);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ 小工具

        /// <summary>运行时生成一张竖向渐变 sprite（2x128 够用，反正会被拉伸）。</summary>
        static Sprite MakeGradientSprite(Color top, Color bottom)
        {
            const int h = 128;
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < h; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(h - 1));
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f));
        }
        static Image Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        Text Text(Transform parent, string name, string content, int size, Color color, TextAnchor align,
                  Vector2 anchor, Vector2 sizeDelta, Vector2 pivot, Vector2 pos)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = pos;
            return t;
        }

        void Btn(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = Color.white;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.18f, 0.24f, 0.32f, 1f);
            colors.highlightedColor = new Color(0.26f, 0.34f, 0.44f, 1f);
            colors.pressedColor = new Color(0.14f, 0.19f, 0.26f, 1f);
            colors.selectedColor = new Color(0.18f, 0.24f, 0.32f, 1f);
            btn.colors = colors;
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(520f, 84f);
            rt.anchoredPosition = pos;

            Text(go.transform, "Label", label, 30, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(500f, 60f), new Vector2(0.5f, 0.5f), Vector2.zero);
        }
    }
}
