// 一境地图：显示当前层数、队伍血量、三个待选节点。点节点 → 记下类型 → 进战斗/休息。
using System.Collections.Generic;
using LingJing.Battle;
using LingJing.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LingJing.UI
{
    public class RunMapScreen : MonoBehaviour
    {
        const string BattleScene = "BattlePrototype";

        Font font;
        Text header;
        readonly List<Text> partyTexts = new List<Text>();
        readonly List<Button> nodeButtons = new List<Button>();
        readonly List<Text> nodeTexts = new List<Text>();
        readonly string[] nodeKinds = new string[3];

        void Start()
        {
            Application.runInBackground = true;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 22);
            RunState.Load();
            if (!RunState.Active) RunState.Reset();
            Build();
            Refresh();
        }

        void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            var canvasRect = canvasGo.GetComponent<RectTransform>();

            var bg = Panel(canvasRect, "Bg", new Color(0.05f, 0.06f, 0.08f));
            Stretch(bg.rectTransform);

            header = Text(canvasRect, "Header", "", 44, new Color(1f, 0.92f, 0.72f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(900f, 70f), new Vector2(0.5f, 1f), new Vector2(0f, -90f));

            for (int i = 0; i < 3; i++)
            {
                var t = Text(canvasRect, "Party" + i, "", 26, Color.white, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(520f, 40f), new Vector2(0f, 1f), new Vector2(120f, -190f - i * 48f));
                partyTexts.Add(t);
            }

            for (int i = 0; i < 3; i++)
            {
                var btn = Button(canvasRect, "Node" + i, new Vector2(-360f + i * 360f, -60f), new Vector2(300f, 220f));
                int idx = i;
                btn.onClick.AddListener(() => OnNode(idx));
                nodeButtons.Add(btn);
                nodeTexts.Add(btn.GetComponentInChildren<Text>());
            }

            Button(canvasRect, "休整一次", new Vector2(-560f, -350f), new Vector2(240f, 70f)).onClick.AddListener(OnRestAll);
            Button(canvasRect, "回主菜单", new Vector2(560f, -350f), new Vector2(240f, 70f)).onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));
        }

        void Refresh()
        {
            header.text = string.Format("一境 · 第 {0} 层    掉落 {1} 件", RunState.Floor, RunState.Loot.Count);
            for (int i = 0; i < partyTexts.Count; i++)
            {
                string id = i < RunState.PartyIds.Count ? RunState.PartyIds[i] : "-";
                float hp = i < RunState.HpPct.Count ? RunState.HpPct[i] : 1f;
                var def = BattleDatabase.GetCreature(id);
                partyTexts[i].text = string.Format("{0}  {1}%", def != null ? def.displayName : id, Mathf.RoundToInt(hp * 100f));
                partyTexts[i].color = Color.Lerp(new Color(0.9f, 0.3f, 0.3f), new Color(0.6f, 1f, 0.7f), hp);
            }

            // 节点：第 1 层固定普通，之后按层数给精英/Boss/休整
            nodeKinds[0] = "normal";
            nodeKinds[1] = RunState.Floor % 3 == 0 ? "boss" : "elite";
            nodeKinds[2] = "rest";
            for (int i = 0; i < 3; i++)
            {
                nodeTexts[i].text = NodeLabel(nodeKinds[i]);
                nodeTexts[i].color = Color.white;
            }
        }

        static string NodeLabel(string kind)
        {
            switch (kind)
            {
                case "normal": return "普通遭遇\n低风险";
                case "elite": return "精英遭遇\n风险高";
                case "boss": return "首领遭遇\n最高风险";
                default: return "休整\n全队回 30%";
            }
        }

        void OnNode(int idx)
        {
            var kind = nodeKinds[idx];
            if (kind == "rest") { OnRestAll(); return; }
            RunState.PendingNode = kind;
            RunState.Save();
            SceneManager.LoadScene(BattleScene);
        }

        void OnRestAll()
        {
            RunState.Rest();
            Refresh();
        }

        // ---------------------------------------------------------------- 小工具

        static Image Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
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
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = pos;
            return t;
        }

        Button Button(Transform parent, string label, Vector2 centerPos, Vector2 size)
        {
            var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = Color.white;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = centerPos;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.22f, 0.30f, 0.40f);
            colors.highlightedColor = new Color(0.28f, 0.38f, 0.50f);
            colors.pressedColor = new Color(0.18f, 0.25f, 0.34f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.20f, 0.22f, 0.26f);
            btn.colors = colors;

            var text = Text(go.transform, "Label", label, 26, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), size, new Vector2(0.5f, 0.5f), Vector2.zero);
            text.raycastTarget = false;
            return btn;
        }
    }
}