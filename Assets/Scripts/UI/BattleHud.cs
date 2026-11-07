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
        [Tooltip("可选：第二波敌人 id。留空就是单波战斗。")]
        public string[] enemyIdsWave2 = new string[0];

        [Header("随机遭遇（一境）")]
        [Tooltip("打开后忽略 enemyIds / enemyIdsWave2，改用 EncounterGenerator 现场生成")]
        public bool useRandomEncounter = false;
        [Tooltip("遭遇随机种子：同一个种子出同一场，0 表示按时间随机")]
        public int encounterSeed = 0;
        public float timeScale = 1f;
        public bool autoBattle;

        [Tooltip("是否给三只兽穿满配五件。装备界面还没做，先用这个开关对比战力与胜率")]
        public bool equipFullSet = true;

        [Tooltip("跳过编队面板直接开打（自动截图/批测用），默认编队为水/火/风")]
        public bool skipFormation;

        BattleSimulator sim;

        /// <summary>给编辑器脚本（自动截图/批量测试）读状态用。</summary>
        public BattleSimulator Simulator { get { return sim; } }
        Font font;

        readonly List<Text> logLines = new List<Text>();
        readonly Dictionary<BattleUnit, RectTransform> hpFills = new Dictionary<BattleUnit, RectTransform>();
        readonly Dictionary<BattleUnit, Text> hpTexts = new Dictionary<BattleUnit, Text>();
        readonly Dictionary<BattleUnit, Text> powerTexts = new Dictionary<BattleUnit, Text>();
        readonly Dictionary<BattleUnit, Button> cardButtons = new Dictionary<BattleUnit, Button>();
        readonly Dictionary<BattleUnit, RectTransform> gaugeMarkers = new Dictionary<BattleUnit, RectTransform>();
        readonly Dictionary<BattleUnit, Image> gaugeMarkerBgs = new Dictionary<BattleUnit, Image>();
        RectTransform gaugeTrack;
        RectTransform gaugePanelRect;
        readonly Dictionary<BattleUnit, RectTransform> mpFills = new Dictionary<BattleUnit, RectTransform>();
        readonly Dictionary<BattleUnit, Text> mpTexts = new Dictionary<BattleUnit, Text>();
        Text mpPotionText;
        Button mpPotionButton;
        int pendingSlot = -1;              // >=0 表示正等玩家点目标
        bool pendingAlly;                  // true=这次点的是友方（辅助/治疗）
        readonly Dictionary<BattleUnit, RectTransform> shieldFills = new Dictionary<BattleUnit, RectTransform>();   // 护盾覆盖条（靛蓝，盖在血条上）
        readonly Dictionary<BattleUnit, Text> gaugeTexts = new Dictionary<BattleUnit, Text>();
        readonly Dictionary<BattleUnit, RectTransform> cardRects = new Dictionary<BattleUnit, RectTransform>();
        readonly Dictionary<BattleUnit, Image> cardImages = new Dictionary<BattleUnit, Image>();
        readonly Dictionary<BattleUnit, Color> cardBaseColors = new Dictionary<BattleUnit, Color>();
        readonly Dictionary<BattleUnit, Image> portraits = new Dictionary<BattleUnit, Image>();

        Transform playerRow;
        Transform enemyRow;
        Transform logColumn;
        RectTransform canvasRect;
        Text spiritText;
        Text potionText;
        Text statusBanner;

        // 技能按钮 / 药品按钮上的图标（程序生成，见 Assets/Resources/Art/Icons）
        readonly Image[] skillIcons = new Image[5];   // 0=普攻，1..4=上阵
        Image potionIcon;
        readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();
        readonly Dictionary<BattleUnit, Transform> statusRows = new Dictionary<BattleUnit, Transform>();
        readonly Dictionary<BattleUnit, string> statusSignatures = new Dictionary<BattleUnit, string>();
        RectTransform floatLayer;
        Button[] skillButtons = new Button[5];
        Text[] skillLabels = new Text[5];   // 0=普攻，1..4=上阵
        Button potionButton;
        Button autoButton;                 // 自动战斗：等级 <15 时置灰（设计文档「自动战斗需等级 ≥15」）
        Button startButton;                // 编队面板的「开战」
        Button menuBackButton;             // 结算后「回主菜单」
        GameObject formationPanel;         // 开战前的编队面板（4 选 3，开战后不再给入口）
        readonly List<string> picked = new List<string>();   // 编队选中的 3 只
        bool battleStarted;
        string lastEncounterText = "";

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
            // v0.1 规则：编队 4 选 3，开战前选、开战后不可更改 —— 所以先出编队面板，由「开战」触发 StartBattle()
            // 一境探索模式：队伍在进战斗前就定了（RunState），不能再弹编队面板
            if (skipFormation || LingJing.Battle.RunState.Active)
            {
                if (!LingJing.Battle.RunState.Active) PrefillDefaultFormation();
                StartBattle();
            }
            else
            {
                BuildFormationPanel();
            }
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
            // 受击位移：立绘左右抖一下；治疗不给抖，不然看着像挨打
            if (!isHeal && portraits.ContainsKey(unit) && portraits[unit] != null)
            {
                var shake = portraits[unit].gameObject.GetComponent<HitShake>();
                if (shake == null) shake = portraits[unit].gameObject.AddComponent<HitShake>();
                shake.Init(portraits[unit].rectTransform.anchoredPosition, 7f, 0.20f);
            }

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
            canvasRect = canvasGo.GetComponent<RectTransform>();
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

            // 行动条：横向一条跑道（用户要求「都在一条直线上，谁跑得快就到谁攻击」）
            var gaugePanel = MakePanel(canvasGo.transform, "GaugePanel", new Color(0.10f, 0.11f, 0.14f, 0.95f));
            var gp = gaugePanel.rectTransform;
            gaugePanelRect = gp;
            gp.anchorMin = new Vector2(0f, 1f);
            gp.anchorMax = new Vector2(1f, 1f);
            gp.pivot = new Vector2(0.5f, 1f);
            gp.anchoredPosition = new Vector2(0f, -60f);
            gp.sizeDelta = new Vector2(-860f, 54f);   // 左右各留 410：避开顶栏文字与右上角自动战斗按钮

            MakeText(gaugePanel.transform, "GaugeTitle", "行动条", 16, new Color(0.75f, 0.8f, 0.9f),
                TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(90f, 20f), new Vector2(0f, 0.5f), new Vector2(-96f, 0f));

            // 跑道：一条从左到右的细线，右端是「出手线」
            var track = MakePanel(gaugePanel.transform, "Track", new Color(0.34f, 0.38f, 0.47f, 1f));
            gaugeTrack = track.rectTransform;
            gaugeTrack.anchorMin = new Vector2(0f, 0.5f);
            gaugeTrack.anchorMax = new Vector2(1f, 0.5f);
            gaugeTrack.pivot = new Vector2(0.5f, 0.5f);
            gaugeTrack.anchoredPosition = new Vector2(0f, 2f);
            gaugeTrack.sizeDelta = new Vector2(-130f, 8f);

            // 出手线在左端：徽标从右往左滚，滚到这条线就出手
            var finish = MakePanel(gaugePanel.transform, "Finish", new Color(1f, 0.85f, 0.45f, 0.9f));
            var fr = finish.rectTransform;
            fr.anchorMin = new Vector2(0f, 0.5f);
            fr.anchorMax = new Vector2(0f, 0.5f);
            fr.pivot = new Vector2(0f, 0.5f);
            fr.sizeDelta = new Vector2(5f, 46f);
            fr.anchoredPosition = new Vector2(64f, 2f);
            MakeText(finish.transform, "FinishLabel", "出手", 14, new Color(1f, 0.85f, 0.45f), TextAnchor.MiddleLeft,
                new Vector2(0f, 0f), new Vector2(60f, 18f), new Vector2(0f, 0f), new Vector2(0f, -4f));

            // 上：敌方（怪物在上，贴顶部）；下：我方（自己的兽在底部，紧贴技能栏）
            enemyRow = MakeRow(canvasGo.transform, "Enemies", new Vector2(340f, 124f), new Vector2(1500f, 200f));
            playerRow = MakeRow(canvasGo.transform, "Players", new Vector2(340f, 660f), new Vector2(1500f, 200f));

            // MP 改成每只兽一张蓝条（画在卡片里，见 BuildUnitWidgets）——用户明确不要统一 MP
            // 回合数与药品数：顶栏一排（MP 已经改成每只兽卡片里的蓝条，这里不再放全局蓝条）
            spiritText = MakeText(canvasGo.transform, "Round", "回合 1/30", 22, new Color(0.8f, 0.88f, 1f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(300f, 30f), new Vector2(0f, 1f), new Vector2(352f, -2f));
            potionText = MakeText(canvasGo.transform, "Potion", "药品 8", 22, new Color(0.85f, 0.9f, 0.7f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(260f, 30f), new Vector2(0f, 1f), new Vector2(676f, -2f));

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

            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                // 5 个技能位（0=普攻，1..4=上阵），200 宽 / 间距 212
                var btn = MakeButton(bar.transform, "Skill" + i, "", new Vector2(-520f + i * 212f, 0f), new Vector2(200f, 126f));
                btn.onClick.AddListener(() => OnSkillClicked(idx));
                skillButtons[i] = btn;
                skillLabels[i] = btn.GetComponentInChildren<Text>();
                skillIcons[i] = AddButtonIcon(btn, 58f, 10f);
                IndentLabel(skillLabels[i], 74f);
            }
            mpPotionButton = MakeButton(bar.transform, "MpPotionBtn", "魔药\n+4 MP", new Vector2(638f, 0f), new Vector2(128f, 120f));
            mpPotionButton.onClick.AddListener(OnMpPotionClicked);
            mpPotionText = mpPotionButton.GetComponentInChildren<Text>();
            mpPotionButton.image.color = new Color(0.20f, 0.26f, 0.40f, 1f);

            potionButton = MakeButton(bar.transform, "PotionBtn", "药品\n35% 血", new Vector2(494f, 0f), new Vector2(128f, 120f));
            potionButton.onClick.AddListener(OnPotionClicked);
            potionIcon = AddButtonIcon(potionButton, 52f, 10f);
            potionIcon.sprite = LoadIcon("potion");
            potionIcon.enabled = potionIcon.sprite != null;   // AddButtonIcon 默认是隐藏的
            IndentLabel(potionButton.GetComponentInChildren<Text>(), 66f);

            // 自动战斗：设计文档要求「等级 ≥15 才解锁」，低于 15 级置灰并写明原因
            // 2026-11-01 用户要求先藏起来（试玩阶段不想让人一键跳过操作），改 showAutoBattleButton 一个开关即可恢复
            autoButton = MakeButton(canvasGo.transform, "AutoBtn", "", new Vector2(0f, 0f), new Vector2(240f, 56f));
            var abrt = autoButton.GetComponent<RectTransform>();
            abrt.anchorMin = new Vector2(1f, 1f);      // 右上角：日志面板左边、行动条右边
            abrt.anchorMax = new Vector2(1f, 1f);
            abrt.pivot = new Vector2(1f, 1f);
            abrt.anchoredPosition = new Vector2(-344f, -6f);
            autoButton.onClick.AddListener(OnAutoClicked);
            autoButton.gameObject.SetActive(showAutoBattleButton);
            UpdateAutoButton();

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

        /// <summary>按品质随机挑一只敌人（探索模式的精英/首领节点用）。</summary>
        static string PickEnemyId(Quality q)
        {
            var list = BattleDatabase.EnemiesOfQuality(q);
            if (list == null || list.Count == 0) return "en_bonefish";
            return list[UnityEngine.Random.Range(0, list.Count)].id;
        }

        void StartBattle()
        {
            var cfg = BattleDatabase.Config;
            if (cfg == null) { Debug.LogError("[BattleHud] 没有 BattleConfig"); return; }

            sim = new BattleSimulator(cfg) { interactive = true, verbose = true };
            int[] free = { 23, 23, 23, 23, 23, 23 };

            // 探索模式（RunState.Active）下队伍与残血来自地图；否则用编队面板选的 3 只
            bool runMode = LingJing.Battle.RunState.Active;
            string[] players = runMode ? LingJing.Battle.RunState.PartyIds.ToArray() : picked.ToArray();
            int playerLevel = runMode ? LingJing.Battle.RunState.PlayerLevel : this.playerLevel;   // 探索模式等级跟地图走
            for (int i = 0; i < players.Length; i++)
            {
                var def = BattleDatabase.GetCreature(players[i]);
                if (def == null) { Debug.LogError("[BattleHud] 缺少 " + players[i]); return; }

                // 装备：换装界面存了什么就穿什么；一件都没配时才退回「满配开关」
                EquipmentLoadout gear = null;
                var preset = EquipmentPresets.Get(players[i]);
                if (!preset.IsEmpty()) gear = preset;
                else if (equipFullSet)
                {
                    gear = new EquipmentLoadout();
                    string[] gearIds = { "eq_wpn", "eq_arm", "eq_trk", "eq_bts", "eq_tot" };
                    for (int g = 0; g < gearIds.Length; g++)
                    {
                        var eq = BattleDatabase.GetEquipment(gearIds[g]);
                        if (eq != null) gear.Set(eq);
                    }
                }

                // 上阵技能：技能界面存了什么就带什么；一格都没配就按等级自动装满 4 格
                var equipped = LingJing.Data.SkillLoadout.Resolve(def, playerLevel);
                var unit = sim.AddPlayer(def, playerLevel, free, gear, equipped);
                if (runMode)
                {
                    float pct = (i < LingJing.Battle.RunState.HpPct.Count) ? LingJing.Battle.RunState.HpPct[i] : 1f;
                    unit.hp = Mathf.Max(1f, unit.maxHp * Mathf.Clamp(pct, 0.05f, 1f));   // 残血继承，但至少留 5%
                }
            }

            if (runMode)
            {
                // 节点类型决定遭遇：普通=生成器；精英=1 精英+1 普通；首领=1 小 Boss
                var kind = LingJing.Battle.RunState.PendingNode;
                int b0 = cfg.BandIndex(LingJing.Battle.RunState.PendingLevel);
                if (kind == "wild" || kind.StartsWith("boss_"))
                {
                    int lv = LingJing.Battle.RunState.PendingLevel;
                    string[] ids;
                    if (kind == "boss_mini") ids = new[] { "en_jelly" };
                    else if (kind == "boss_mid") ids = new[] { "en_tigerking" };
                    else if (kind == "boss_big") ids = new[] { "en_snake" };
                    else
                    {
                        var zone = LingJing.Battle.WorldMap.ZoneById(LingJing.Battle.RunState.PendingZone);
                        var rngW = new System.Random(System.Environment.TickCount);
                        var list = LingJing.Battle.WorldMap.RollEnemies(zone, lv, rngW);
                        ids = list.ToArray();
                        lastEncounterText = (zone != null ? zone.displayName : "浅滩") + " · Lv" + lv + " · " + ids.Length + " 只";
                    }
                    if (kind.StartsWith("boss_")) lastEncounterText = "首领遭遇 · Lv" + lv;
                    if (ids.Length > 0) AddEnemyWave(b0, ids);
                }
                else
                if (kind == "elite")
                {
                    AddEnemyWave(b0, new[] { PickEnemyId(Quality.Elite), PickEnemyId(Quality.Normal) });
                    lastEncounterText = "精英遭遇";
                }
                else if (kind == "boss")
                {
                    AddEnemyWave(b0, new[] { PickEnemyId(Quality.MiniBoss) });
                    lastEncounterText = "首领遭遇";
                }
                else
                {
                    var rng2 = new System.Random(LingJing.Battle.RunState.Floor * 7919 + 13);
                    var enc = EncounterGenerator.Roll(playerLevel, cfg.BandIndex(playerLevel), rng2);
                    enemyLevel = enc.level;
                    for (int w = 0; w < enc.waves.Count; w++) AddEnemyWave(cfg.BandIndex(enc.level), enc.waves[w]);
                    lastEncounterText = EncounterGenerator.Describe(enc);
                }
                Debug.Log("[BattleHud] 探索第 " + LingJing.Battle.RunState.Floor + " 层：" + lastEncounterText);
            }
            else if (useRandomEncounter)
            {
                int seed = encounterSeed != 0 ? encounterSeed : System.Environment.TickCount;
                var rng = new System.Random(seed);
                var enc = EncounterGenerator.Roll(playerLevel, cfg.BandIndex(playerLevel), rng);
                enemyLevel = enc.level;
                for (int w = 0; w < enc.waves.Count; w++)
                {
                    int b = cfg.BandIndex(enc.level);
                    AddEnemyWave(b, enc.waves[w]);
                }
                lastEncounterText = EncounterGenerator.Describe(enc);
                Debug.Log("[BattleHud] 随机遭遇（seed " + seed + "）：" + lastEncounterText);
            }
            else
            {
                int band = cfg.BandIndex(enemyLevel);
                AddEnemyWave(band, enemyIds);
                // 可选第二波：填了 enemyIdsWave2 就变成两波战斗（一波清完不判胜利）
                if (enemyIdsWave2 != null && enemyIdsWave2.Length > 0) AddEnemyWave(band, enemyIdsWave2);
            }

            sim.Begin();
            sim.ActionStarted += OnActionStarted;

            // 调试：开战直接上盾（只为检查靛蓝护盾条长什么样）
            if (debugShieldOnStart)
            {
                int n = 0;
                for (int i = 0; i < sim.units.Count; i++)
                {
                    var u = sim.units[i];
                    if (u.side != Side.Player || !u.IsAlive) continue;
                    u.shield = u.maxHp * (n == 0 ? 0.28f : 0.12f);
                    if (++n >= 2) break;
                }
            }

            BuildUnitWidgets();
            RaiseGaugePanel();
            RefreshAll();
            PrimeSkillLabels();

            // 开战即锁：编队面板关掉，之后再没有换人的入口（v0.1「编队 4 选 3 不可改」）
            battleStarted = true;
            if (formationPanel != null) formationPanel.SetActive(false);
            UpdateAutoButton();
        }

        // ------------------------------------------------------------------ 自动战斗

        [Tooltip("试玩阶段先藏起来；要恢复自动战斗按钮改成 true 即可")]
        public bool showAutoBattleButton = false;

        [Tooltip("调试用：开战时直接给前两只我方上盾，方便检查靛蓝护盾条")]
        public bool debugShieldOnStart = false;
        static readonly int AutoBattleUnlockLevel = 15;

        void UpdateAutoButton()
        {
            if (autoButton == null) return;
            bool unlocked = playerLevel >= AutoBattleUnlockLevel;
            autoButton.interactable = unlocked && battleStarted;
            var lbl = autoButton.GetComponentInChildren<Text>();
            if (lbl == null) return;
            if (!unlocked) { lbl.text = "自动战斗（需 Lv" + AutoBattleUnlockLevel + "）"; lbl.color = new Color(0.55f, 0.6f, 0.66f); }
            else { lbl.text = autoBattle ? "自动战斗：开" : "自动战斗：关"; lbl.color = Color.white; }
        }

        void OnAutoClicked()
        {
            if (playerLevel < AutoBattleUnlockLevel) return;
            autoBattle = !autoBattle;
            Flash(autoBattle ? "自动战斗已开启" : "自动战斗已关闭");
            UpdateAutoButton();
        }

        // ------------------------------------------------------------------ 编队（4 选 3）

        static readonly string[] RosterIds = { "beast_water", "beast_fire", "beast_wind", "beast_earth" };
        Text formationTip;

        void PrefillDefaultFormation()
        {
            picked.Clear();
            picked.Add(RosterIds[0]);
            picked.Add(RosterIds[1]);
            picked.Add(RosterIds[2]);
        }

        void BuildFormationPanel()
        {
            var panel = MakePanel(canvasRect, "Formation", new Color(0.05f, 0.06f, 0.09f, 0.985f));
            Stretch(panel.rectTransform);
            formationPanel = panel.gameObject;

            MakeText(panel.transform, "Title", "编队：4 只里选 3 只（开战后不可更换）", 38,
                new Color(1f, 0.92f, 0.6f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(1400f, 56f), new Vector2(0.5f, 1f), new Vector2(0f, -90f));

            for (int i = 0; i < RosterIds.Length; i++)
            {
                int idx = i;
                var def = BattleDatabase.GetCreature(RosterIds[i]);
                string nm = def != null ? def.displayName : RosterIds[i];

                float x = -570f + i * 380f;
                var card = MakePanel(panel.transform, "Slot_" + i, new Color(0.14f, 0.18f, 0.24f, 1f));
                var crt = card.rectTransform;
                crt.anchorMin = new Vector2(0.5f, 0.5f);
                crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.sizeDelta = new Vector2(340f, 480f);
                crt.anchoredPosition = new Vector2(x, 40f);

                // 立绘：等比放进 260×260 的框
                var portrait = new GameObject("Portrait", typeof(Image)).GetComponent<Image>();
                portrait.transform.SetParent(card.transform, false);
                var sprite = LoadPortrait(RosterIds[i]);
                portrait.sprite = sprite;
                portrait.preserveAspect = true;
                portrait.raycastTarget = false;
                portrait.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.05f);
                var prt = portrait.rectTransform;
                prt.anchorMin = new Vector2(0.5f, 1f);
                prt.anchorMax = new Vector2(0.5f, 1f);
                prt.pivot = new Vector2(0.5f, 1f);
                prt.sizeDelta = new Vector2(260f, 260f);
                prt.anchoredPosition = new Vector2(0f, -30f);

                MakeText(card.transform, "Name", nm, 26, Color.white, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 1f), new Vector2(300f, 34f), new Vector2(0.5f, 1f), new Vector2(0f, -300f));

                // 战力按「换装界面存了什么就穿什么」算，跟开打后卡片上的数字对得上
                EquipmentLoadout gear = EquipmentPresets.Get(RosterIds[i]);
                bool hasGear = gear != null && !gear.IsEmpty();
                float power = PowerWithGear(def, hasGear ? gear : null);
                MakeText(card.transform, "Power", "战力 " + Mathf.RoundToInt(power), 22,
                    new Color(0.72f, 0.82f, 0.95f), TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 1f), new Vector2(300f, 30f), new Vector2(0.5f, 1f), new Vector2(0f, -336f));
                MakeText(card.transform, "GearTag", hasGear ? "已装备" : "（裸装）", 18,
                    hasGear ? new Color(0.55f, 0.9f, 0.6f) : new Color(0.75f, 0.7f, 0.7f), TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 1f), new Vector2(300f, 26f), new Vector2(0.5f, 1f), new Vector2(0f, -366f));

                // 整张卡就是一个按钮，点一下选中/取消
                var btn = card.gameObject.AddComponent<Button>();
                btn.targetGraphic = card;
                var colors = btn.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
                colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                colors.selectedColor = Color.white;
                colors.disabledColor = Color.white;
                btn.colors = colors;
                btn.onClick.AddListener(() => OnRosterClicked(idx));
                rosterCards.Add(card);
            }

            formationTip = MakeText(panel.transform, "Tip", "", 28, new Color(0.8f, 0.86f, 0.95f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(900f, 40f), new Vector2(0.5f, 0f), new Vector2(0f, 262f));

            var start = MakeButton(panel.transform, "StartBtn", "开战", new Vector2(0f, -360f), new Vector2(340f, 96f));
            startButton = start;
            start.onClick.AddListener(OnStartClicked);

            RefreshFormation();
        }

        readonly List<Image> rosterCards = new List<Image>();

        void OnRosterClicked(int i)
        {
            string id = RosterIds[i];
            if (picked.Contains(id)) picked.Remove(id);
            else if (picked.Count < 3) picked.Add(id);
            RefreshFormation();
        }

        void RefreshFormation()
        {
            for (int i = 0; i < rosterCards.Count; i++)
            {
                bool on = picked.Contains(RosterIds[i]);
                rosterCards[i].color = on ? new Color(0.24f, 0.38f, 0.30f, 1f) : new Color(0.14f, 0.18f, 0.24f, 1f);
                var tag = rosterCards[i].transform.Find("GearTag");
                if (tag != null)
                {
                    var t = tag.GetComponent<Text>();
                    t.text = on ? "✔ 已选" : (picked.Count >= 3 ? "已满 3 只" : "点击选择");
                    t.color = on ? new Color(0.55f, 1f, 0.6f) : new Color(0.7f, 0.74f, 0.8f);
                }
            }

            if (formationTip != null) formationTip.text = string.Format("已选 {0} / 3", picked.Count);
            if (startButton != null)
            {
                startButton.interactable = picked.Count == 3;
                var lbl = startButton.GetComponentInChildren<Text>();
                if (lbl != null) lbl.color = picked.Count == 3 ? Color.white : new Color(0.55f, 0.6f, 0.66f);
            }
        }

        void OnStartClicked()
        {
            if (picked.Count != 3 || battleStarted) return;
            StartBattle();
        }

        float PowerWithGear(CreatureDefinition def, EquipmentLoadout loadout)
        {
            if (def == null) return 0f;
            int[] free = { 23, 23, 23, 23, 23, 23 };
            var raw = BattleFormulas.PlayerStats(def.baseStats, playerLevel, def.potentials, free);
            var final = loadout != null ? loadout.Apply(raw) : raw;
            float gearPower = loadout != null ? loadout.Power() : 0f;
            return BattleFormulas.Power(final, playerLevel, gearPower);
        }

        void AddEnemyWave(int band, string[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                var def = BattleDatabase.GetCreature(ids[i]);
                if (def == null) { Debug.LogError("[BattleHud] 缺少 " + ids[i]); continue; }
                sim.AddEnemy(def, enemyLevel, band);
            }
            sim.EndWave();
        }

        /// <summary>
        /// 开局先把四个按钮写成「1 号兽的技能名」（置灰不可点）。
        /// 不写的话，在轮到玩家之前（以及自动战斗/结算画面）这四个按钮是空方块，看起来像界面坏了。
        /// </summary>
        void PrimeSkillLabels()
        {
            var first = sim.Alive(Side.Player).Count > 0 ? sim.Alive(Side.Player)[0] : null;
            if (first == null) return;

            for (int i = 0; i < 5; i++)
            {
                var skill = first.SkillAt(i);
                bool hasSkill = skill != null;
                string cost = !hasSkill ? "-" : (skill.spiritCost < 0 ? "+1" : skill.spiritCost.ToString());
                skillLabels[i].text = !hasSkill ? "—"
                    : string.Format("{0}\n<{1}> 灵能{2}", skill.displayName, SlotName(i), cost);
                skillLabels[i].color = new Color(0.45f, 0.45f, 0.45f);
                skillButtons[i].interactable = false;
                if (skillIcons[i] != null)
                {
                    skillIcons[i].sprite = hasSkill ? LoadIcon(skill.id) : null;
                    skillIcons[i].enabled = hasSkill && skillIcons[i].sprite != null;   // 还没画图标的新技能别显示白方块
                    skillIcons[i].color = new Color(0.55f, 0.55f, 0.55f, 0.75f);
                }
            }
        }

        void BuildUnitWidgets()
        {
            // 卡片宽度跟着「这一行有多少只」走：3 只以内 460（立绘 150 + 文字 278），
            // 4–5 只压到 360，6 只以上压到 240 —— 不压的话 4 只就 1840 宽，直接溢出 1920 屏幕。
            int enemyTotal = Mathf.Max(1, sim.MaxWaveSize);   // 按最大一波算宽度，波次不影响排版

            float cardW = enemyTotal >= 6 ? 240f : (enemyTotal >= 4 ? 360f : 460f);
            float cardH = enemyTotal >= 6 ? 170f : 190f;
            float pcW = Mathf.Round(cardW * 0.326f);          // 立绘宽
            float textW0 = cardW - pcW - 22f;                 // 文字列剩多少

            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                bool isPlayer = u.side == Side.Player;
                var row = isPlayer ? playerRow : enemyRow;

                // 我方固定 3 只，永远用满宽卡片
                float cw = isPlayer ? 460f : cardW;
                float ch = isPlayer ? 205f : cardH;
                float pw = isPlayer ? 150f : pcW;
                float tw = isPlayer ? 278f : textW0;

                var card = MakePanel(row, "Card_" + u.id, isPlayer ? new Color(0.16f, 0.22f, 0.30f, 1f) : new Color(0.30f, 0.16f, 0.16f, 1f));
                card.rectTransform.sizeDelta = new Vector2(cw, ch);
                cardRects[u] = card.rectTransform;
                BuildStatusRow(u, card.rectTransform);
                cardImages[u] = card;
                cardBaseColors[u] = card.color;
                var le = card.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = cw;
                le.preferredHeight = ch;

                // 卡片做成按钮：单体攻击点敌方卡，单体辅助/治疗点我方卡（点完再结算）
                cardButtons[u] = card.gameObject.AddComponent<Button>();
                cardButtons[u].targetGraphic = card;
                cardButtons[u].transition = Selectable.Transition.None;
                var captured = u;
                cardButtons[u].onClick.AddListener(() => OnTargetClicked(captured));

                // 立绘：卡片左侧 150x150，等比缩放（没有图就只留空白，不影响其它布局）
                // Boss 想要「看起来更压场」但不换美术：立绘放大 12% + 背后加一圈品质色光晕。
                bool isBossCard = !isPlayer && u.quality >= Quality.MiniBoss;
                if (isBossCard)
                {
                    var aura = new GameObject("Aura", typeof(Image)).GetComponent<Image>();
                    aura.transform.SetParent(card.transform, false);
                    aura.sprite = MakeRadialSprite(QualityColor(u.quality) * 0.55f, new Color(0f, 0f, 0f, 0f), 96);
                    aura.raycastTarget = false;
                    var art = aura.rectTransform;
                    art.anchorMin = new Vector2(0f, 0f);
                    art.anchorMax = new Vector2(0f, 0f);
                    art.pivot = new Vector2(0f, 0f);
                    art.sizeDelta = new Vector2(pw * 1.5f, (ch - 30f) * 1.5f);
                    art.anchoredPosition = new Vector2(-pw * 0.22f, -ch * 0.20f);
                }

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
                prt.sizeDelta = isBossCard ? new Vector2(pw * 1.12f, (ch - 30f) * 1.12f) : new Vector2(pw, ch - 30f);
                prt.anchoredPosition = isBossCard ? new Vector2(2f, 4f) : new Vector2(10f, 14f);
                portraits[u] = portrait;

                // 名字 + 血条排在立绘右边
                var col = MakeColumn(card.transform, "Col", 4f, new RectOffset((int)pw + 20, 12, 12, 8), false);

                // —— 品质区分（用户 03:40 提的：精英和 Boss 不能跟普通怪长得一样）——
                // 手段只有三样，都便宜且不影响布局：卡片底色、名字颜色、左下角一条竖色带 + 右下角角标。
                if (!isPlayer)
                {
                    Color qc = QualityColor(u.quality);
                    if (u.quality != Quality.Normal)
                    {
                        card.color = QualityCardTint(u.quality);
                        cardBaseColors[u] = card.color;

                        // 左侧竖色带：8px 宽，贴着卡片左边缘（挂在 card 上，绝对定位，不参与竖列布局）
                        var strip = MakePanel(card.transform, "QualityStrip", qc);
                        var srt = strip.rectTransform;
                        srt.anchorMin = new Vector2(0f, 0f);
                        srt.anchorMax = new Vector2(0f, 1f);
                        srt.pivot = new Vector2(0f, 0.5f);
                        srt.sizeDelta = new Vector2(8f, 0f);
                        srt.anchoredPosition = Vector2.zero;
                        strip.raycastTarget = false;

                        // 右下角角标：精英 / BOSS（这块原本是空的，战力和血条都在左侧）
                        var badge = MakePanel(card.transform, "QualityBadge", qc);
                        var brt = badge.rectTransform;
                        brt.anchorMin = new Vector2(1f, 0f);
                        brt.anchorMax = new Vector2(1f, 0f);
                        brt.pivot = new Vector2(1f, 0f);
                        brt.sizeDelta = new Vector2(QualityTag(u.quality).Length >= 4 ? 96f : 68f, 26f);
                        brt.anchoredPosition = new Vector2(-8f, 8f);
                        badge.raycastTarget = false;
                        var btxt = MakeText(badge.transform, "T", QualityTag(u.quality), 16,
                            new Color(0.09f, 0.08f, 0.06f), TextAnchor.MiddleCenter,
                            new Vector2(0.5f, 0.5f), new Vector2(96f, 26f), new Vector2(0.5f, 0.5f), Vector2.zero);
                        btxt.fontStyle = FontStyle.Bold;
                        Stretch(btxt.rectTransform);
                    }
                }

                // Boss 名字用金色，一眼能看出这场不是杂兵战（v0.1 验收第 4 条）
                bool isBoss = !isPlayer && u.quality >= Quality.MiniBoss;
                Color nameColor = !isPlayer ? QualityNameColor(u.quality) : Color.white;
                MakeText(col, "Name", u.displayName + "   Lv" + u.level, (!isPlayer && enemyTotal >= 6) ? 20 : 24,
                    nameColor, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(tw, 30f), new Vector2(0f, 1f), Vector2.zero);

                // 卡片宽 460，立绘占左侧 170（150 图 + 10 边距 + 10 间隙），文字区只剩 278 —— 这里的宽度必须跟着改，
                // 不然血条会伸出卡片右边（childControlWidth=false 时布局不会帮你压窄）
                var hpBar = MakeBar(col, "Hp", new Color(0.85f, 0.25f, 0.25f), tw, 26f);
                hpFills[u] = hpBar.Item2;

                // 护盾：直接往血条容器里塞一张靛蓝 Image，锚点每次刷新时算
                var shieldGo = new GameObject("ShieldFill", typeof(Image));
                shieldGo.transform.SetParent(hpBar.Item1, false);
                var shieldImg = shieldGo.GetComponent<Image>();
                shieldImg.color = new Color(0.42f, 0.32f, 1f, 1f);   // 不透明靛蓝：半透明压在红血上会发闷，看着不像靛蓝
                shieldImg.raycastTarget = false;
                var shieldRt = shieldImg.rectTransform;
                shieldRt.anchorMin = Vector2.zero;
                shieldRt.anchorMax = Vector2.one;
                shieldRt.offsetMin = Vector2.zero;
                shieldRt.offsetMax = Vector2.zero;
                shieldGo.SetActive(false);
                shieldFills[u] = shieldRt;
                hpTexts[u] = MakeText(hpBar.Item1, "HpText", "", 17, Color.white, TextAnchor.MiddleCenter,
                    new Vector2(0f, 0f), new Vector2(tw, 26f), new Vector2(0f, 0f), Vector2.zero);
                Stretch(hpTexts[u].rectTransform);
                hpTexts[u].transform.SetAsLastSibling();

                // 战力：只显示不参与结算。放在名字/血条同一个竖列里最省事——
                // 之前挂在卡片左下角，Text 建出来了、文本也写进去了，但就是画不出来
                // （卡片自身 Image 与列布局的层级关系），挪进 col 立刻可见。
                if (isPlayer)
                {
                    var mpBar = MakeBar(col, "Mp", new Color(0.36f, 0.68f, 1f), tw, 17f);
                    mpFills[u] = mpBar.Item2;
                    mpTexts[u] = MakeText(mpBar.Item1, "MpText", "", 14, Color.white, TextAnchor.MiddleCenter,
                        new Vector2(0f, 0f), new Vector2(tw, 17f), new Vector2(0f, 0f), Vector2.zero);
                    Stretch(mpTexts[u].rectTransform);
                    mpTexts[u].transform.SetAsLastSibling();
                    var mpo = mpTexts[u].gameObject.AddComponent<Outline>();
                    mpo.effectColor = new Color(0f, 0.10f, 0.25f, 0.95f);
                    mpo.effectDistance = new Vector2(1.5f, -1.5f);
                }

                powerTexts[u] = MakeText(col, "Power", "战力 -", 16, new Color(0.72f, 0.80f, 0.90f),
                    TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(tw, 24f),
                    new Vector2(0f, 1f), Vector2.zero);

                // 行动条徽标：我方蓝、敌方红；带小立绘 + 名字首字，跑道上按进度往右挪
                var markBg = MakePanel(gaugeTrack, "Mark_" + u.id,
                    isPlayer ? new Color(0.22f, 0.40f, 0.62f, 1f) : new Color(0.52f, 0.24f, 0.22f, 1f));
                var mr = markBg.rectTransform;
                mr.anchorMin = new Vector2(0f, 0.5f);
                mr.anchorMax = new Vector2(0f, 0.5f);
                mr.pivot = new Vector2(0.5f, 0.5f);
                mr.sizeDelta = new Vector2(34f, 34f);
                mr.anchoredPosition = new Vector2(0f, 6f);
                gaugeMarkers[u] = mr;
                gaugeMarkerBgs[u] = markBg;

                var mIcon = new GameObject("MIcon", typeof(Image)).GetComponent<Image>();
                mIcon.transform.SetParent(mr, false);
                mIcon.sprite = LoadPortrait(u.id);
                mIcon.preserveAspect = true;
                mIcon.raycastTarget = false;
                mIcon.enabled = mIcon.sprite != null;
                Stretch(mIcon.rectTransform);
                mIcon.rectTransform.offsetMin = new Vector2(3f, 3f);
                mIcon.rectTransform.offsetMax = new Vector2(-3f, -3f);

                gaugeTexts[u] = MakeText(mr, "GVal", "", 11, Color.white, TextAnchor.MiddleCenter,
                    new Vector2(0.5f, 0f), new Vector2(34f, 14f), new Vector2(0.5f, 0f), new Vector2(0f, -13f));
            }
        }

        // ------------------------------------------------------------------ 主循环

        void Update()
        {
            if (sim == null || !battleStarted) return;

            // 选目标时可以按 Esc 放弃，回到技能选择
            // 注意：工程 activeInputHandler=1（只用新输入系统），旧的 UnityEngine.Input 会直接抛异常
            if (pendingSlot >= 0 && UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                pendingSlot = -1;
                pendingAlly = false;
                MarkTargetable(false, false);
                statusBanner.text = "轮到 " + (sim.awaitingPlayer != null ? sim.awaitingPlayer.displayName : "") + " —— 选一个技能";
                statusBanner.color = new Color(1f, 0.9f, 0.5f);
            }

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

        void OnMpPotionClicked()
        {
            if (sim.awaitingPlayer == null) return;
            if (sim.mpPotions <= 0) { Flash("没有魔药了"); return; }
            sim.ResolvePlayerMpPotion();
            ClearButtons();
            RefreshAll();
        }

        /// <summary>点敌方卡片：正在等目标就按这张卡结算，否则不做事。</summary>
        void OnTargetClicked(BattleUnit target)
        {
            if (pendingSlot < 0) return;
            var actor = sim.awaitingPlayer;
            if (actor == null) { pendingSlot = -1; return; }
            if (target == null || !target.IsAlive) return;

            var skill = actor.SkillAt(pendingSlot);
            bool wantAlly = pendingAlly;
            pendingSlot = -1;
            pendingAlly = false;
            MarkTargetable(false, false);
            if (skill == null) return;
            // 点的是敌方卡却要辅助技能（或反过来）→ 不结算，保持等待
            if (wantAlly && target.side != Side.Player) return;
            if (!wantAlly && target.side != Side.Enemy) return;
            sim.ResolvePlayerTurn(skill, target);
            ClearButtons();
            RefreshAll();
        }

        /// <summary>把「可点的卡」标出来：敌方（攻击）或我方（辅助/治疗）加一圈暖色描边并略微放大。</summary>
        void MarkTargetable(bool on, bool ally)
        {
            var wantSide = ally ? Side.Player : Side.Enemy;
            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                if (!cardImages.ContainsKey(u)) continue;
                if (!u.IsAlive || !u.inField) continue;
                bool mark = on && u.side == wantSide;
                if (u.side == Side.Enemy)
                    cardImages[u].color = mark
                        ? Color.Lerp(cardBaseColors[u], new Color(1f, 0.85f, 0.45f), 0.35f)
                        : (u.quality == Quality.Normal ? cardBaseColors[u] : QualityCardTint(u.quality));
                else
                    cardImages[u].color = mark
                        ? Color.Lerp(cardBaseColors[u], new Color(0.55f, 0.95f, 0.65f), 0.35f)
                        : cardBaseColors[u];
                cardRects[u].localScale = mark ? new Vector3(1.03f, 1.03f, 1f) : Vector3.one;
            }
        }

        void OnSkillClicked(int slot)
        {
            var actor = sim.awaitingPlayer;
            if (actor == null) return;
            var skill = actor.SkillAt(slot);
            if (skill == null) { Flash("这个槽位是空的"); return; }
            if (skill.spiritCost > 0 && actor.mp < skill.spiritCost) { Flash("这只兽的 MP 不够"); return; }
            if (actor.Cooldown(skill.id) > 0) { Flash(string.Format("冷却中（还要 {0} 次行动）", actor.Cooldown(skill.id))); return; }

            // 单体技能要玩家自己点目标（攻击点敌人、辅助/治疗点自己人）；群体技能直接结算
            // 辅助分两种：给己方加 buff、给敌人挂 debuff —— 由 SkillTargeting 按效果语义判定
            bool needAlly = SkillTargeting.PickAlly(skill);
            bool needEnemy = SkillTargeting.NeedsPick(skill) && !needAlly;
            if (needEnemy || needAlly)
            {
                pendingSlot = slot;
                pendingAlly = needAlly;
                MarkTargetable(true, needAlly);
                statusBanner.text = needAlly ? "点一只自己的兽作为目标（Esc 取消）" : "点一个敌人作为目标（Esc 取消）";
                statusBanner.color = new Color(needAlly ? 0.55f : 1f, 0.9f, needAlly ? 0.65f : 0.45f);
                return;
            }

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

            for (int i = 0; i < 5; i++)
            {
                var skill = actor.SkillAt(i);
                bool hasSkill = skill != null;
                bool usable = hasSkill
                              && (skill.spiritCost <= 0 || actor.mp >= skill.spiritCost)
                              && actor.Cooldown(skill.id) <= 0;

                skillButtons[i].interactable = usable;
                string cost = !hasSkill ? "-" : (skill.spiritCost < 0 ? "+1" : skill.spiritCost.ToString());
                string shape = !hasSkill ? "" : TargetShape(skill);
                string cd = (hasSkill && actor.Cooldown(skill.id) > 0)
                    ? string.Format("  冷却{0}", actor.Cooldown(skill.id)) : "";
                skillLabels[i].text = !hasSkill ? "—"
                    : string.Format("{0}\n<{1}{2}> MP{3}{4}", skill.displayName, SlotName(i), shape, cost, cd);
                skillLabels[i].color = usable ? Color.white : new Color(0.45f, 0.45f, 0.45f);
                if (skillIcons[i] != null)
                {
                    skillIcons[i].sprite = hasSkill ? LoadIcon(skill.id) : null;
                    skillIcons[i].enabled = hasSkill && skillIcons[i].sprite != null;   // 还没画图标的新技能别显示白方块
                    skillIcons[i].color = usable ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.8f);
                }
            }
            potionButton.interactable = sim.potions > 0;
            if (mpPotionButton != null) mpPotionButton.interactable = sim.mpPotions > 0 && actor.mp < actor.mpMax;
        }

        void ClearButtons()
        {
            // 只置灰、不清文字：清空会留下四个黑方块，看起来像界面坏了
            for (int i = 0; i < 5; i++)
            {
                skillButtons[i].interactable = false;
                skillLabels[i].color = new Color(0.45f, 0.45f, 0.45f);
            }
            potionButton.interactable = false;
            if (mpPotionButton != null) mpPotionButton.interactable = false;
            if (pendingSlot >= 0) { pendingSlot = -1; pendingAlly = false;
                MarkTargetable(false, false); }
            statusBanner.text = "";
        }

        /// <summary>单体 / 全体 / 治疗 / 增益 —— 直接写在按钮上，免得玩家点了才发现打不到。</summary>
        static string TargetShape(SkillDefinition skill)
        {
            if (skill.isHeal) return "·治疗";
            if (skill.isShield) return "·护盾";
            if (skill.buffPercent > 0f) return "·增益";
            if (skill.targetSide == TargetSide.AllEnemies && (skill.multiplier > 0f || skill.dotStacks > 0)) return "·全体";
            if (skill.targetSide == TargetSide.Enemy && skill.multiplier > 0f) return "·单体";
            return "";
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
                    hpTexts[u].text = string.Format("{0:F0}/{1:F0}", u.hp, u.maxHp);
                    if (shieldFills.ContainsKey(u))
                    {
                        var sf = shieldFills[u];
                        float ratio = Mathf.Clamp01(u.shield / Mathf.Max(1f, u.maxHp));
                        sf.gameObject.SetActive(u.shield > 0.5f);
                        sf.anchorMin = new Vector2(0f, 0f);
                        sf.anchorMax = new Vector2(ratio, 1f);
                        sf.offsetMin = Vector2.zero;
                        sf.offsetMax = Vector2.zero;
                    }
                }
                if (gaugeMarkers.ContainsKey(u))
                {
                    // 跑道宽度 = 父节点宽度，按行动值比例摆；同分时我方略高一档，避免完全重叠
                    float t = Mathf.Clamp01(u.gauge / Mathf.Max(1f, sim.config.gaugeThreshold));
                    var parentW = gaugeTrack.rect.width;
                    float x = Mathf.Lerp(parentW - 19f, 19f, t);   // 从右往左滚，左边是出手线
                    // 两车道交错：行动值接近时两个徽标不会完全叠在一起
                    float lane = (i % 2 == 0) ? 7f : -7f;
                    gaugeMarkers[u].anchoredPosition = new Vector2(x, 4f + lane);
                    bool acting = sim.awaitingPlayer == u;
                    gaugeMarkerBgs[u].color = acting ? new Color(1f, 0.85f, 0.45f, 1f)
                        : (u.side == Side.Player ? new Color(0.22f, 0.40f, 0.62f, 1f) : new Color(0.52f, 0.24f, 0.22f, 1f));
                    gaugeTexts[u].text = u.IsAlive ? u.gauge.ToString("F0") : "倒";
                    gaugeTexts[u].color = acting ? new Color(1f, 0.85f, 0.45f) : Color.white;
                    gaugeMarkers[u].localScale = acting ? new Vector3(1.15f, 1.15f, 1f) : Vector3.one;
                }
                if (portraits.ContainsKey(u) && portraits[u] != null)
                {
                    // 倒下的立绘压暗，别让它在卡片里看着还活着
                    portraits[u].color = u.IsAlive ? Color.white : new Color(0.35f, 0.35f, 0.40f, 0.55f);
                }
                if (powerTexts.ContainsKey(u) && powerTexts[u] != null)
                {
                    powerTexts[u].text = "战力 " + Mathf.RoundToInt(u.power);
                }
                UpdateStatusIcons(u);

                // 卡片显示规则：我方一直显示（死了压暗）；敌方必须已入场且还活着。
                // 一波清完立刻收尸，否则第二波进场时上一波的卡还占着位置（4+3 波就会出现 6 张卡）。
                bool showCard = u.side == Side.Player || (u.inField && u.IsAlive);
                if (cardRects.ContainsKey(u) && cardRects[u].gameObject.activeSelf != showCard)
                    cardRects[u].gameObject.SetActive(showCard);

                // 行动条徽标：没入场的波次不显示
                if (gaugeMarkers.ContainsKey(u) && gaugeMarkers[u].gameObject.activeSelf != (u.inField && u.IsAlive))
                    gaugeMarkers[u].gameObject.SetActive(u.inField && u.IsAlive);
            }

            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                if (mpFills.ContainsKey(u))
                {
                    mpFills[u].anchorMax = new Vector2(Mathf.Clamp01(u.mp / (float)Mathf.Max(1, u.mpMax)), 1f);
                    mpTexts[u].text = string.Format("MP {0}/{1}", u.mp, u.mpMax);
                }
            }
            if (mpPotionText != null) mpPotionText.text = string.Format("魔药 {0}\n+4 MP", sim.mpPotions);

            spiritText.text = sim.WaveTotal > 1
                ? string.Format("回合 {0}/{1}     第 {2}/{3} 波",
                    Mathf.Min(sim.round, sim.config.maxRounds), sim.config.maxRounds,
                    sim.currentWave + 1, sim.WaveTotal)
                : string.Format("回合 {0}/{1}",
                    Mathf.Min(sim.round, sim.config.maxRounds), sim.config.maxRounds);
            potionText.text = string.Format("药品 {0}", sim.potions);

            RefreshLog();
        }

        /// <summary>调试用：把 HUD 与模拟器的关键状态压成一行字符串，供自动截图脚本写日志。</summary>
        public string DebugState()
        {
            if (sim == null) return "sim=null";

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("frame={0} t={1:F1}s round={2} mpPotions={3} potions={4} awaiting={5} over={6}",
                Time.frameCount, sim.time, sim.round, sim.mpPotions, sim.potions,
                sim.awaitingPlayer != null ? sim.awaitingPlayer.displayName : "-", sim.IsBattleOver);

            for (int i = 0; i < sim.units.Count; i++)
            {
                var u = sim.units[i];
                float gaugeRatio = Mathf.Clamp01(u.gauge / sim.config.gaugeThreshold);
                float drawnRatio = gaugeMarkers.ContainsKey(u) ? gaugeMarkers[u].anchoredPosition.x : -1f;
                sb.AppendFormat(" | {0}: hp={1:F0}/{2:F0} gauge={3:F0} ratio={4:F2} drawn={5:F2}",
                    u.displayName, u.hp, u.maxHp, u.gauge, gaugeRatio, drawnRatio);
            }

            sb.Append(" | labels=");
            for (int i = 0; i < skillLabels.Length; i++)
            {
                var t = skillLabels[i];
                sb.Append(t == null ? "[null]" : ("[" + t.text.Replace("\n", "/") + "]"));
            }

            sb.Append(" | power=");
            int shown = 0;
            for (int i = 0; i < sim.units.Count && shown < 6; i++)
            {
                var u = sim.units[i];
                Text pt = powerTexts.ContainsKey(u) ? powerTexts[u] : null;
                sb.AppendFormat("[{0} val={1:F0} txt={2}]", u.displayName, u.power,
                    pt == null ? "null" : ("'" + pt.text + "'"));
                shown++;
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

        void ClearFloatingNumbers()
        {
            if (floatLayer == null) return;
            for (int i = floatLayer.childCount - 1; i >= 0; i--)
                Destroy(floatLayer.GetChild(i).gameObject);
            popCount = 0;
        }

        /// <summary>探索模式结算：把血量/掉落写回 RunState；赢了层数 +1。</summary>
        void CaptureRunIfAny(bool victory)
        {
            if (!LingJing.Battle.RunState.Active || sim == null) return;
            var players = new System.Collections.Generic.List<BattleUnit>();
            for (int i = 0; i < sim.units.Count; i++)
                if (sim.units[i].side == Side.Player) players.Add(sim.units[i]);
            LingJing.Battle.RunState.Capture(players, victory, sim.loot);
        }

        void BackToRunMap()
        {
            if (Application.CanStreamedLevelBeLoaded("RunMap")) UnityEngine.SceneManagement.SceneManager.LoadScene("RunMap");
            else BackToMenu();
        }

        void ShowFinish()
        {
            ClearFloatingNumbers();
            bool win = sim.Alive(Side.Enemy).Count == 0;
            CaptureRunIfAny(win);      // 探索模式：血量/掉落/层数写回地图状态
            ClearButtons(); // 先清按钮（它会把横幅清空），再写结果文字，别反了
            statusBanner.text = win ? "战斗结束：胜利" : "战斗结束：失败";
            statusBanner.color = win ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f);

            // 掉落写进战斗日志：不弹窗、不占按钮，跟其它日志一条条读
            if (win && sim.loot != null && sim.loot.Count > 0)
            {
                for (int i = 0; i < sim.loot.Count; i++)
                {
                    var eq = LingJing.Battle.BattleDatabase.GetEquipment(sim.loot[i]);
                    string nm = eq != null ? eq.displayName : sim.loot[i];
                    AddLog("获得装备：" + nm);
                }
            }
            else if (win)
            {
                AddLog("这场没有掉落");
            }

            // 闭环：结算后给一个回主菜单的出口（v0.1 之前结算完就停在这儿，没法再开一局）
            if (menuBackButton == null)
            {
                menuBackButton = MakeButton(canvasRect, "MenuBack", "回主菜单", new Vector2(0f, 0f), new Vector2(240f, 64f));
            var mbrt = menuBackButton.GetComponent<RectTransform>();
            mbrt.anchorMin = new Vector2(0.5f, 0.5f);   // 屏幕中线下方：横幅与卡片之间的空档
            mbrt.anchorMax = new Vector2(0.5f, 0.5f);
            mbrt.pivot = new Vector2(0.5f, 0.5f);
            mbrt.anchoredPosition = new Vector2(0f, -56f);

                // 运行时新建的按钮，ColorBlock 的首次着色没生效（截出来是一块纯白）。
                // 这里把底色写死、四个状态色都设白，等于只靠 Image.color 出颜色。
                var mbImg = menuBackButton.GetComponent<Image>();
                if (mbImg != null) mbImg.color = new Color(0.22f, 0.30f, 0.40f, 1f);
                var mcb = menuBackButton.colors;
                mcb.normalColor = Color.white;
                mcb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
                mcb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                mcb.selectedColor = Color.white;
                mcb.disabledColor = Color.white;
                menuBackButton.colors = mcb;
                var mblbl = menuBackButton.GetComponentInChildren<Text>();
                if (mblbl != null) { mblbl.color = Color.white; mblbl.fontSize = 26; }

                menuBackButton.onClick.AddListener(BackToMenu);
            }
            // 探索模式下按钮改成「回地图」，点了回 RunMap 继续下一层
            bool runMode = LingJing.Battle.RunState.Active;
            var label = menuBackButton.GetComponentInChildren<Text>();
            if (label != null) label.text = runMode ? "回地图" : "回主菜单";
            menuBackButton.onClick.RemoveAllListeners();
            if (runMode) menuBackButton.onClick.AddListener(BackToRunMap);
            else menuBackButton.onClick.AddListener(BackToMenu);
            menuBackButton.gameObject.SetActive(true);
        }

        /// <summary>结算后回主菜单（也让自动截图脚本能直接调来验证链路）。</summary>
        public void BackToMenu()
        {
            const string menuScene = "MainMenu";
            if (Application.CanStreamedLevelBeLoaded(menuScene)) UnityEngine.SceneManagement.SceneManager.LoadScene(menuScene);
            else Debug.LogWarning("[BattleHud] MainMenu 不在 Build Settings，先跑一次「灵境/生成 主菜单场景」");
        }

        /// <summary>往战斗日志尾部追加一行（日志面板只保留最近 22 行）。</summary>
        void AddLog(string msg)
        {
            sim.log.Add(msg);
            RefreshLog();
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

        // ------------------------------------------------------------------ 品质配色

        static Color QualityColor(Quality q)
        {
            switch (q)
            {
                case Quality.Elite: return new Color(0.72f, 0.45f, 1f);      // 精英：紫
                case Quality.MiniBoss: return new Color(1f, 0.80f, 0.30f);   // 小Boss：金
                case Quality.MidBoss: return new Color(1f, 0.55f, 0.25f);    // 中Boss：橙金
                case Quality.BigBoss: return new Color(1f, 0.35f, 0.30f);    // 大Boss：红金
                default: return new Color(0.55f, 0.42f, 0.42f);
            }
        }

        static string QualityTag(Quality q)
        {
            switch (q)
            {
                case Quality.Elite: return "精英";
                case Quality.MiniBoss: return "小BOSS";
                case Quality.MidBoss: return "中BOSS";
                case Quality.BigBoss: return "大BOSS";
                default: return "";
            }
        }

        static Color QualityCardTint(Quality q)
        {
            switch (q)
            {
                case Quality.Elite: return new Color(0.24f, 0.15f, 0.34f, 1f);
                case Quality.MiniBoss: return new Color(0.36f, 0.27f, 0.11f, 1f);
                case Quality.MidBoss: return new Color(0.38f, 0.22f, 0.10f, 1f);
                case Quality.BigBoss: return new Color(0.40f, 0.14f, 0.14f, 1f);
                default: return new Color(0.30f, 0.16f, 0.16f, 1f);
            }
        }

        static Color QualityNameColor(Quality q)
        {
            switch (q)
            {
                case Quality.Elite: return new Color(0.82f, 0.70f, 1f);
                case Quality.MiniBoss: return new Color(1f, 0.90f, 0.55f);
                case Quality.MidBoss: return new Color(1f, 0.75f, 0.45f);
                case Quality.BigBoss: return new Color(1f, 0.55f, 0.50f);
                default: return Color.white;
            }
        }

        static Image MakePanel(Transform parent, string name, Color color)        {
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

        // ---------------------------------------------------------------- 图标

        Sprite LoadIcon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Sprite s;
            if (iconCache.TryGetValue(id, out s)) return s;
            s = Resources.Load<Sprite>("Art/Icons/Skills/" + id);
            if (s == null) s = Resources.Load<Sprite>("Art/Icons/Status/" + id);
            iconCache[id] = s;
            return s;
        }

        Image AddButtonIcon(Button btn, float size, float pad)
        {
            var go = new GameObject("Icon", typeof(Image));
            go.transform.SetParent(btn.transform, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.enabled = false;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(pad, 0f);
            return img;
        }

        /// <summary>按钮文字让出左侧图标的位置（默认文字是整体居中的）。</summary>
        static void IndentLabel(Text label, float left)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, 6f);
            rt.offsetMax = new Vector2(-6f, -6f);
        }

        static string StatusIconId(StatusType t)
        {
            switch (t)
            {
                case StatusType.Wet: return "st_wet";
                case StatusType.WindErosion: return "st_wind";
                case StatusType.Petrify: return "st_rock";
                case StatusType.HealMark: return "st_heal";
                case StatusType.Burn: return "st_burn";
                case StatusType.Poison: return "st_poison";
                case StatusType.Stun: return "st_stun";
                case StatusType.HealDown: return "st_healdown";
                default: return null;
            }
        }

        /// <summary>把行动条提到卡片之上，免得被敌方卡片盖住（飘字层仍然最上）。</summary>
        void RaiseGaugePanel()
        {
            if (gaugePanelRect == null || canvasRect == null) return;
            int top = canvasRect.childCount - 1;          // 最后一个子物体是飘字层
            gaugePanelRect.SetSiblingIndex(Mathf.Max(0, top - 1));
        }

        void BuildStatusRow(BattleUnit u, RectTransform card)
        {
            var go = new GameObject("StatusRow", typeof(RectTransform));
            go.transform.SetParent(card, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(150f, 26f);
            rt.anchoredPosition = new Vector2(-8f, -8f);
            statusRows[u] = go.transform;
        }

        /// <summary>状态图标只在状态集合变化时重建，别每帧 Destroy/Instantiate。</summary>
        void UpdateStatusIcons(BattleUnit u)
        {
            Transform row;
            if (!statusRows.TryGetValue(u, out row) || row == null) return;

            string sig = "";
            for (int i = 0; i < u.statuses.Count; i++)
                sig += ((int)u.statuses[i].type) + "x" + u.statuses[i].stacks + ";";

            string old;
            if (statusSignatures.TryGetValue(u, out old) && old == sig) return;
            statusSignatures[u] = sig;

            for (int i = row.childCount - 1; i >= 0; i--) Destroy(row.GetChild(i).gameObject);

            const float size = 26f;
            float x = 0f;
            int drawn = 0;
            for (int i = 0; i < u.statuses.Count && drawn < 5; i++)
            {
                string id = StatusIconId(u.statuses[i].type);
                if (id == null) continue;
                var sprite = LoadIcon(id);
                if (sprite == null) continue;

                var go = new GameObject("S_" + id, typeof(Image));
                go.transform.SetParent(row, false);
                var img = go.GetComponent<Image>();
                img.sprite = sprite;
                img.raycastTarget = false;
                img.preserveAspect = true;
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(-x, 0f);
                x += size + 3f;
                drawn++;
            }
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


