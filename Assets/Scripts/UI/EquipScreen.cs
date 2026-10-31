using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using LingJing.Data;
using LingJing.Battle;

namespace LingJing.UI
{
    /// <summary>
    /// 换装界面（v0.2）。左边选兽、中间五个槽位、右边选该槽位的装备。
    /// 全部用代码搭 uGUI，没有美术资源；改完写进 EquipmentPresets，战斗场景直接读。
    /// 结构上刻意保持「每次改动整块重建」，避免一堆增量刷新逻辑（战力、词条、按钮态都要跟着变）。
    /// </summary>
    public class EquipScreen : MonoBehaviour
    {
        static readonly string[] BeastIds = { "beast_water", "beast_fire", "beast_wind" };
        static readonly string[] BeastNames = { "水灵", "火灵", "风灵" };
        static readonly string[] SlotNames = { "武器", "护甲", "灵饰", "足具", "图腾" };
        static readonly string[] GearIds = { "eq_wpn", "eq_arm", "eq_trk", "eq_bts", "eq_tot" };
        static readonly string[] StatNames = { "攻击", "防御", "速度", "体力", "精神", "天赋" };

        static Font font;

        int selectedBeast;
        EquipSlot selectedSlot = EquipSlot.Weapon;

        RectTransform beastCol;
        RectTransform slotCol;
        RectTransform candCol;
        Text powerText;
        Text tipText;

        void Start()
        {
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 24);

            // 第一次打开（谁都没穿过）先给三只兽各穿一套默认五件，免得界面一片「（空）」，
            // 也方便直接对比「满配 vs 裸装」的战力差。
            bool allEmpty = true;
            for (int i = 0; i < BeastIds.Length; i++)
                if (!EquipmentPresets.IsEmpty(BeastIds[i])) { allEmpty = false; break; }
            if (allEmpty)
                for (int i = 0; i < BeastIds.Length; i++) EquipmentPresets.EquipAllFive(BeastIds[i], GearIds);

            BuildStaticUi();
            Rebuild();
        }

        // ------------------------------------------------------------------ 静态骨架

        void BuildStaticUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = MakePanel(canvasGo.transform, "Bg", new Color(0.07f, 0.08f, 0.10f, 1f), Vector2.zero, new Vector2(1920f, 1080f), new Vector2(0.5f, 0.5f));
            bg.raycastTarget = false;

            MakeText(canvasGo.transform, "Title", "装备　（每只兽 5 件 · 每件加 3 项六维 · 只影响六维，不影响结算规则）",
                30, new Color(0.85f, 0.90f, 0.96f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(1200f, 44f), new Vector2(0f, 1f), new Vector2(60f, -30f));

            beastCol = MakeColumn(canvasGo.transform, "Beasts", 12f, new RectOffset(16, 16, 16, 16),
                new Vector2(0f, 1f), new Vector2(300f, 520f), new Vector2(0f, 1f), new Vector2(60f, -100f));
            slotCol = MakeColumn(canvasGo.transform, "Slots", 10f, new RectOffset(16, 16, 16, 16),
                new Vector2(0f, 1f), new Vector2(760f, 520f), new Vector2(0f, 1f), new Vector2(390f, -100f));
            candCol = MakeColumn(canvasGo.transform, "Candidates", 10f, new RectOffset(16, 16, 16, 16),
                new Vector2(0f, 1f), new Vector2(560f, 520f), new Vector2(0f, 1f), new Vector2(1180f, -100f));

            powerText = MakeText(canvasGo.transform, "Power", "", 26, new Color(1f, 0.88f, 0.55f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(1100f, 40f), new Vector2(0f, 1f), new Vector2(60f, -640f));

            tipText = MakeText(canvasGo.transform, "Tip", "", 20, new Color(0.65f, 0.70f, 0.78f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(1100f, 32f), new Vector2(0f, 1f), new Vector2(60f, -688f));

            // 底部按钮
            MakeButton(canvasGo.transform, "一键满配", new Vector2(60f, -760f), 200f, () =>
            {
                for (int i = 0; i < BeastIds.Length; i++) EquipmentPresets.EquipAllFive(BeastIds[i], GearIds);
                Rebuild();
            });
            MakeButton(canvasGo.transform, "全部卸下", new Vector2(280f, -760f), 200f, () =>
            {
                EquipmentPresets.ResetAll();
                Rebuild();
            });
            MakeButton(canvasGo.transform, "开战（波次场景）", new Vector2(500f, -760f), 300f, () =>
            {
                SceneManager.LoadScene("BattleWaves");
            });
            MakeButton(canvasGo.transform, "回主菜单", new Vector2(830f, -760f), 200f, () =>
            {
                if (Application.CanStreamedLevelBeLoaded("MainMenu")) SceneManager.LoadScene("MainMenu");
                else Debug.LogWarning("[EquipScreen] MainMenu 不在 Build Settings");
            });

            EnsureEventSystem();
        }

        // ------------------------------------------------------------------ 每次改动整块重建

        void Rebuild()
        {
            ClearChildren(beastCol);
            ClearChildren(slotCol);
            ClearChildren(candCol);

            // 左：三只出战友 + 各自战力
            for (int i = 0; i < BeastIds.Length; i++)
            {
                int idx = i;
                var def = BattleDatabase.GetCreature(BeastIds[i]);
                var loadout = EquipmentPresets.Get(BeastIds[i]);
                float power = def != null ? PowerOf(def, loadout) : 0f;

                bool on = idx == selectedBeast;
                var btn = MakeButton(beastCol, BeastNames[i] + "　战力 " + Mathf.RoundToInt(power),
                    Vector2.zero, 264f, () => { selectedBeast = idx; Rebuild(); }, on);
                btn.GetComponent<LayoutElement>().preferredHeight = 84f;
            }

            var beast = BeastIds[selectedBeast];
            var gear = EquipmentPresets.Get(beast);

            // 中：五个槽位
            for (int s = 0; s < SlotNames.Length; s++)
            {
                var slot = (EquipSlot)s;
                var item = gear.Get(slot);
                bool sel = selectedSlot == slot;

                string label = SlotNames[s] + "　" + (item == null ? "（空）" : item.displayName);
                if (item != null) label += "\n" + StatLine(item);

                int slotIdx = s;
                var row = MakeButton(slotCol, label, Vector2.zero, 720f, () => { selectedSlot = slot; Rebuild(); }, sel);
                row.GetComponent<LayoutElement>().preferredHeight = 88f;
                row.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }

            // 右：该槽位的候选装备
            var list = BattleDatabase.GetEquipmentsBySlot(selectedSlot);
            if (list.Count == 0)
            {
                MakeText(candCol, "Empty", "这个槽位还没有任何装备（去 equipment.csv 加）", 20,
                    new Color(0.7f, 0.72f, 0.78f), TextAnchor.MiddleLeft,
                    new Vector2(0f, 1f), new Vector2(520f, 40f), new Vector2(0f, 1f), Vector2.zero);
            }
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                bool equipped = gear.Get(item.slot) == item;
                string label = item.displayName + (equipped ? "　✓ 已穿" : "") + "\n" + StatLine(item);
                var btn = MakeButton(candCol, label, Vector2.zero, 520f, () =>
                {
                    EquipmentPresets.Set(beast, item);
                    Rebuild();
                }, equipped);
                btn.GetComponent<LayoutElement>().preferredHeight = 88f;
                btn.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }

            // 底部：战力合计与提示
            float total = 0f;
            var def0 = BattleDatabase.GetCreature(beast);
            if (def0 != null) total = PowerOf(def0, gear);

            powerText.text = BeastNames[selectedBeast] + "　战力 " + Mathf.RoundToInt(total) +
                             "　（裸装 " + Mathf.RoundToInt(PowerOf(def0, null)) + "）";
            tipText.text = "改了自动存盘。战斗场景里三只兽按这里的穿戴参战；「一键满配」方便看装备差多少。";
        }

        /// <summary>六维 → 战力，口径与 BattleFormulas.Power 一致（只在换装界面显示用）。</summary>
        float PowerOf(CreatureDefinition def, EquipmentLoadout loadout)
        {
            if (def == null) return 0f;
            const int level = 24;
            int[] free = { 23, 23, 23, 23, 23, 23 };
            var raw = BattleFormulas.PlayerStats(def.baseStats, level, def.potentials, free);
            var final = loadout != null ? loadout.Apply(raw) : raw;
            float gearPower = loadout != null ? loadout.Power() : 0f;
            return BattleFormulas.Power(final, level, gearPower);
        }

        string StatLine(EquipmentDefinition item)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < item.mods.Count; i++)
            {
                var m = item.mods[i];
                if (m == null) continue;
                if (sb.Length > 0) sb.Append("　");
                sb.Append(StatNames[(int)m.stat]).Append('+').Append(Mathf.RoundToInt(m.percent * 100f)).Append('%');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ 小工具

        static void ClearChildren(RectTransform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        static Image MakePanel(Transform parent, string name, Color color, Vector2 pos, Vector2 size, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return img;
        }

        static Text MakeText(Transform parent, string name, string content, int size, Color color, TextAnchor align,
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
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = pos;
            return t;
        }

        static RectTransform MakeColumn(Transform parent, string name, float spacing, RectOffset padding,
                                        Vector2 anchor, Vector2 size, Vector2 pivot, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var v = go.GetComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childControlWidth = true;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.UpperLeft;
            return rt;
        }

        static Button MakeButton(Transform parent, string label, Vector2 pos, float width, UnityEngine.Events.UnityAction onClick, bool highlight = false)
        {
            var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = Color.white;   // 底色交给 ColorBlock，否则两者相乘会把按钮压成黑的
            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            var baseColor = highlight ? new Color(0.34f, 0.52f, 0.72f) : new Color(0.20f, 0.26f, 0.34f);
            colors.normalColor = baseColor;
            colors.highlightedColor = baseColor * 1.15f;
            colors.pressedColor = baseColor * 0.85f;
            colors.selectedColor = baseColor;
            colors.disabledColor = baseColor;
            btn.colors = colors;

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 44f;

            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, 44f);
            rt.anchoredPosition = pos;

            MakeText(go.transform, "Label", label, 20, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(width - 16f, 80f), new Vector2(0.5f, 0.5f), Vector2.zero);

            btn.onClick.AddListener(onClick);
            return btn;
        }

        static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }
    }
}
