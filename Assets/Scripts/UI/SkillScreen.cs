using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using LingJing.Data;
using LingJing.Battle;

namespace LingJing.UI
{
    /// <summary>
    /// 技能界面（v0.3）：左边选兽、中间是该兽的 8 个技能池（锁定/可学）、右边 4 个上阵格。
    /// 只做「装/卸 + 锁定状态」，技能升级（I/II/III）留到下一步。
    /// 改完写进 SkillLoadout（PlayerPrefs），战斗场景直接读。
    /// </summary>
    public class SkillScreen : MonoBehaviour
    {
        static readonly string[] BeastIds = { "beast_water", "beast_fire", "beast_wind", "beast_earth" };
        static readonly int PlayerLevel = 24;   // 一境满级视角：方便看到池子全貌

        static Font font;

        int selectedBeast;
        CreatureDefinition def;

        RectTransform beastCol;
        RectTransform listCol;
        RectTransform slotCol;
        Text tipText;

        void Start()
        {
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial" }, 24);

            // 第一次打开：谁都没配就按等级自动装满，免得一进来 4 格全空
            for (int i = 0; i < BeastIds.Length; i++)
            {
                var d = BattleDatabase.GetCreature(BeastIds[i]);
                if (d != null && SkillLoadout.IsEmpty(d.id)) SkillLoadout.AutoFill(d, PlayerLevel);
            }

            BuildStaticUi();
            Rebuild();
        }

        void BuildStaticUi()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var bg = MakePanel(canvasGo.transform, "Bg", new Color(0.07f, 0.08f, 0.10f, 1f),
                Vector2.zero, new Vector2(1920f, 1080f), new Vector2(0.5f, 0.5f));
            bg.raycastTarget = false;

            MakeText(canvasGo.transform, "Title", "技能　（每只兽 8 个技能池 · 上场只带 4 个 · 普攻不占格）",
                30, new Color(0.85f, 0.90f, 0.96f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(1400f, 44f), new Vector2(0f, 1f), new Vector2(60f, -30f));

            tipText = MakeText(canvasGo.transform, "Tip", "", 22, new Color(1f, 0.86f, 0.45f),
                TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(1200f, 34f), new Vector2(0f, 0f), new Vector2(60f, 40f));

            beastCol = MakeColumn(canvasGo.transform, "Beasts", new Vector2(0f, 1f), new Vector2(420f, 860f), new Vector2(60f, -110f));
            listCol = MakeColumn(canvasGo.transform, "List", new Vector2(0f, 1f), new Vector2(760f, 860f), new Vector2(510f, -110f));
            slotCol = MakeColumn(canvasGo.transform, "Slots", new Vector2(0f, 1f), new Vector2(560f, 860f), new Vector2(1300f, -110f));

            // 底部按钮
            var auto = MakeButton(canvasGo.transform, "AutoFill", "一键满配",
                new Vector2(0f, 0f), new Vector2(200f, 64f));
            SetAnchor(auto.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(60f, 90f));
            auto.onClick.AddListener(() =>
            {
                foreach (var id in BeastIds)
                {
                    var d = BattleDatabase.GetCreature(id);
                    if (d == null) continue;
                    for (int s = 0; s < 4; s++) SkillLoadout.Set(d.id, s, null);
                    SkillLoadout.Save();
                    SkillLoadout.AutoFill(d, PlayerLevel);
                }
                Flash("已按等级自动装满 4 格");
                Rebuild();
            });

            var clear = MakeButton(canvasGo.transform, "Clear", "全部清空",
                new Vector2(0f, 0f), new Vector2(200f, 64f));
            SetAnchor(clear.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(280f, 90f));
            clear.onClick.AddListener(() =>
            {
                foreach (var id in BeastIds)
                    for (int s = 0; s < 4; s++) SkillLoadout.Set(id, s, null);
                SkillLoadout.Save();
                Flash("4 格已清空");
                Rebuild();
            });

            var back = MakeButton(canvasGo.transform, "Back", "返回主菜单",
                new Vector2(0f, 0f), new Vector2(220f, 64f));
            var brt = back.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(1f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(1f, 0f);
            brt.anchoredPosition = new Vector2(-60f, 90f);
            back.onClick.AddListener(() =>
            {
                if (Application.CanStreamedLevelBeLoaded("MainMenu")) SceneManager.LoadScene("MainMenu");
                else Flash("MainMenu 没加进 Build Settings");
            });
        }

        // ------------------------------------------------------------------ 重建

        void Rebuild()
        {
            def = BattleDatabase.GetCreature(BeastIds[Mathf.Clamp(selectedBeast, 0, BeastIds.Length - 1)]);
            BuildBeastColumn();
            BuildSkillList();
            BuildSlots();
        }

        void BuildBeastColumn()
        {
            Clear(beastCol);
            for (int i = 0; i < BeastIds.Length; i++)
            {
                var d = BattleDatabase.GetCreature(BeastIds[i]);
                if (d == null) continue;
                bool sel = i == selectedBeast;
                var card = MakeButton(beastCol, "B" + i, "", Vector2.zero, new Vector2(400f, 132f));
                var img = card.GetComponent<Image>();
                img.color = sel ? new Color(0.22f, 0.30f, 0.42f, 1f) : new Color(0.15f, 0.17f, 0.21f, 1f);
                var colors = card.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(0.92f, 0.95f, 1f);
                colors.pressedColor = new Color(0.85f, 0.88f, 0.95f);
                colors.selectedColor = Color.white;
                colors.disabledColor = new Color(0.7f, 0.7f, 0.7f);
                card.colors = colors;
                int idx = i;
                card.onClick.AddListener(() => { selectedBeast = idx; Rebuild(); });

                var label = card.GetComponentInChildren<Text>();
                StretchLabel(label, 88f, 24);
                label.text = string.Format("{0}（{1}）\n技能点 {2}  ·  战力 {3}",
                    d.displayName, d.element, PlayerLevel / 4, Mathf.RoundToInt(PowerWithGear(d)));

                var icon = new GameObject("Icon", typeof(Image)).GetComponent<Image>();
                icon.transform.SetParent(card.transform, false);
                icon.sprite = LoadPortrait(d.id);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = icon.sprite != null;
                var irt = icon.rectTransform;
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(70f, 70f);
                irt.anchoredPosition = new Vector2(8f, 0f);
            }
        }

        void BuildSkillList()
        {
            Clear(listCol);
            if (def == null) return;
            var eq = SkillLoadout.Get(def.id);

            MakeText(listCol, "Hdr", string.Format("{0} 的技能池（{1}）", def.displayName, def.element),
                24, new Color(0.8f, 0.88f, 1f), TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(740f, 34f), new Vector2(0f, 1f), new Vector2(0f, -4f));

            var pool = def.skillPool;
            for (int i = 0; i < pool.Length; i++)
            {
                var sk = pool[i];
                if (sk == null) continue;
                bool unlocked = sk.unlockLevel <= PlayerLevel;
                bool equipped = false;
                for (int s = 0; s < 4; s++) if (eq[s] == sk.id) equipped = true;

                var row = MakeButton(listCol, "S" + i, "", Vector2.zero, new Vector2(740f, 68f));
                var img = row.GetComponent<Image>();
                img.color = equipped ? new Color(0.24f, 0.34f, 0.30f, 1f)
                    : (unlocked ? new Color(0.16f, 0.19f, 0.24f, 1f) : new Color(0.12f, 0.13f, 0.15f, 1f));
                row.interactable = unlocked;

                var label = row.GetComponentInChildren<Text>();
                StretchLabel(label, 84f, 22);
                string shape = ShapeTag(sk);
                string state = !unlocked ? string.Format("Lv{0} 解锁", sk.unlockLevel)
                    : (equipped ? "已上阵（点一下卸下）" : "点一下装上");
                label.text = string.Format("{0}{1}\n<{2}>{3}　{4}　MP{5}",
                    sk.displayName, equipped ? "  ✔" : "", shape, sk.tier == 1 ? "普攻" : "",
                    state, sk.spiritCost < 0 ? "+1" : sk.spiritCost.ToString());
                label.color = unlocked ? Color.white : new Color(0.55f, 0.58f, 0.62f);

                var icon = new GameObject("Icon", typeof(Image)).GetComponent<Image>();
                icon.transform.SetParent(row.transform, false);
                icon.sprite = LoadIcon(sk.id);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = icon.sprite != null;
                var irt = icon.rectTransform;
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(58f, 58f);
                irt.anchoredPosition = new Vector2(10f, 0f);

                string skId = sk.id;
                row.onClick.AddListener(() => OnSkillClicked(skId));
            }
        }

        void BuildSlots()
        {
            Clear(slotCol);
            if (def == null) return;
            var eq = SkillLoadout.Get(def.id);

            MakeText(slotCol, "Hdr", "上阵 4 格（点格子卸下）", 24, new Color(0.8f, 0.88f, 1f),
                TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(520f, 34f), new Vector2(0f, 1f), new Vector2(0f, -4f));

            for (int s = 0; s < 4; s++)
            {
                var sk = string.IsNullOrEmpty(eq[s]) ? null : BattleDatabase.GetSkill(eq[s]);
                var slot = MakeButton(slotCol, "Slot" + s, "", Vector2.zero, new Vector2(520f, 84f));
                slot.GetComponent<Image>().color = sk != null
                    ? new Color(0.20f, 0.26f, 0.34f, 1f) : new Color(0.12f, 0.13f, 0.16f, 1f);

                var label = slot.GetComponentInChildren<Text>();
                StretchLabel(label, 84f, 22);
                label.text = sk != null
                    ? string.Format("{0}　{1}\n<{2}>　MP{3}　冷却 {4}",
                        sk.displayName, ShapeTag(sk), sk.tier == 1 ? "普攻" : "上阵",
                        sk.spiritCost < 0 ? "+1" : sk.spiritCost.ToString(), sk.cooldown)
                    : "（空）";
                label.color = sk != null ? Color.white : new Color(0.55f, 0.58f, 0.62f);

                if (sk != null)
                {
                    var icon = new GameObject("Icon", typeof(Image)).GetComponent<Image>();
                    icon.transform.SetParent(slot.transform, false);
                    icon.sprite = LoadIcon(sk.id);
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    icon.enabled = icon.sprite != null;
                    var irt = icon.rectTransform;
                    irt.anchorMin = new Vector2(0f, 0.5f);
                    irt.anchorMax = new Vector2(0f, 0.5f);
                    irt.pivot = new Vector2(0f, 0.5f);
                    irt.sizeDelta = new Vector2(58f, 58f);
                    irt.anchoredPosition = new Vector2(10f, 0f);
                }

                int slotIdx = s;
                slot.onClick.AddListener(() =>
                {
                    SkillLoadout.Set(def.id, slotIdx, null);
                    SkillLoadout.Save();
                    Rebuild();
                });
            }

            // 参战提示：普攻永远在技能栏最左
            MakeText(slotCol, "Note",
                "普攻打 / 回蓝不占格：\n战斗中技能栏最左边固定是普攻。\n（技能升级 I/II/III 下一步做）",
                20, new Color(0.68f, 0.74f, 0.82f), TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(520f, 110f), new Vector2(0f, 1f), new Vector2(0f, -420f));
        }

        // ------------------------------------------------------------------ 交互

        void OnSkillClicked(string skillId)
        {
            if (def == null) return;
            var sk = BattleDatabase.GetSkill(skillId);
            if (sk == null || sk.unlockLevel > PlayerLevel) { Flash("还没到解锁等级"); return; }

            var eq = SkillLoadout.Get(def.id);
            for (int s = 0; s < 4; s++)
                if (eq[s] == skillId) { SkillLoadout.Set(def.id, s, null); SkillLoadout.Save(); Rebuild(); return; }

            if (sk.tier == 1) { Flash("普攻固定挂在技能栏最左，不占上阵格"); return; }

            int free = -1;
            for (int s = 0; s < 4; s++) if (string.IsNullOrEmpty(eq[s])) { free = s; break; }
            if (free < 0) { Flash("4 格已满，先点右边的格子卸下一个"); return; }

            // 奥义最多带 1 个
            if (sk.type == SkillType.Ultimate)
                for (int s = 0; s < 4; s++)
                {
                    var cur = string.IsNullOrEmpty(eq[s]) ? null : BattleDatabase.GetSkill(eq[s]);
                    if (cur != null && cur.type == SkillType.Ultimate) { Flash("奥义最多带 1 个"); return; }
                }

            SkillLoadout.Set(def.id, free, skillId);
            SkillLoadout.Save();
            Rebuild();
        }

        void Flash(string msg) { if (tipText != null) tipText.text = msg; }

        // ------------------------------------------------------------------ 小工具


        /// <summary>把按钮自带的 Label 拉成整块，左侧留出图标位，避免文字被挤成几行。</summary>
        static void StretchLabel(Text label, float leftPad, int fontSize)
        {
            label.fontSize = fontSize;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var rt = label.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(leftPad, 6f);
            rt.offsetMax = new Vector2(-12f, -6f);
        }

        static string ShapeTag(SkillDefinition sk)
        {
            if (sk.isHeal) return "治疗";
            if (sk.isShield) return "护盾";
            if (sk.targetSide == TargetSide.AllEnemies) return "全体";
            if (sk.targetSide == TargetSide.AllAllies || sk.targetSide == TargetSide.Self) return "增益";
            return sk.multiplier > 0f ? "单体" : "辅助";
        }

        static float PowerWithGear(CreatureDefinition d)
        {
            var preset = EquipmentPresets.Get(d.id);
            var stats = BattleFormulas.PlayerStats(d.baseStats, PlayerLevel, d.potentials, FreePoints());
            float eqPower = 0f;
            if (preset != null && !preset.IsEmpty())
            {
                var loadout = preset;
                loadout.Apply(stats);
                eqPower = loadout.Power();
            }
            return BattleFormulas.Power(stats, PlayerLevel, eqPower);
        }

        static int[] FreePoints()
        {
            int per = (PlayerLevel - 1) * 6 / 6;
            return new[] { per, per, per, per, per, per };
        }

        static Sprite LoadPortrait(string id) { return Resources.Load<Sprite>("Art/Creatures/" + id); }

        static Sprite LoadIcon(string id)
        {
            var s = Resources.Load<Sprite>("Art/Icons/Skills/" + id);
            if (s == null) s = Resources.Load<Sprite>("Art/Icons/Status/" + id);
            return s;
        }

        static void SetAnchor(RectTransform rt, Vector2 anchor, Vector2 pos)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = pos;
        }

        static void Clear(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        }

        static RectTransform MakeColumn(Transform parent, string name, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var v = go.GetComponent<VerticalLayoutGroup>();
            v.spacing = 10f;
            v.childControlHeight = false;
            v.childControlWidth = false;
            v.childForceExpandHeight = false;
            v.childForceExpandWidth = false;
            v.childAlignment = TextAnchor.UpperLeft;
            return rt;
        }

        static Image MakePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 size, Vector2 anchoredPos)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            var rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMin;
            rt.pivot = anchorMin;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            return img;
        }

        static Text MakeText(Transform parent, string name, string content, int size, Color color,
            TextAnchor align, Vector2 anchor, Vector2 sizeDelta, Vector2 pivot, Vector2 pos)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.text = content;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = pos;
            return t;
        }

        static Button MakeButton(Transform parent, string name, string label, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.22f, 0.28f, 0.36f, 1f);
            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.95f, 1f);
            colors.pressedColor = new Color(0.85f, 0.88f, 0.95f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f);
            btn.colors = colors;
            btn.targetGraphic = img;

            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            MakeText(go.transform, "Label", label, 22, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), size, new Vector2(0.5f, 0.5f), Vector2.zero);
            return btn;
        }
    }
}
