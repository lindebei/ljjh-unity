// 营地（家园）界面：游戏真正的第一屏。
// 三只兽站在营地里，左侧是整备入口，右下「出发」进入一境地图。
// 和 MainMenu 一样全代码搭 UI，皮肤用 Assets/Resources/Art/UI 下的水彩纸资源（没导入就退回纯色）。
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LingJing.UI
{
    public class CampScreen : MonoBehaviour
    {
        public const string ExploreScene = "Explore";
        public const string EquipScene = "EquipScreen";
        public const string SkillScene = "SkillScreen";

        static readonly string[] PartyIds = { "beast_water", "beast_fire", "beast_wind" };
        const int PlayerLevel = 24;

        Font font;
        RectTransform canvasRect;
        Text tipText;

        void Start()
        {
            Application.runInBackground = true;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 22);
            Build();
        }

        // ------------------------------------------------------------------ 搭 UI

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

            // 天空：纸底 + 上方暖色渐变（和主菜单同一套做法，纯色块会显成一条死板长条）
            var bg = Panel(canvasRect, "Bg", new Color(0.94f, 0.91f, 0.84f, 1f));
            Stretch(bg.rectTransform);

            var sky = Panel(canvasRect, "Sky", Color.white);
            sky.sprite = Gradient(new Color(0.42f, 0.36f, 0.24f, 1f), new Color(0.94f, 0.91f, 0.84f, 0f));
            var srt = sky.rectTransform;
            srt.anchorMin = new Vector2(0f, 0.55f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = Vector2.zero;
            srt.offsetMax = Vector2.zero;

            // 地面：一条横向纸带
            var ground = Panel(canvasRect, "Ground", new Color(0.80f, 0.74f, 0.60f, 1f));
            var grt = ground.rectTransform;
            grt.anchorMin = new Vector2(0f, 0f);
            grt.anchorMax = new Vector2(1f, 0.42f);
            grt.offsetMin = Vector2.zero;
            grt.offsetMax = Vector2.zero;

            var groundLine = Panel(ground.transform, "GroundLine", new Color(0.55f, 0.48f, 0.36f, 0.55f));
            var glrt = groundLine.rectTransform;
            glrt.anchorMin = new Vector2(0f, 1f);
            glrt.anchorMax = new Vector2(1f, 1f);
            glrt.pivot = new Vector2(0.5f, 1f);
            glrt.sizeDelta = new Vector2(0f, 4f);
            glrt.anchoredPosition = Vector2.zero;

            // 两顶帐篷（左后 / 右后）
            MakeTent(new Vector2(-520f, 120f));
            MakeTent(new Vector2(430f, 150f));


            // 三只兽：站在地面上，等距排开
            for (int i = 0; i < PartyIds.Length; i++)
            {
                float x = (i - 1) * 330f;
                MakeBeast(PartyIds[i], x, 250f);
            }
            // 篝火：一团暖光 + 火苗
            var fire = Panel(canvasRect, "Campfire", new Color(1f, 0.65f, 0.28f, 0.75f));
            var frt = fire.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0f);
            frt.pivot = new Vector2(0.5f, 0f);
            frt.sizeDelta = new Vector2(120f, 60f);
            frt.anchoredPosition = new Vector2(0f, 150f);
            var glow = Panel(canvasRect, "FireGlow", new Color(1f, 0.72f, 0.35f, 0.16f));
            var gprt = glow.rectTransform;
            gprt.anchorMin = gprt.anchorMax = new Vector2(0.5f, 0f);
            gprt.pivot = new Vector2(0.5f, 0f);
            gprt.sizeDelta = new Vector2(420f, 220f);
            gprt.anchoredPosition = new Vector2(0f, 110f);
            glow.sprite = Radial(new Color(1f, 0.72f, 0.35f, 0.30f), new Color(1f, 0.72f, 0.35f, 0f));

            // 顶栏
            Text(canvasRect, "Title", "营地", 64, new Color(0.20f, 0.16f, 0.12f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(400f, 80f), new Vector2(0f, 1f), new Vector2(60f, -56f));
            Text(canvasRect, "Sub", "整备好了就出发 —— 一境的地图在雾里等着", 24, new Color(0.34f, 0.28f, 0.22f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(700f, 34f), new Vector2(0f, 1f), new Vector2(62f, -110f));

            // 左侧整备栏
            var side = Panel(canvasRect, "SidePanel", Color.white);
            side.sprite = Skin("panel_side");
            side.type = Image.Type.Sliced;
            var srt2 = side.rectTransform;
            srt2.anchorMin = new Vector2(0f, 0.5f);
            srt2.anchorMax = new Vector2(0f, 0.5f);
            srt2.pivot = new Vector2(0f, 0.5f);
            srt2.sizeDelta = new Vector2(300f, 560f);
            srt2.anchoredPosition = new Vector2(60f, -40f);

            Text(side.transform, "SideTitle", "整备", 30, new Color(0.24f, 0.19f, 0.14f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(260f, 40f), new Vector2(0.5f, 1f), new Vector2(0f, -36f));

            MenuBtn(side.transform, "队伍", new Vector2(0f, -40f), () => Tip("队伍编成还没做 —— 现在固定三只"));
            MenuBtn(side.transform, "装备", new Vector2(0f, -110f), () => Load(EquipScene));
            MenuBtn(side.transform, "技能", new Vector2(0f, -180f), () => Load(SkillScene));
            MenuBtn(side.transform, "图鉴", new Vector2(0f, -250f), () => Tip("图鉴占位：后面放已见过的怪与 Boss"));

            // 出发（右下角最大的一颗）
            var go = new GameObject("Btn_出发", typeof(Image), typeof(Button));
            go.transform.SetParent(canvasRect, false);
            var img = go.GetComponent<Image>();
            img.sprite = Skin("btn_primary");
            img.type = Image.Type.Sliced;
            img.color = img.sprite != null ? Color.white : new Color(0.55f, 0.40f, 0.22f, 1f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Depart);
            var brt = go.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(1f, 0f);
            brt.sizeDelta = new Vector2(360f, 108f);
            brt.anchoredPosition = new Vector2(-80f, 80f);
            var labelColor = img.sprite != null ? new Color(0.22f, 0.15f, 0.09f) : Color.white;
            Text(go.transform, "Label", "出发 · 进入一境", 34, labelColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(340f, 60f), new Vector2(0.5f, 0.5f), Vector2.zero);

            tipText = Text(canvasRect, "Tip", "", 22, new Color(0.30f, 0.24f, 0.18f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(900f, 32f), new Vector2(0.5f, 0f), new Vector2(0f, 30f));

            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            if (LingJing.Battle.RunState.Active)
                SetTip("上次的探索还在：第 " + LingJing.Battle.RunState.Floor + " 层（点「出发」继续）");
            else
                SetTip("点「出发」进入一境：走地图 → 遇怪 → 打或逃 → 回营地/回补给点");
        }

        void MakeTent(Vector2 pos)
        {
            var tent = Panel(canvasRect, "Tent", new Color(0.72f, 0.60f, 0.42f, 1f));
            var trt = tent.rectTransform;
            trt.anchorMin = new Vector2(0.5f, 0f);
            trt.anchorMax = new Vector2(0.5f, 0f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(240f, 190f);
            trt.anchoredPosition = new Vector2(pos.x, 170f);
            var door = Panel(tent.transform, "Door", new Color(0.36f, 0.28f, 0.20f, 1f));
            var drt = door.rectTransform;
            drt.anchorMin = new Vector2(0.5f, 0f);
            drt.anchorMax = new Vector2(0.5f, 0f);
            drt.pivot = new Vector2(0.5f, 0f);
            drt.sizeDelta = new Vector2(70f, 110f);
            drt.anchoredPosition = Vector2.zero;
            Text(tent.transform, "TentLabel", "营地", 20, new Color(0.34f, 0.26f, 0.18f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(200f, 26f), new Vector2(0.5f, 1f), new Vector2(0f, -6f));
        }

        void MakeBeast(string id, float x, float height)
        {
            var holder = new GameObject("Beast_" + id, typeof(RectTransform));
            holder.transform.SetParent(canvasRect, false);
            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.sizeDelta = new Vector2(260f, height);
            hrt.anchoredPosition = new Vector2(x, 150f);

            var pic = Panel(holder.transform, "Pic", Color.white);
            pic.preserveAspect = true;
            pic.raycastTarget = false;
            var sprite = Resources.Load<Sprite>("Art/Creatures/" + id);
            pic.enabled = sprite != null;
            pic.sprite = sprite;
            var prt = pic.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(240f, 240f);
            prt.anchoredPosition = Vector2.zero;

            var name = Resources.Load<LingJing.Data.CreatureDefinition>("BattleData/Creatures/" + id);
            string label = name != null ? name.displayName : id;
            Text(holder.transform, "Name", label + "  Lv" + PlayerLevel, 24, new Color(0.22f, 0.17f, 0.12f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(240f, 32f), new Vector2(0.5f, 0f), new Vector2(0f, -6f));
        }

        // ------------------------------------------------------------------ 交互

        void Depart()
        {
            if (!LingJing.Battle.RunState.Active) LingJing.Battle.RunState.Reset();
            Load(ExploreScene);
        }

        void Load(string sceneName)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName)) SceneManager.LoadScene(sceneName);
            else SetTip("场景没进 Build Settings：" + sceneName);
        }

        void Tip(string msg) { SetTip(msg); }

        void SetTip(string msg)
        {
            if (tipText != null) tipText.text = msg;
            Debug.Log("[Camp] " + msg);
        }

        // ------------------------------------------------------------------ 小工具

        void MenuBtn(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Menu_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = Skin("btn_small");
            img.type = Image.Type.Sliced;
            img.color = img.sprite != null ? Color.white : new Color(0.30f, 0.36f, 0.46f, 1f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(240f, 58f);
            rt.anchoredPosition = pos;
            var c = img.sprite != null ? new Color(0.20f, 0.16f, 0.12f) : Color.white;
            Text(go.transform, "Label", label, 26, c, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(220f, 40f), new Vector2(0.5f, 0.5f), Vector2.zero);
        }

        static Sprite Skin(string name) { return Resources.Load<Sprite>("Art/UI/" + name); }

        static Sprite Gradient(Color top, Color bottom)
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

        static Sprite Radial(Color center, Color edge)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - n * 0.5f) / (n * 0.5f), dy = (y - n * 0.5f) / (n * 0.5f);
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    tex.SetPixel(x, y, Color.Lerp(center, edge, d));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
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
    }
}