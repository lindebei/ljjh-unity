// 一境探索：一张连续地图，自由走动 + 暗雷遇怪（《一境地图与遇敌结构》）。
// 全部用 uGUI 现画，不依赖美术资源。地图 800×800 米缩成正屏一块。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using LingJing.Data;
using LingJing.Battle;

namespace LingJing.UI
{
    public class ExploreScreen : MonoBehaviour
    {
        const string BattleScene = "BattlePrototype";
        const float MoveSpeed = 5f;            // 米/秒（横穿 800 米约 2.7 分钟）
        const float FogCell = 20f;             // 战争迷雾格子（米）
        const int FogGrid = 40;                // 800 / 20
        const float RevealRadius = 150f;   // 起点要多看一点，70 米只能看到脚下一小块
        const float CooldownSeconds = 8f;
        const float MinMetersBetween = 20f;
        const float SafeRadius = 12f;

        RectTransform mapRect;
        RectTransform pawn;
        readonly List<Image> fogCells = new List<Image>();
        Text zoneText, statText, tipText, logText;
        Button challengeButton;
        GameObject challengePanel;
        string challengeBoss;
        int challengeLevel;

        Vector2 pos;
        bool[,] seen = new bool[FogGrid, FogGrid];
        float metersSinceBattle;
        float cooldown;
        float stepAccum;
        float tipTimer;
        int encounterCount;
        readonly System.Random rng = new System.Random();
        readonly List<string> logLines = new List<string>();

        static readonly Color[] ZoneColors =
        {
            new Color(0.16f, 0.30f, 0.42f),   // 水
            new Color(0.42f, 0.20f, 0.14f),   // 火
            new Color(0.16f, 0.34f, 0.28f),   // 风
            new Color(0.34f, 0.28f, 0.14f),   // 土
        };

        void Awake()
        {
            Application.runInBackground = true;
            WorldMap.Reload();
            if (!RunState.Active) RunState.Reset();
            pos = new Vector2(RunState.PosX, RunState.PosY);
            if (pos.sqrMagnitude < 1f) pos = WorldMap.SupplyPoints[0];
        }

        void Start()
        {
            BuildUi();
            RevealFog();
            RefreshHud();
        }

        // ------------------------------------------------------------------ UI
        void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var bg = Panel(canvasGo.transform, "Bg", new Color(0.05f, 0.06f, 0.08f));
            Stretch(bg.rectTransform);

            // 地图容器：800×800 米 → 800×800 像素（1 米 = 1 像素），居中
            var mapGo = Panel(canvasGo.transform, "Map", new Color(0.03f, 0.04f, 0.06f));
            mapRect = mapGo.rectTransform;
            mapRect.anchorMin = new Vector2(0.5f, 0.5f);
            mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapRect.pivot = new Vector2(0.5f, 0.5f);
            mapRect.sizeDelta = new Vector2(WorldMap.Size, WorldMap.Size);
            mapRect.anchoredPosition = new Vector2(0f, -30f);

            // 底噪地貌：生态区之间是有空档的，没有这层底图就是一片纯黑
            var baseTerrain = Panel(mapRect, "Terrain", new Color(0.10f, 0.13f, 0.16f));
            Stretch(baseTerrain.rectTransform);
            baseTerrain.raycastTarget = false;

            // 100 米一格的淡网格，走路时有参照物
            for (int g = 1; g < 8; g++)
            {
                var v = Panel(mapRect, "GridV" + g, new Color(1f, 1f, 1f, 0.05f));
                var vrt = v.rectTransform;
                vrt.anchorMin = new Vector2(0f, 0f);
                vrt.anchorMax = new Vector2(0f, 1f);
                vrt.pivot = new Vector2(0f, 0f);
                vrt.sizeDelta = new Vector2(1f, 0f);
                vrt.anchoredPosition = new Vector2(g * 100f, 0f);

                var hPanel = Panel(mapRect, "GridH" + g, new Color(1f, 1f, 1f, 0.05f));
                var hrt = hPanel.rectTransform;
                hrt.anchorMin = new Vector2(0f, 0f);
                hrt.anchorMax = new Vector2(1f, 0f);
                hrt.pivot = new Vector2(0f, 0f);
                hrt.sizeDelta = new Vector2(0f, 1f);
                hrt.anchoredPosition = new Vector2(0f, g * 100f);
            }

            // 生态区色块 + 名字
            var zones = WorldMap.All;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                var c = ZoneColors[(int)z.Dominant()];
                var cell = Panel(mapRect, "Zone_" + z.id, new Color(c.r, c.g, c.b, 0.85f));
                var rt = cell.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(z.w, z.h);
                rt.anchoredPosition = new Vector2(z.x, z.y);

                Text(mapRect, "ZoneName_" + z.id, "<b>" + z.displayName + "</b>\n" + ElementWord(z.Dominant()) + "·Lv" + z.minLevel + "–" + z.maxLevel,
                    22, new Color(1f, 0.96f, 0.86f, 0.92f), TextAnchor.MiddleCenter,
                    new Vector2(0f, 0f), new Vector2(Mathf.Max(140f, z.w * 0.6f), 60f),
                    new Vector2(0f, 0f), new Vector2(z.x + z.w * 0.5f, z.y + z.h * 0.5f));
            }

            // 补给点
            for (int i = 0; i < WorldMap.SupplyPoints.Length; i++)
            {
                var sp = WorldMap.SupplyPoints[i];
                var dot = Panel(mapRect, "Supply" + i, i == 0 ? new Color(0.75f, 0.85f, 1f) : new Color(0.45f, 0.95f, 0.75f));
                var rt = dot.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(18f, 18f);
                rt.anchoredPosition = sp;
                Text(mapRect, "SupplyLbl" + i, i == 0 ? "据点" : "补给", 16, new Color(0.8f, 0.95f, 1f),
                    TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(80f, 18f), new Vector2(0.5f, 0.5f), sp + new Vector2(0f, 24f));
            }

            // Boss 地标（光柱 = 一条细长发光矩形）
            BossMarker("小 Boss·8", "boss_mini", WorldMap.MiniBossPos, new Color(0.5f, 0.85f, 1f), 8);
            BossMarker("中 Boss·16", "boss_mid", WorldMap.MidBossPos, new Color(1f, 0.65f, 0.35f), 16);
            BossMarker("大 Boss·22", "boss_big", WorldMap.BigBossPos, new Color(1f, 0.4f, 0.45f), 22);

            // 战争迷雾（盖在地图之上、玩家之下）
            var fogRoot = new GameObject("Fog", typeof(RectTransform)).GetComponent<RectTransform>();
            fogRoot.SetParent(mapRect, false);
            Stretch(fogRoot);
            var fog = fogRoot.gameObject.AddComponent<Image>();
            fog.color = new Color(0f, 0f, 0f, 0f);
            fog.raycastTarget = false;
            for (int gy = 0; gy < FogGrid; gy++)
            {
                for (int gx = 0; gx < FogGrid; gx++)
                {
                    var cellGo = new GameObject("F" + gx + "_" + gy, typeof(Image));
                    var img = cellGo.GetComponent<Image>();
                    img.color = new Color(0.02f, 0.02f, 0.03f, 0.30f);   // 只压暗，不要压没：地图是连续地形，走之前得能看清轮廓   // 0.92 太黑，地形轮廓全看不见
                    img.raycastTarget = false;
                    var rt = img.rectTransform;
                    rt.SetParent(fogRoot, false);
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(0f, 0f);
                    rt.pivot = new Vector2(0f, 0f);
                    rt.sizeDelta = new Vector2(FogCell, FogCell);
                    rt.anchoredPosition = new Vector2(gx * FogCell, gy * FogCell);
                    fogCells.Add(img);
                }
            }

            // 玩家
            var pawnGo = Panel(mapRect, "Player", new Color(1f, 0.95f, 0.55f));
            pawn = pawnGo.rectTransform;
            pawn.anchorMin = new Vector2(0f, 0f);
            pawn.anchorMax = new Vector2(0f, 0f);
            pawn.pivot = new Vector2(0.5f, 0.5f);
            pawn.sizeDelta = new Vector2(16f, 16f);

            // 顶栏
            zoneText = Text(canvasGo.transform, "Zone", "", 30, new Color(1f, 0.94f, 0.78f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(900f, 44f), new Vector2(0f, 1f), new Vector2(70f, -46f));
            statText = Text(canvasGo.transform, "Stat", "", 24, new Color(0.82f, 0.88f, 0.95f), TextAnchor.MiddleRight,
                new Vector2(1f, 1f), new Vector2(900f, 44f), new Vector2(1f, 1f), new Vector2(-70f, -46f));

            logText = Text(canvasGo.transform, "Log", "", 20, new Color(0.75f, 0.82f, 0.9f), TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(620f, 260f), new Vector2(0f, 1f), new Vector2(70f, -110f));

            tipText = Text(canvasGo.transform, "Tip", "", 26, new Color(1f, 0.9f, 0.6f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(1400f, 50f), new Vector2(0.5f, 0f), new Vector2(0f, 86f));
            tipText.text = "WASD / 方向键 走动 · 走近 Boss 光柱可挑战 · 踩补给点回满并存档";

            // 挑战面板
            challengePanel = new GameObject("Challenge", typeof(RectTransform)).gameObject;
            var cp = challengePanel.AddComponent<Image>();
            cp.color = new Color(0.1f, 0.12f, 0.16f, 0.96f);
            var cpr = cp.rectTransform;
            cpr.SetParent(canvasGo.transform, false);
            cpr.anchorMin = new Vector2(0.5f, 0.5f);
            cpr.anchorMax = new Vector2(0.5f, 0.5f);
            cpr.pivot = new Vector2(0.5f, 0.5f);
            cpr.sizeDelta = new Vector2(520f, 240f);
            cpr.anchoredPosition = Vector2.zero;
            Text(challengePanel.transform, "CTitle", "", 30, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(480f, 60f), new Vector2(0.5f, 1f), new Vector2(0f, -50f));
            challengeButton = Btn(challengePanel.transform, "开战", new Vector2(0f, -10f), new Vector2(220f, 70f), StartChallenge);
            Btn(challengePanel.transform, "先不打", new Vector2(0f, -100f), new Vector2(220f, 62f), () => challengePanel.SetActive(false));
            challengePanel.SetActive(false);

            // 底部按钮
            Btn(canvasGo.transform, "回主菜单", new Vector2(-820f, 46f), new Vector2(220f, 64f), () => SceneManager.LoadScene("MainMenu"));
        }

        void BossMarker(string label, string nodeId, Vector2 at, Color color, int level)
        {
            var pillar = Panel(mapRect, "Pillar_" + nodeId, new Color(color.r, color.g, color.b, 0.35f));
            var rt = pillar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(26f, 120f);
            rt.anchoredPosition = at;

            var core = Panel(mapRect, "Core_" + nodeId, color);
            var crt = core.rectTransform;
            crt.anchorMin = new Vector2(0f, 0f);
            crt.anchorMax = new Vector2(0f, 0f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(20f, 20f);
            crt.anchoredPosition = at;

            Text(mapRect, "BossLbl_" + nodeId, label, 18, color, TextAnchor.MiddleCenter,
                new Vector2(0f, 0f), new Vector2(160f, 22f), new Vector2(0.5f, 0.5f), at + new Vector2(0f, 134f));
        }

        // ------------------------------------------------------------------ 主循环
        void Update()
        {
            if (challengePanel != null && challengePanel.activeSelf) return;

            Vector2 dir = Vector2.zero;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir.y -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir.x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir.x += 1f;
            }
            if (dir.sqrMagnitude > 0.001f) dir = dir.normalized;

            float dt = Time.deltaTime;
            if (cooldown > 0f) cooldown -= dt;

            if (dir != Vector2.zero)
            {
                var next = pos + dir * MoveSpeed * dt;
                next.x = Mathf.Clamp(next.x, 0f, WorldMap.Size);
                next.y = Mathf.Clamp(next.y, 0f, WorldMap.Size);

                // 雾障硬门槛：等级不够，进不去灵坛
                var z = WorldMap.ZoneAt(next.x, next.y);
                if (z != null && z.gateLevel > 0 && RunState.PlayerLevel < z.gateLevel)
                {
                    ShowTip("雾障把你推了回去 —— 需要 Lv" + z.gateLevel + "（现在 Lv" + RunState.PlayerLevel + "）");
                }
                else
                {
                    float moved = Vector2.Distance(pos, next);
                    pos = next;
                    stepAccum += moved;
                    metersSinceBattle += moved;
                    RunState.PosX = pos.x;
                    RunState.PosY = pos.y;
                    RevealFog();
                    TryEncounter(moved);
                }
            }

            if (pawn != null) pawn.anchoredPosition = pos;
            if (tipTimer > 0f) { tipTimer -= dt; if (tipTimer <= 0f) tipText.text = ""; }

            RefreshHud();
            CheckLandmarks();
        }

        void RefreshHud()
        {
            if (zoneText == null) return;
            var z = WorldMap.ZoneAt(pos.x, pos.y);
            zoneText.text = z != null
                ? "<b>" + z.displayName + "</b>   " + ElementWord(z.Dominant()) + "属性活跃 · 怪物 Lv" + z.minLevel + "–" + z.maxLevel + " · 遇敌 " + (z.ratePerMeter * 100f).ToString("F1") + "%／米"
                : "雾潮浅滩 · 未标记区域";

            string hp = "";
            for (int i = 0; i < RunState.PartyIds.Count && i < RunState.HpPct.Count; i++)
                hp += (i > 0 ? "  " : "") + ShortName(RunState.PartyIds[i]) + " " + (RunState.HpPct[i] * 100f).ToString("F0") + "%";
            statText.text = "Lv " + RunState.PlayerLevel + "  ·  战斗 " + encounterCount + " 场  ·  " + hp;
        }

        void CheckLandmarks()
        {
            string boss = WorldMap.BossIdNear(pos);
            if (boss == null) { if (challengePanel.activeSelf) challengePanel.SetActive(false); return; }
            if (challengePanel.activeSelf && challengeBoss == boss) return;

            challengeBoss = boss;
            challengeLevel = boss == "boss_mini" ? 8 : (boss == "boss_mid" ? 16 : 22);
            var title = challengePanel.transform.Find("CTitle").GetComponent<Text>();
            title.text = boss == "boss_mini" ? "潮喉水母王  Lv8" : (boss == "boss_mid" ? "赤脊虎王  Lv16" : "雾潮之主骸蛇  Lv22");
            challengePanel.SetActive(true);
        }

        void StartChallenge()
        {
            RunState.PendingNode = challengeBoss;
            RunState.PendingLevel = challengeLevel;
            RunState.PendingZone = "";
            RunState.Save();
            SceneManager.LoadScene(BattleScene);
        }

        void TryEncounter(float moved)
        {
            if (cooldown > 0f || metersSinceBattle < MinMetersBetween) return;
            var z = WorldMap.ZoneAt(pos.x, pos.y);
            if (z == null) return;

            Vector2 sp;
            if (WorldMap.NearSupply(pos, out sp) && Vector2.Distance(pos, sp) <= SafeRadius) return;

            if (rng.NextDouble() >= z.ratePerMeter * moved) return;

            int level = WorldMap.RollLevel(z, RunState.PlayerLevel, rng);
            RunState.PendingNode = "wild";
            RunState.PendingZone = z.id;
            RunState.PendingLevel = level;
            RunState.Save();
            encounterCount++;
            AddLog("遭遇！" + z.displayName + " · Lv" + level);
            SceneManager.LoadScene(BattleScene);
        }

        void RevealFog()
        {
            int cx = Mathf.FloorToInt(pos.x / FogCell);
            int cy = Mathf.FloorToInt(pos.y / FogCell);
            int r = Mathf.CeilToInt(RevealRadius / FogCell);
            for (int gy = cy - r; gy <= cy + r; gy++)
            {
                for (int gx = cx - r; gx <= cx + r; gx++)
                {
                    if (gx < 0 || gy < 0 || gx >= FogGrid || gy >= FogGrid) continue;
                    if (seen[gx, gy]) continue;
                    float dx = (gx + 0.5f) * FogCell - pos.x;
                    float dy = (gy + 0.5f) * FogCell - pos.y;
                    if (dx * dx + dy * dy > RevealRadius * RevealRadius) continue;
                    seen[gx, gy] = true;
                    var img = fogCells[gy * FogGrid + gx];
                    if (img != null) img.color = new Color(0f, 0f, 0f, 0f);
                }
            }

            // 踩到补给点：回满血、存档
            Vector2 at;
            if (WorldMap.NearSupply(pos, out at))
            {
                bool already = Vector2.Distance(at, lastSupply) < 1f;
                if (!already)
                {
                    lastSupply = at;
                    for (int i = 0; i < RunState.HpPct.Count; i++) RunState.HpPct[i] = 1f;
                    RunState.Save();
                    AddLog("补给点：血量回满，已存档");
                    ShowTip("补给点：回满血 · 已存档");
                }
            }
        }

        Vector2 lastSupply = new Vector2(-999f, -999f);

        void AddLog(string line)
        {
            logLines.Add(line);
            while (logLines.Count > 9) logLines.RemoveAt(0);
            if (logText != null) logText.text = string.Join("\n", logLines.ToArray());
        }

        void ShowTip(string s) { if (tipText != null) { tipText.text = s; tipTimer = 2.2f; } }

        static string ElementWord(Element e)
        {
            switch (e) { case Element.Water: return "水"; case Element.Fire: return "火"; case Element.Wind: return "风"; default: return "土"; }
        }

        static string ShortName(string id)
        {
            switch (id)
            {
                case "beast_water": return "水灵";
                case "beast_fire": return "火灵";
                case "beast_wind": return "风灵";
                case "beast_earth": return "土灵";
                default: return id;
            }
        }

        // ------------------------------------------------------------------ 小工具
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

        static Text Text(Transform parent, string name, string content, int size, Color color, TextAnchor align,
            Vector2 anchor, Vector2 sizeDelta, Vector2 pivot, Vector2 position)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, size);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = content;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = position;
            return t;
        }

        static Button Btn(Transform parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.24f, 0.31f, 0.42f);
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(1f, 1f, 1f, 0.6f);
            btn.colors = cb;
            btn.onClick.AddListener(onClick);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;

            var t = Text(go.transform, "Label", label, 24, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), size, new Vector2(0.5f, 0.5f), Vector2.zero);
            Stretch(t.rectTransform);
            return btn;
        }
    }
}
