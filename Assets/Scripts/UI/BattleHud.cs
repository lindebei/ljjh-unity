using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LingJing.Data;
using LingJing.Battle;

namespace LingJing.UI
{
    /// <summary>
    /// v0.1 战斗界面：全部用代码搭，不依赖任何美术资源。
    /// 目的只有一个——验证「速度条顺序可读、共享灵能有取舍、三兽轮流有操作感」，
    /// 所以刻意做得很朴素：左边行动条、中间血条、下边四个技能按钮、右边日志。
    /// </summary>
    public class BattleHud : MonoBehaviour
    {
        [Header("对局配置（Inspector 可改）")]
        public int playerLevel = 24;
        public int enemyLevel = 10;
        public string[] enemyIds = { "en_bonefish", "en_stonelizard", "en_bird" };
        public float timeScale = 1f;
        public bool autoBattle;

        BattleSimulator sim;

        /// <summary>给编辑器脚本（自动截图/批量测试）读状态用。</summary>
        public BattleSimulator Simulator { get { return sim; } }
        Font font;

        readonly List<Text> logLines = new List<Text>();
        readonly Dictionary<BattleUnit, RectTransform> hpFills = new Dictionary<BattleUnit, RectTransform>();
        readonly Dictionary<BattleUnit, Text> hpTexts = new Dictionary<BattleUnit, Text>();
        readonly Dictionary<BattleUnit, RectTransform> gaugeFills = new Dictionary<BattleUnit, RectTransform>();
        readonly Dictionary<BattleUnit, Text> gaugeTexts = new Dictionary<BattleUnit, Text>();
        readonly Dictionary<BattleUnit, RectTransform> cardRects = new Dictionary<BattleUnit, RectTransform>();
        readonly Dictionary<BattleUnit, Image> cardImages = new Dictionary<BattleUnit, Image>();
        readonly Dictionary<BattleUnit, Color> cardBaseColors = new Dictionary<BattleUnit, Color>();
        readonly Dictionary<BattleUnit, Image> portraits = new Dictionary<BattleUnit, Image>();

        Transform playerRow;
        Transform enemyRow;
        Transform gaugeColumn;
        Transform logColumn;
        Text spiritText;
        Text potionText;
        Text statusBanner;
        RectTransform floatLayer;
        Button[] skillButtons = new Button[4];
        Text[] skillLabels = new Text[4];
        Button potionButton;

        float tickAccumulator;
        bool finishShown;
        int popCount;

        /// <summary>飘字累计条数（给自动截图脚本判断「这一帧有数字可看」）。</summary>
        public int PopCount { get { return popCount; } }

        void Start()
        {
            // 编辑器失焦时默认会把 Update 压到很低频，自动截图脚本就会「跑很久也没推几秒战斗」
            Application.runInBackground = true;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 22);
            BuildUi();
            StartBattle();
            BattleUnit.HpChanged += OnHpChanged;
        }

        void OnDestroy()
        {
            BattleUnit.HpChanged -= OnHpChanged;
        }

        // ------------------------------------------------------------------ 飘字

        /// <summary>血量变化 → 在对应卡片上方弹一个数字（伤害橙黄、治疗绿、我方受伤红）。</summary>
        void OnHpChanged(BattleUnit unit, float delta, bool isHeal)
        {
            if (floatLayer == null || !cardRects.ContainsKey(unit)) return;
            if (delta < 0.5f) return;

            var go = new GameObject("Pop_" + unit.id, typeof(Text));
            go.transform.SetParent(floatLayer, false);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = isHeal ? 32 : (delta >= unit.maxHp * 0.12f ? 46 : 36);
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = (isHeal ? "+" : "-") + Mathf.RoundToInt(delta);

            if (isHeal) text.color = new Color(0.45f, 1f, 0.55f);
            else if (unit.side == Side.Enemy) text.color = new Color(1f, 0.85f, 0.30f);
            else text.color = new Color(1f, 0.42f, 0.42f);

            // 受击/受治疗：卡片本身闪一下（比只有数字更容易注意到「谁挨打了」）
            // 闪光色要偏暗——直接用纯白会把整张卡（含立绘）糊成一块白板
            if (isHeal) StartCoroutine(FlashCard(unit, new Color(0.30f, 0.52f, 0.36f), 0.28f));
            else StartCoroutine(FlashCard(unit, unit.side == Side.Enemy ? new Color(0.52f, 0.60f, 0.70f) : new Color(0.58f, 0.26f, 0.26f), 0.22f));

            // 深色描边，避免数字压在血条/卡片底色上看不清
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            var rt = text.rectTransform;
            rt.sizeDelta = new Vector2(240f, 50f);
            popCount++;

            // 起点在卡片中下部（血条下方那片空区），往上飘 55px —— 全程留在卡片范围内，
            // 既不飞进顶部的「灵能/回合/药品」那行，也不跟血条上的 1218/2117 数字打架。
            // 按 popCount 取模错开纵向位置 + 横向大幅散开，否则治疗和伤害同时跳会叠成「+214422」。
            Vector3 basePos = cardRects[unit].position;
            float stagger = (popCount % 3) * 14f;
            rt.position = basePos + new Vector3(UnityEngine.Random.Range(-110f, 110f), -32f + stagger, 0f);

            go.AddComponent<FloatNumber>().Init(rt, text, 50f, 0.9f);
        }

        // ------------------------------------------------------------------ 建界面

        void BuildUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = MakePanel(canvasGo.transform, "Bg", new Color(0.07f, 0.08f, 0.10f, 1f));
            Stretch(bg.rectTransform);

            // 场景背景：上暗下亮的竖向渐变 + 中间一团冷色光晕，卡片才有「站在场地里」的感觉
            var sky = MakePanel(canvasGo.transform, "Sky", Color.white);
            sky.sprite = MakeGradientSprite(new Color(0.05f, 0.06f, 0.10f), new Color(0.13f, 0.16f, 0.22f));
            Stretch(sky.rectTransform);

            var glow = MakePanel(canvasGo.transform, "ArenaGlow", Color.white);
            glow.sprite = MakeRadialSprite(new Color(0.32f, 0.42f, 0.62f, 0.42f), new Color(0f, 0f, 0f, 0f));
            var glr = glow.rectTransform;
            glr.anchorMin = new Vector2(0.5f, 0.5f);
            glr.anchorMax = new Vector2(0.5f, 0.5f);
            glr.sizeDelta = new Vector2(1500f, 760f);
            glr.anchoredPosition = new Vector2(80f, 40f);

            // 左：行动条面板（贴左边，宽 300）
            var gaugePanel = MakePanel(canvasGo.transform, "GaugePanel", new Color(0.11f, 0.12f, 0.15f, 1f));
            var gp = gaugePanel.rectTransform;
            gp.anchorMin = new Vector2(0f, 0f);
            gp.anchorMax = new Vector2(0f, 1f);
            gp.pivot = new Vector2(0f, 0.5f);
            gp.offsetMin = new Vector2(0f, 0f);
            gp.offsetMax = new Vector2(300f, 0f);

            MakeText(gaugePanel.transform, "GaugeTitle", "行动条  0.5s/拍", 20, new Color(0.75f, 0.8f, 0.9f),
                TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(280f, 28f), new Vector2(0f, 1f), new Vector2(16f, -14f));
            gaugeColumn = MakeColumn(gaugePanel.transform, "GaugeList", 10f, new RectOffset(14, 14, 52, 14), false);

            // 上：敌方（怪物在上，贴顶部）；下：我方（自己的兽在底部，紧贴技能栏）
            enemyRow = MakeRow(canvasGo.transform, "Enemies", new Vector2(340f, 100f), new Vector2(1500f, 200f));
            playerRow = MakeRow(canvasGo.transform, "Players", new Vector2(340f, 660f), new Vector2(1500f, 200f));

            spiritText = MakeText(canvasGo.transform, "Spirit", "灵能 4/8", 30, new Color(0.5f, 0.85f, 1f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(420f, 40f), new Vector2(0f, 1f), new Vector2(350f, -60f));
            potionText = MakeText(canvasGo.transform, "Potion", "药品 8", 26, new Color(0.85f, 0.9f, 0.7f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(260f, 40f), new Vector2(0f, 1f), new Vector2(800f, -60f));

            // 横幅放在「我方行」与「敌方行」之间的空档里（320–690 那条带），别压到血条卡片上
            statusBanner = MakeText(canvasGo.transform, "Banner", "", 34, new Color(1f, 0.9f, 0.5f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(1200f, 60f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f));

            // 下：技能栏
            var bar = MakePanel(canvasGo.transform, "SkillBar", new Color(0.11f, 0.12f, 0.15f, 1f));
            var br = bar.rectTransform;
            br.anchorMin = new Vector2(0.5f, 0f);
            br.anchorMax = new Vector2(0.5f, 0f);
            br.pivot = new Vector2(0.5f, 0f);
            br.anchoredPosition = Vector2.zero;
            br.sizeDelta = new Vector2(1520f, 190f);

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var btn = MakeButton(bar.transform, "Skill" + i, "", new Vector2(-570f + i * 290f, 0f), new Vector2(270f, 130f));
                btn.onClick.AddListener(() => OnSkillClicked(idx));
                skillButtons[i] = btn;
                skillLabels[i] = btn.GetComponentInChildren<Text>();
            }
            potionButton = MakeButton(bar.transform, "PotionBtn", "药品\n(35% 血)", new Vector2(660f, 0f), new Vector2(180f, 130f));
            potionButton.onClick.AddListener(OnPotionClicked);

            // 右：日志
            var logPanel = MakePanel(canvasGo.transform, "LogPanel", new Color(0.09f, 0.10f, 0.12f, 1f));
            var lp = logPanel.rectTransform;
            lp.anchorMin = new Vector2(1f, 0f);
            lp.anchorMax = new Vector2(1f, 1f);
            lp.pivot = new Vector2(1f, 0.5f);
            lp.offsetMin = new Vector2(-330f, 0f);
            lp.offsetMax = new Vector2(-10f, 0f);

            MakeText(logPanel.transform, "LogTitle", "战斗日志", 20, new Color(0.75f, 0.8f, 0.9f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(280f, 28f), new Vector2(0f, 1f), new Vector2(14f, -14f));
            logColumn = MakeColumn(logPanel.transform, "LogLines", 4f, new RectOffset(12, 12, 52, 12), true);

            // 飘字层：最后一个子物体，保证画在所有面板之上（不挡点击）
            var floatGo = new GameObject("FloatLayer", typeof(RectTransform));
            floatGo.transform.SetParent(canvasGo.transform, false);
            floatLayer = floatGo.GetComponent<RectTransform>();
            Stretch(floatLayer);

            EnsureEventSystem();
        }

        void EnsureEventSystem()
        {
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            var module = es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            module.enabled = true;
        }

        // ------------------------------------------------------------------ 开打

        void StartBattle()
        {
            var cfg = BattleDatabase.Config;
            if (cfg == null) { Debug.LogError("[BattleHud] 没有 BattleConfig"); return; }

            sim = new BattleSimulator(cfg) { interactive = true, verbose = true };
            int[] free = { 23, 23, 23, 23, 23, 23 };

            string[] players = { "beast_water", "beast_fire", "beast_wind", "beast_earth" };
            // v0.1 规则：4 选 3、编队不可改——这里固定带水/火/风，土灵留给玩家以后自己换
            for (int i = 0; i < 3; i++)
            {
                var def = BattleDatabase.GetCreature(players[i]);
                if (def == null) { Debug.LogError("[BattleHud] 缺少 " + players[i]); return; }
                sim.AddPlayer(def, playerLevel, free);
            }

            int band = cfg.BandIndex(enemyLevel);
            for (int i = 0; i < enemyIds.Length; i++)
            {
                var def = BattleDatabase.GetCreature(enemyIds[i]);
                if (def == null) { Debug.LogError("[BattleHud] 缺少 " + enemyIds[i]); continue; }
                sim.AddEnemy(def, enemyLevel, band);
            }

            sim.Begin();
            sim.ActionStarted += OnActionStarted;
            BuildUnitWidgets();
            RefreshAll();
            PrimeSkillLabels();
        }

        /// <summary>
        /// 开局先把四个按钮写成「1 号兽的技能名」（置灰不可点）。
        /// 不写的话，在轮到玩家之前（以及自动战斗/结算画面）这四个按钮是空方块，看起来像界面坏了。
        /// </summary>
        void PrimeSkillLabels()
        {
            var first = sim.Alive(Side.Player).Count > 0 ? sim.Alive(Side.Player)[0] : null;
            if (first == null) return;

            for (int i = 0; i < 4; i++)
            {
                var skill = first.def.Skill((SkillType)i);
                bool hasSkill = skill != null;
                string cost = !hasSkill ? "-" : (skill.spiritCost < 0 ? "+1" : skill.spiritCost.ToString());
                skillLabels[i].text = !hasSkill ? "—"
                    : string.Format("{0}\n<{1}> 灵能{2}", skill.displayName, SlotName(i), cost);
                skillLabels[i].color = new Color(0.45f, 0.45f, 0.45f);
                skillButtons[i].interactable = false;
            }
        }

        void BuildUnitWidgets()
        {
            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                bool isPlayer = u.side == Side.Player;
                var row = isPlayer ? playerRow : enemyRow;

                var card = MakePanel(row, "Card_" + u.id, isPlayer ? new Color(0.16f, 0.22f, 0.30f, 1f) : new Color(0.30f, 0.16f, 0.16f, 1f));
                card.rectTransform.sizeDelta = new Vector2(460f, 190f);
                cardRects[u] = card.rectTransform;
                cardImages[u] = card;
                cardBaseColors[u] = card.color;
                var le = card.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 460f;
                le.preferredHeight = 190f;

                // 立绘：卡片左侧 150x150，等比缩放（没有图就只留空白，不影响其它布局）
                var portrait = new GameObject("Portrait", typeof(Image)).GetComponent<Image>();
                portrait.transform.SetParent(card.transform, false);
                portrait.sprite = LoadPortrait(u.id);
                portrait.preserveAspect = true;
                portrait.raycastTarget = false;
                portrait.enabled = portrait.sprite != null;
                var prt = portrait.rectTransform;
                prt.anchorMin = new Vector2(0f, 0f);
                prt.anchorMax = new Vector2(0f, 0f);
                prt.pivot = new Vector2(0f, 0f);
                prt.sizeDelta = new Vector2(150f, 160f);
                prt.anchoredPosition = new Vector2(10f, 14f);
                portraits[u] = portrait;

                // 名字 + 血条排在立绘右边
                var col = MakeColumn(card.transform, "Col", 8f, new RectOffset(170, 12, 16, 10), false);
                MakeText(col, "Name", u.displayName + "   Lv" + u.level, 24, Color.white, TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(278f, 30f), new Vector2(0f, 1f), Vector2.zero);

                // 卡片宽 460，立绘占左侧 170（150 图 + 10 边距 + 10 间隙），文字区只剩 278 —— 这里的宽度必须跟着改，
                // 不然血条会伸出卡片右边（childControlWidth=false 时布局不会帮你压窄）
                var hpBar = MakeBar(col, "Hp", new Color(0.85f, 0.25f, 0.25f), 278f, 26f);
                hpFills[u] = hpBar.Item2;
                hpTexts[u] = MakeText(hpBar.Item1, "HpText", "", 17, Color.white, TextAnchor.MiddleCenter,
                    new Vector2(0f, 0f), new Vector2(278f, 26f), new Vector2(0f, 0f), Vector2.zero);
                Stretch(hpTexts[u].rectTransform);

                // 行动条（左侧竖排）
                var gRow = MakePanel(gaugeColumn, "GRow_" + u.id, new Color(0.13f, 0.15f, 0.19f, 1f));
                gRow.rectTransform.sizeDelta = new Vector2(272f, 58f);
                var gle = gRow.gameObject.AddComponent<LayoutElement>();
                gle.preferredHeight = 58f;
                gle.preferredWidth = 272f;

                MakeText(gRow.transform, "GName", u.displayName + "  " + (isPlayer ? "我" : "敌"), 17,
                    isPlayer ? new Color(0.6f, 0.9f, 1f) : new Color(1f, 0.7f, 0.6f), TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(200f, 22f), new Vector2(0f, 1f), new Vector2(8f, -4f));

                gaugeTexts[u] = MakeText(gRow.transform, "GVal", "", 16, Color.white, TextAnchor.MiddleRight,
                    new Vector2(1f, 1f), new Vector2(80f, 22f), new Vector2(1f, 1f), new Vector2(-8f, -4f));

                var gBar = MakeBar(gRow.transform, "Gauge", isPlayer ? new Color(0.35f, 0.75f, 1f) : new Color(1f, 0.55f, 0.35f), 256f, 16f);
                gaugeFills[u] = gBar.Item2;
                var grt = gBar.Item1;
                grt.anchorMin = new Vector2(0f, 0f);
                grt.anchorMax = new Vector2(0f, 0f);
                grt.pivot = new Vector2(0f, 0f);
                grt.anchoredPosition = new Vector2(8f, 8f);
            }
        }

        // ------------------------------------------------------------------ 主循环

        void Update()
        {
            if (sim == null) return;

            if (sim.IsBattleOver)
            {
                if (!finishShown) { finishShown = true; ShowFinish(); }
                return;
            }

            // 自动战斗：轮到玩家立刻替他出手，这一支必须放在「等待玩家」早退之前，
            // 否则 autoBattle 一旦打开就会永远卡在第一个玩家回合上（踩过一次）。
            if (autoBattle && sim.awaitingPlayer != null)
            {
                // 自动战斗也要喝药——否则 UI 自动打一场（不喝药）跟 batchmode 跑同一场景
                // （TakeAction 里会自动喝药）对不上，剩血能差 20 个点。
                if (sim.ShouldUsePotion()) sim.ResolvePlayerPotion();
                else sim.ResolvePlayerTurn(sim.ChoosePlayerSkill(sim.awaitingPlayer));
                ClearButtons();
            }

            if (sim.awaitingPlayer != null)
            {
                UpdateSkillButtons(sim.awaitingPlayer);
                RefreshAll();
                return;
            }

            tickAccumulator += Time.deltaTime * timeScale;
            int guard = 0;
            while (tickAccumulator >= sim.config.tickSeconds && sim.awaitingPlayer == null && !sim.IsBattleOver && guard++ < 60)
            {
                tickAccumulator -= sim.config.tickSeconds;
                sim.StepOnce();
            }

            RefreshAll();
        }

        // ------------------------------------------------------------------ 表现层（出手/命中）

        /// <summary>
        /// 有人出手：在该单位头顶弹一条「名字 · 技能名」（属性色），并让他的卡片弹一下。
        /// v0.1 不做骨骼动画，这三件事（技能名 + 出手方弹动 + 受击方闪白）就够读懂了。
        /// </summary>
        void OnActionStarted(BattleUnit actor, SkillDefinition skill)
        {
            if (actor == null || skill == null) return;
            var color = ElementColor(skill.element);
            StartCoroutine(ShowSkillName(actor, skill, color));
            StartCoroutine(PulseCard(actor, 1.08f, 0.22f));
        }

        System.Collections.IEnumerator ShowSkillName(BattleUnit actor, SkillDefinition skill, Color color)
        {
            float life = 0.9f;
            var go = new GameObject("SkillName", typeof(Text));
            go.transform.SetParent(floatLayer, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = actor.side == Side.Player ? 40 : 34;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.text = actor.displayName + " · " + skill.displayName;
            t.color = color;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            var rt = t.rectTransform;
            rt.sizeDelta = new Vector2(600f, 56f);
            // 名字挂在出手者卡片正上方（我方在上方会压到敌方行，所以往自己那侧偏）
            Vector3 basePos = cardRects[actor].position;
            rt.position = basePos + new Vector3(0f, actor.side == Side.Player ? 108f : -108f, 0f);

            float el = 0f;
            while (el < life && t != null)
            {
                el += Time.deltaTime;
                float k = el / life;
                var c = color;
                c.a = k < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (k - 0.6f) / 0.4f);
                t.color = c;
                rt.position = basePos + new Vector3(0f, (actor.side == Side.Player ? 108f : -108f) + k * (actor.side == Side.Player ? 26f : -26f), 0f);
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        System.Collections.IEnumerator PulseCard(BattleUnit unit, float scale, float duration)
        {
            var rt = cardRects.ContainsKey(unit) ? cardRects[unit] : null;
            if (rt == null) yield break;
            float el = 0f;
            while (el < duration && rt != null)
            {
                el += Time.deltaTime;
                float k = el / duration;
                // 0→1 先放大再回落
                float s = 1f + (scale - 1f) * Mathf.Sin(k * Mathf.PI);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
        }

        /// <summary>受击/受治疗：卡片底色闪一下再回到原色。</summary>
        System.Collections.IEnumerator FlashCard(BattleUnit unit, Color flash, float duration)
        {
            if (!cardImages.ContainsKey(unit)) yield break;
            var img = cardImages[unit];
            var baseColor = cardBaseColors[unit];
            float el = 0f;
            while (el < duration && img != null)
            {
                el += Time.deltaTime;
                img.color = Color.Lerp(flash, baseColor, el / duration);
                yield return null;
            }
            if (img != null) img.color = baseColor;
        }

        // ------------------------------------------------------------------ 交互

        void OnSkillClicked(int slot)
        {
            var actor = sim.awaitingPlayer;
            if (actor == null) return;
            var skill = actor.def.Skill((SkillType)slot);
            if (skill == null) { Flash("这个槽位是空的"); return; }
            if (skill.spiritCost > 0 && sim.spirit < skill.spiritCost) { Flash("灵能不够"); return; }
            if (actor.Cooldown(skill.id) > 0) { Flash("技能冷却中"); return; }

            sim.ResolvePlayerTurn(skill);
            ClearButtons();
            RefreshAll();
        }

        void OnPotionClicked()
        {
            if (sim.awaitingPlayer == null) return;
            sim.ResolvePlayerPotion();
            ClearButtons();
            RefreshAll();
        }

        void UpdateSkillButtons(BattleUnit actor)
        {
            statusBanner.text = "轮到 " + actor.displayName + " —— 选一个技能";
            statusBanner.color = new Color(1f, 0.9f, 0.5f);

            for (int i = 0; i < 4; i++)
            {
                var skill = actor.def.Skill((SkillType)i);
                bool hasSkill = skill != null;
                bool usable = hasSkill
                              && (skill.spiritCost <= 0 || sim.spirit >= skill.spiritCost)
                              && actor.Cooldown(skill.id) <= 0;

                skillButtons[i].interactable = usable;
                string cost = !hasSkill ? "-" : (skill.spiritCost < 0 ? "+1" : skill.spiritCost.ToString());
                skillLabels[i].text = !hasSkill ? "—"
                    : string.Format("{0}\n<{1}> 灵能{2}", skill.displayName, SlotName(i), cost);
                skillLabels[i].color = usable ? Color.white : new Color(0.45f, 0.45f, 0.45f);
            }
            potionButton.interactable = sim.potions > 0;
        }

        void ClearButtons()
        {
            // 只置灰、不清文字：清空会留下四个黑方块，看起来像界面坏了
            for (int i = 0; i < 4; i++)
            {
                skillButtons[i].interactable = false;
                skillLabels[i].color = new Color(0.45f, 0.45f, 0.45f);
            }
            potionButton.interactable = false;
            statusBanner.text = "";
        }

        static string SlotName(int i)
        {
            switch (i)
            {
                case 0: return "普攻";
                case 1: return "主动";
                case 2: return "辅助";
                default: return "奥义";
            }
        }

        // ------------------------------------------------------------------ 刷新

        void RefreshAll()
        {
            if (sim == null) return;

            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                if (hpFills.ContainsKey(u))
                {
                    hpFills[u].anchorMax = new Vector2(Mathf.Clamp01(u.hp / Mathf.Max(1f, u.maxHp)), 1f);
                    hpTexts[u].text = string.Format("{0:F0}/{1:F0}{2}", u.hp, u.maxHp,
                        u.shield > 0.5f ? "   盾 " + u.shield.ToString("F0") : "");
                }
                if (gaugeFills.ContainsKey(u))
                {
                    gaugeFills[u].anchorMax = new Vector2(Mathf.Clamp01(u.gauge / sim.config.gaugeThreshold), 1f);
                    gaugeTexts[u].text = u.IsAlive ? u.gauge.ToString("F0") : "倒下";
                }
                if (portraits.ContainsKey(u) && portraits[u] != null)
                {
                    // 倒下的立绘压暗，别让它在卡片里看着还活着
                    portraits[u].color = u.IsAlive ? Color.white : new Color(0.35f, 0.35f, 0.40f, 0.55f);
                }
            }

            spiritText.text = string.Format("灵能 {0}/{1}     回合 {2}", sim.spirit, sim.config.spiritMax, sim.round);
            potionText.text = string.Format("药品 {0}", sim.potions);

            RefreshLog();
        }

        /// <summary>调试用：把 HUD 与模拟器的关键状态压成一行字符串，供自动截图脚本写日志。</summary>
        public string DebugState()
        {
            if (sim == null) return "sim=null";

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("frame={0} t={1:F1}s round={2} spirit={3}/{4} potions={5} awaiting={6} over={7}",
                Time.frameCount, sim.time, sim.round, sim.spirit, sim.config.spiritMax, sim.potions,
                sim.awaitingPlayer != null ? sim.awaitingPlayer.displayName : "-", sim.IsBattleOver);

            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                float gaugeRatio = Mathf.Clamp01(u.gauge / sim.config.gaugeThreshold);
                float drawnRatio = gaugeFills.ContainsKey(u) ? gaugeFills[u].anchorMax.x : -1f;
                sb.AppendFormat(" | {0}: hp={1:F0}/{2:F0} gauge={3:F0} ratio={4:F2} drawn={5:F2}",
                    u.displayName, u.hp, u.maxHp, u.gauge, gaugeRatio, drawnRatio);
            }

            sb.Append(" | labels=");
            for (int i = 0; i < skillLabels.Length; i++)
            {
                var t = skillLabels[i];
                sb.Append(t == null ? "[null]" : ("[" + t.text.Replace("\n", "/") + "]"));
            }
            return sb.ToString();
        }

        int logShown;
        void RefreshLog()
        {
            for (; logShown < sim.log.Count; logShown++)
            {
                var line = MakeText(logColumn, "L" + logShown, sim.log[logShown], 15, new Color(0.85f, 0.88f, 0.92f),
                    TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(286f, 20f), new Vector2(0f, 1f), Vector2.zero);
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
                line.verticalOverflow = VerticalWrapMode.Overflow;
                logLines.Add(line);
            }
            while (logLines.Count > 22)
            {
                Destroy(logLines[0].gameObject);
                logLines.RemoveAt(0);
            }
        }

        void ShowFinish()
        {
            bool win = sim.Alive(Side.Enemy).Count == 0;
            ClearButtons(); // 先清按钮（它会把横幅清空），再写结果文字，别反了
            statusBanner.text = win ? "战斗结束：胜利" : "战斗结束：失败";
            statusBanner.color = win ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f);
        }

        void Flash(string msg)
        {
            statusBanner.text = msg;
            statusBanner.color = new Color(1f, 0.6f, 0.4f);
        }

        // ------------------------------------------------------------------ UI 小工具

        /// <summary>运行时生成一张竖向渐变 sprite（2x128 就够，反正被拉伸）。</summary>
        static Sprite MakeGradientSprite(Color top, Color bottom)
        {
            const int h = 128;
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < h; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(h - 1));
                tex.SetPixel(0, y, c);
                tex.SetPixel(1, y, c);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>径向渐变（中心实、边缘透明），用来画场地光晕。</summary>
        static Sprite MakeRadialSprite(Color center, Color edge, int size = 128)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(r, r)) / r;
                    tex.SetPixel(x, y, Color.Lerp(center, edge, Mathf.Clamp01(d)));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>立绘从 Resources 里按 id 取，取不到就返回 null（卡片照样能用）。</summary>
        static Sprite LoadPortrait(string id)
        {
            var s = Resources.Load<Sprite>("Art/Creatures/" + id);
            return s;
        }

        static Color ElementColor(Element e)
        {
            switch (e)
            {
                case Element.Water: return new Color(0.42f, 0.78f, 1f);
                case Element.Fire: return new Color(1f, 0.62f, 0.32f);
                case Element.Wind: return new Color(0.55f, 1f, 0.85f);
                default: return new Color(1f, 0.85f, 0.45f);
            }
        }

        static Image MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        /// <summary>anchorMin = anchorMax = anchor，pivot 与位置单给，避免把 pivot 传进 anchor 造成拉伸。</summary>
        Text MakeText(Transform parent, string name, string content, int size, Color color, TextAnchor align,
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
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = t.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = pos;
            return t;
        }

        static Transform MakeRow(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(position.x, -position.y);
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            return go.transform;
        }

        static Transform MakeColumn(Transform parent, string name, float spacing, RectOffset padding, bool controlHeight)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Stretch(rt);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = controlHeight;
            layout.childControlWidth = false;
            layout.childAlignment = TextAnchor.UpperLeft;
            return go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 返回 (底条, 填充条)。填充条用锚点宽度驱动（anchorMax.x = 比例）——
        /// Image.Type.Filled 在没有 sprite 时不会裁剪，纯色矩形照样画满，所以不能用 fillAmount。
        /// </summary>
        (RectTransform, RectTransform) MakeBar(Transform parent, string name, Color fillColor, float width, float height)
        {
            var back = MakePanel(parent, name, new Color(0.05f, 0.05f, 0.06f, 1f));
            back.rectTransform.sizeDelta = new Vector2(width, height);

            var barLe = back.gameObject.AddComponent<LayoutElement>();
            barLe.preferredWidth = width;
            barLe.preferredHeight = height;

            var fillGo = new GameObject(name + "_Fill", typeof(Image));
            fillGo.transform.SetParent(back.transform, false);
            var fillImg = fillGo.GetComponent<Image>();
            fillImg.color = fillColor;
            var rt = fillImg.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return (back.rectTransform, rt);
        }

        Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            // 底色交给 Button 的 ColorBlock 控制；Image 必须是白色，否则 ColorBlock 会再乘一次，置灰后直接变全黑
            img.color = Color.white;

            // 默认的 disabledColor 是半透明灰，置灰后按钮会变成「黑洞」——底色和文字一起淡掉。
            // 这里让禁用态保持同样的底色，只靠文字颜色变灰来区分。
            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = new Color(0.20f, 0.26f, 0.34f, 1f);
            colors.highlightedColor = new Color(0.28f, 0.36f, 0.46f, 1f);
            colors.pressedColor = new Color(0.16f, 0.21f, 0.28f, 1f);
            colors.selectedColor = new Color(0.20f, 0.26f, 0.34f, 1f);
            colors.disabledColor = new Color(0.20f, 0.26f, 0.34f, 1f);
            btn.colors = colors;
            btn.targetGraphic = img;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var text = MakeText(go.transform, "Label", label, 22, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), size, new Vector2(0.5f, 0.5f), Vector2.zero);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return btn;
        }
    }
}
