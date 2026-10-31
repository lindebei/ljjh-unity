using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using LingJing.Data;

/// <summary>
/// CSV → ScriptableObject 导入器。
/// 唯一真源是 Assets/Data/*.csv（跟 E:\TT\sim\ 的 Python 吃同一份数值），
/// Assets/Resources/BattleData/ 下的 .asset 全部是生成物，手改会被下次导入覆盖。
/// 命令行：Unity.exe -batchmode -quit -projectPath ... -executeMethod BattleCsvImporter.ImportAll
/// </summary>
public static class BattleCsvImporter
{
    const string CsvDir = "Assets/Data";
    const string OutRoot = "Assets/Resources/BattleData";
    const string SkillsDir = OutRoot + "/Skills";
    const string CreaturesDir = OutRoot + "/Creatures";
    const string SetsDir = OutRoot + "/EnemySkillSets";
    const string EquipDir = OutRoot + "/Equipment";

    [MenuItem("灵境/导入战斗数据 (CSV → SO)")]
    public static void ImportAll()
    {
        EnsureFolders();

        var skills = ImportSkills();
        ImportCreatures(skills);
        ImportEnemySkillSets(skills);
        ImportBattleConfig();
        int equipCount = ImportEquipment();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(string.Format("[BattleCsvImporter] 导入完成：{0} 个技能、{1} 张兽/怪、4 组敌方技能、1 份配置、{2} 件装备。", skills.Count, CountAssets<CreatureDefinition>(CreaturesDir), equipCount));
    }

    // ------------------------------------------------------------------ 装备

    /// <summary>
    /// equipment.csv 的主值只是「1 级基础值」，品质系数（1.0/1.3/1.6/2.0/2.6）在这里乘进去；
    /// 强化系数（+8%/级）留到运行时 `MainValueAt(enhance)` 再乘，这样同一件装备强化升级不用重导。
    /// </summary>
    static int ImportEquipment()
    {
        string path = Path.Combine(CsvDir, "equipment.csv");
        if (!File.Exists(path)) return 0;
        var rows = ReadCsv(path);

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string id = Get(r, "id");
            if (string.IsNullOrEmpty(id)) continue;

            var so = Upsert<EquipmentDefinition>(EquipDir, id);
            so.id = id;
            so.displayName = Get(r, "name");
            so.slot = (EquipSlot)Mathf.Clamp(GetInt(r, "slot"), 0, 4);
            so.quality = Mathf.Clamp(GetInt(r, "quality"), 0, 4);
            so.mainStat = ParseEnum<StatKind>(Get(r, "mainStat"), StatKind.Atk);
            so.mainValue = GetFloat(r, "mainValue") * EquipmentDefinition.QualityCoeff[so.quality];
            so.enhance = Mathf.Clamp(GetInt(r, "enhance"), 0, 5);
            so.spiritMaxBonus = 0;
            so.subs = new List<StatMod>();

            string[] keys = { "sub1", "sub2", "sub3", "sub4" };
            for (int k = 0; k < keys.Length; k++)
            {
                string raw = Get(r, keys[k]);
                if (string.IsNullOrEmpty(raw)) continue;
                int colon = raw.IndexOf(':');
                if (colon <= 0) continue;
                string key = raw.Substring(0, colon).Trim();
                float val = 0f;
                float.TryParse(raw.Substring(colon + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out val);
                if (key == "spiritFlat") { so.spiritMaxBonus += Mathf.RoundToInt(val); continue; }
                StatKind sk;
                switch (key)
                {
                    case "atkPct": sk = StatKind.Atk; break;
                    case "defPct": sk = StatKind.Def; break;
                    case "spdPct": sk = StatKind.Spd; break;
                    case "vitPct": sk = StatKind.Vit; break;
                    case "sprPct": sk = StatKind.Spr; break;
                    case "talPct": sk = StatKind.Tal; break;
                    default: continue;
                }
                so.subs.Add(new StatMod { stat = sk, percent = val });
            }
            EditorUtility.SetDirty(so);
        }
        return CountAssets<EquipmentDefinition>(EquipDir);
    }

    // ------------------------------------------------------------------ 技能

    static Dictionary<string, SkillDefinition> ImportSkills()
    {
        var rows = ReadCsv(Path.Combine(CsvDir, "skills.csv"));
        var map = new Dictionary<string, SkillDefinition>();

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            string id = Get(r, "id");
            if (string.IsNullOrEmpty(id)) continue;

            var so = Upsert<SkillDefinition>(SkillsDir, id);
            so.id = id;
            so.displayName = Get(r, "name");
            so.type = ParseEnum<SkillType>(Get(r, "type"), SkillType.Basic);
            so.element = ParseEnum<Element>(Get(r, "element"), Element.Water);
            so.spiritCost = GetInt(r, "cost");
            so.cooldown = GetInt(r, "cooldown");
            so.multiplier = GetFloat(r, "mult");
            so.segments = Mathf.Max(1, GetInt(r, "segments", 1));
            so.spreadSegments = GetBool(r, "spread");
            so.targetSide = ParseEnum<TargetSide>(Get(r, "target"), TargetSide.Enemy);
            so.appliedStatus = ParseEnum<StatusType>(Get(r, "status"), StatusType.None);
            so.dotStacks = GetInt(r, "stacks");
            so.controlChance = GetFloat(r, "ctrlChance");
            so.controlDuration = GetInt(r, "ctrlDur");
            so.gaugePushbackPercent = GetFloat(r, "pushback");
            so.taunt = GetBool(r, "taunt");
            so.selfAppliesStatus = GetBool(r, "selfStatus");
            so.isHeal = GetBool(r, "isHeal");
            so.isShield = GetBool(r, "isShield");
            so.shieldPercent = GetFloat(r, "shieldPct");
            so.dispelOne = GetBool(r, "dispel");
            so.healAllAllies = GetBool(r, "healAll");
            so.buffPercent = GetFloat(r, "buffPct");
            so.buffDuration = GetInt(r, "buffDur");
            so.buffIsAttack = GetBool(r, "buffAtk");
            EditorUtility.SetDirty(so);

            map[id] = so;
        }
        return map;
    }

    // ------------------------------------------------------------------ 兽 / 怪

    static void ImportCreatures(Dictionary<string, SkillDefinition> skills)
    {
        ImportBeasts(skills);
        ImportEnemies();
    }

    static void ImportBeasts(Dictionary<string, SkillDefinition> skills)
    {
        foreach (var r in ReadCsv(Path.Combine(CsvDir, "beasts.csv")))
        {
            string id = Get(r, "id");
            if (string.IsNullOrEmpty(id)) continue;

            var so = Upsert<CreatureDefinition>(CreaturesDir, id);
            so.id = id;
            so.displayName = Get(r, "name");
            so.element = ParseEnum<Element>(Get(r, "element"), Element.Water);
            so.isPlayerBeast = true;
            so.quality = Quality.Normal;
            so.baseStats = new SixStats(GetInt(r, "atk"), GetInt(r, "def"), GetInt(r, "spd"),
                                        GetInt(r, "vit"), GetInt(r, "spr"), GetInt(r, "tal"));
            so.potentials = new SixStatsF(GetFloat(r, "pAtk"), GetFloat(r, "pDef"), GetFloat(r, "pSpd"),
                                          GetFloat(r, "pVit"), GetFloat(r, "pSpr"), GetFloat(r, "pTal"));

            so.skills = new SkillDefinition[4];
            so.skills[(int)SkillType.Basic] = Lookup(skills, Get(r, "sBasic"));
            so.skills[(int)SkillType.Active] = Lookup(skills, Get(r, "sActive"));
            so.skills[(int)SkillType.Support] = Lookup(skills, Get(r, "sSupport"));
            so.skills[(int)SkillType.Ultimate] = Lookup(skills, Get(r, "sUlt"));
            EditorUtility.SetDirty(so);
        }
    }

    static void ImportEnemies()
    {
        foreach (var r in ReadCsv(Path.Combine(CsvDir, "enemies.csv")))
        {
            string id = Get(r, "id");
            if (string.IsNullOrEmpty(id)) continue;

            var so = Upsert<CreatureDefinition>(CreaturesDir, id);
            so.id = id;
            so.displayName = Get(r, "name");
            so.element = ParseEnum<Element>(Get(r, "element"), Element.Water);
            so.isPlayerBeast = false;
            so.quality = ParseEnum<Quality>(Get(r, "quality"), Quality.Normal);
            so.baseStats = new SixStats(GetInt(r, "atk"), GetInt(r, "def"), GetInt(r, "spd"),
                                        GetInt(r, "vit"), GetInt(r, "spr"), GetInt(r, "tal"));
            so.potentials = new SixStatsF(1f, 1f, 1f, 1f, 1f, 1f);   // 怪物不用潜力
            so.skills = new SkillDefinition[4];
            so.enemySkillSet = Get(r, "element");
            EditorUtility.SetDirty(so);
        }
    }

    // ------------------------------------------------------------------ 敌方技能组

    static void ImportEnemySkillSets(Dictionary<string, SkillDefinition> skills)
    {
        foreach (var r in ReadCsv(Path.Combine(CsvDir, "enemy_skillsets.csv")))
        {
            string el = Get(r, "element");
            if (string.IsNullOrEmpty(el)) continue;

            var so = Upsert<EnemySkillSet>(SetsDir, "set_" + el.ToLowerInvariant());
            so.element = ParseEnum<Element>(el, Element.Water);
            so.basic = Lookup(skills, Get(r, "basic"));
            so.active = Lookup(skills, Get(r, "active"));
            so.aoe = Lookup(skills, Get(r, "aoe"));
            so.ultimate = Lookup(skills, Get(r, "ultimate"));
            so.ultimateUnlockHpPercent = GetFloat(r, "ultUnlock", 0.30f);
            so.ultimateCooldown = GetInt(r, "ultCd", 4);
            so.activeChanceWhenReady = GetFloat(r, "activeChance", 0.60f);
            so.aoeChanceWhenReady = GetFloat(r, "aoeChance", 0.70f);
            EditorUtility.SetDirty(so);
        }
    }

    // ------------------------------------------------------------------ 全局配置

    static void ImportBattleConfig()
    {
        var rows = ReadCsv(Path.Combine(CsvDir, "battle_config.csv"));
        var cfg = Upsert<BattleConfig>(OutRoot, "BattleConfig");
        var map = new Dictionary<string, string>();
        for (int i = 0; i < rows.Count; i++)
            map[Get(rows[i], "key")] = Get(rows[i], "value");

        cfg.attackK = GetFloat(map, "attackK", cfg.attackK);
        cfg.defConstant = GetFloat(map, "defConstant", cfg.defConstant);
        cfg.elementalAdvantage = GetFloat(map, "elementalAdvantage", cfg.elementalAdvantage);
        cfg.elementalDisadvantage = GetFloat(map, "elementalDisadvantage", cfg.elementalDisadvantage);
        cfg.useElementalAdvantage = GetBool(map, "useElementalAdvantage", cfg.useElementalAdvantage);
        cfg.useExpectedValue = GetBool(map, "useExpectedValue", cfg.useExpectedValue);
        cfg.damageVariance = GetFloat(map, "damageVariance", cfg.damageVariance);
        cfg.dotMaxStacks = GetInt(map, "dotMaxStacks", cfg.dotMaxStacks);
        cfg.dotPercentPerStack = GetFloat(map, "dotPercentPerStack", cfg.dotPercentPerStack);
        cfg.dotDuration = GetInt(map, "dotDuration", cfg.dotDuration);
        cfg.spiritMax = GetInt(map, "spiritMax", cfg.spiritMax);
        cfg.spiritRegenEveryRounds = GetInt(map, "spiritRegenEveryRounds", cfg.spiritRegenEveryRounds);
        cfg.tickSeconds = GetFloat(map, "tickSeconds", cfg.tickSeconds);
        cfg.gaugeGainPerTick = GetFloat(map, "gaugeGainPerTick", cfg.gaugeGainPerTick);
        cfg.gaugeThreshold = GetFloat(map, "gaugeThreshold", cfg.gaugeThreshold);
        cfg.potionsPerBattle = GetInt(map, "potionsPerBattle", cfg.potionsPerBattle);
        cfg.potionHealPercent = GetFloat(map, "potionHealPercent", cfg.potionHealPercent);
        cfg.potionTriggerHpPercent = GetFloat(map, "potionTriggerHpPercent", cfg.potionTriggerHpPercent);
        cfg.playerActionsPerRound = GetInt(map, "playerActionsPerRound", cfg.playerActionsPerRound);
        cfg.healTalentConstant = GetFloat(map, "healTalentConstant", cfg.healTalentConstant);
        cfg.healSecondRatio = GetFloat(map, "healSecondRatio", cfg.healSecondRatio);
        cfg.timeCapSeconds = GetFloat(map, "timeCapSeconds", cfg.timeCapSeconds);
        cfg.enemyPowerScale = GetFloat(map, "enemyPowerScale", cfg.enemyPowerScale);
        if (map.ContainsKey("qualityPowerScale"))
        {
            cfg.qualityPowerScale = GetFloatArray(map["qualityPowerScale"], cfg.qualityPowerScale);
        }
        cfg.waveClearHealPercent = GetFloat(map, "waveClearHealPercent", cfg.waveClearHealPercent);
        cfg.waveClearSpirit = GetInt(map, "waveClearSpirit", cfg.waveClearSpirit);
        EditorUtility.SetDirty(cfg);
    }

    // ------------------------------------------------------------------ 基础设施

    static void EnsureFolders()
    {
        EnsureFolder("Assets", "Resources");
        EnsureFolder("Assets/Resources", "BattleData");
        EnsureFolder(OutRoot, "Skills");
        EnsureFolder(OutRoot, "Creatures");
        EnsureFolder(OutRoot, "EnemySkillSets");
        EnsureFolder(OutRoot, "Equipment");
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }

    static T Upsert<T>(string dir, string fileName) where T : ScriptableObject
    {
        string path = dir + "/" + fileName + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }

    static int CountAssets<T>(string dir) where T : UnityEngine.Object
    {
        var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { dir });
        return guids.Length;
    }

    static SkillDefinition Lookup(Dictionary<string, SkillDefinition> map, string key)
    {
        SkillDefinition s;
        return map.TryGetValue(key, out s) ? s : null;
    }

    static float[] GetFloatArray(string raw, float[] fallback)
    {
        if (string.IsNullOrEmpty(raw)) return fallback;
        var parts = raw.Split('|');
        var arr = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            float v;
            if (!float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v)) return fallback;
            arr[i] = v;
        }
        return arr;
    }
    static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var list = new List<Dictionary<string, string>>();
        if (!File.Exists(path))
        {
            Debug.LogError("[BattleCsvImporter] 找不到 " + path);
            return list;
        }

        var lines = File.ReadAllLines(path);
        if (lines.Length == 0) return list;

        var header = SplitLine(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i].Trim())) continue;
            var cells = SplitLine(lines[i]);
            var row = new Dictionary<string, string>();
            for (int c = 0; c < header.Length; c++)
                row[header[c].Trim()] = c < cells.Length ? cells[c].Trim() : string.Empty;
            list.Add(row);
        }
        return list;
    }

    static string[] SplitLine(string line)
    {
        // 数据里没有带逗号的字段，直接用简单切分，省掉一个 CSV 库
        return line.Split(',');
    }

    static string Get(Dictionary<string, string> row, string key)
    {
        string v;
        return row.TryGetValue(key, out v) ? v : string.Empty;
    }

    static float GetFloat(Dictionary<string, string> row, string key, float fallback = 0f)
    {
        float v;
        string s = Get(row, key);
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
    }

    static int GetInt(Dictionary<string, string> row, string key, int fallback = 0)
    {
        int v;
        string s = Get(row, key);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
    }

    static bool GetBool(Dictionary<string, string> row, string key, bool fallback = false)
    {
        string s = Get(row, key).ToLowerInvariant();
        if (s == "1" || s == "true" || s == "yes") return true;
        if (s == "0" || s == "false" || s == "no") return false;
        return fallback;
    }

    static T ParseEnum<T>(string s, T fallback) where T : struct
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        try { return (T)Enum.Parse(typeof(T), s, true); }
        catch { return fallback; }
    }
}
