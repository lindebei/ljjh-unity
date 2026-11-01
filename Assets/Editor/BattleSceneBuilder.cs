using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LingJing.UI;

/// <summary>
/// 生成 BattlePrototype 场景：一个相机 + 一个 BattleRunner（挂 BattleHud，界面全代码搭）。
/// 命令行：Unity.exe -batchmode -quit -projectPath D:\ljjh\unity -executeMethod BattleSceneBuilder.BuildScene
/// </summary>
public static class BattleSceneBuilder
{
    const string ScenePath = "Assets/Scenes/BattlePrototype.unity";
    const string WaveScenePath = "Assets/Scenes/BattleWaves.unity";

    [MenuItem("灵境/生成 BattlePrototype 场景")]
    public static void BuildScene()
    {
        BuildInto(ScenePath, new[] { "en_bonefish", "en_stonelizard", "en_bird" }, null);
    }

    [MenuItem("灵境/生成 多波战斗场景 (4+3)")]
    public static void BuildWaveScene()
    {
        // 波次演示：一波清完不判胜利，直接入下一波（一境战斗流程文档 WaveEnter）
        BuildInto(WaveScenePath,
            new[] { "en_bonefish", "en_stonelizard", "en_rat", "en_bear" },
            new[] { "en_firetiger", "en_graywolf", "en_bonefish" });
    }

    const string BossScenePath = "Assets/Scenes/BattleBoss.unity";

    [MenuItem("灵境/生成 Boss 演示场景 (2 普 + 1 小Boss)")]
    public static void BuildBossScene()
    {
        // Boss 演示：用来看 Boss 名字金色标记与「大一号」的观感（v0.1 验收第 4 条）
        // 单波放一起，否则 Boss 要等第一波清完才入场，截图里看不到金色名字
        BuildInto(BossScenePath,
            new[] { "en_bonefish", "en_stonelizard", "en_jelly" },   // 小 Boss 带着两个小跟班（Boss 不会三档同场）
            null);
    }

    const string EquipScenePath = "Assets/Scenes/EquipScreen.unity";

    [MenuItem("灵境/生成 换装界面场景")]
    public static void BuildEquipScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EquipScenePath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        new GameObject("EquipScreen", typeof(EquipScreen));

        EditorSceneManager.SaveScene(scene, EquipScenePath);
        AddToBuildSettings(EquipScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BattleSceneBuilder] 场景已生成：" + EquipScenePath);
    }

    const string MenuScenePath = "Assets/Scenes/MainMenu.unity";

    [MenuItem("灵境/生成 主菜单场景")]
    public static void BuildMenuScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MenuScenePath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 1f);
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        new GameObject("MainMenu", typeof(LingJing.UI.MainMenu));

        EditorSceneManager.SaveScene(scene, MenuScenePath);

        // 主菜单必须排在 Build Settings 第一位，否则打包启动时不会进菜单
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == MenuScenePath);
        list.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
        foreach (var p in new[] { ScenePath, WaveScenePath, BossScenePath, EquipScenePath })
        {
            if (list.Exists(s => s.path == p)) continue;
            list.Add(new EditorBuildSettingsScene(p, true));
        }
        EditorBuildSettings.scenes = list.ToArray();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BattleSceneBuilder] 场景已生成：" + MenuScenePath + "，Build Settings 共 " + list.Count + " 个场景");
    }

    static void BuildInto(string path, string[] wave1, string[] wave2)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        var runner = new GameObject("BattleRunner", typeof(BattleHud));
        var hud = runner.GetComponent<BattleHud>();
        hud.playerLevel = 24;
        hud.enemyLevel = 10;
        hud.enemyIds = wave1;
        hud.enemyIdsWave2 = wave2 ?? new string[0];
        hud.timeScale = 1f;
        hud.autoBattle = false;

        EditorSceneManager.SaveScene(scene, path);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BattleSceneBuilder] 场景已生成：" + path);
    }

    // 随机遭遇场景：用 EncounterGenerator 现场配敌人（12 只普通怪都会出场）
    public const string EncounterScenePath = "Assets/Scenes/BattleEncounter.unity";

    [MenuItem("灵境/生成 BattleEncounter 场景")]
    public static void BuildEncounterScene()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EncounterScenePath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
        cam.orthographic = true;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        var runner = new GameObject("BattleRunner", typeof(BattleHud));
        var hud = runner.GetComponent<BattleHud>();
        hud.playerLevel = 24;
        hud.enemyLevel = 24;
        hud.enemyIds = new string[0];
        hud.enemyIdsWave2 = new string[0];
        hud.timeScale = 1f;
        hud.autoBattle = false;
        hud.useRandomEncounter = true;
        hud.encounterSeed = 20261101;    // 固定种子，截图可复现

        EditorSceneManager.SaveScene(scene, EncounterScenePath);
        AddToBuildSettings(EncounterScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BattleSceneBuilder] 场景已生成：" + EncounterScenePath);
    }

    static void AddToBuildSettings() { AddToBuildSettings(ScenePath); }

    static void AddToBuildSettings(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
            if (scenes[i].path == path) return;

        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

        [MenuItem("灵境/生成 SkillScreen 场景")]
        public static void BuildSkillScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera));
            var c = cam.GetComponent<Camera>();
            c.orthographic = true;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            cam.transform.position = new Vector3(0f, 0f, -10f);

            var go = new GameObject("SkillScreen");
            go.AddComponent<LingJing.UI.SkillScreen>();

            const string path = "Assets/Scenes/SkillScreen.unity";
            EditorSceneManager.SaveScene(scene, path);
            AddToBuildSettings(path);
            Debug.Log("[BattleSceneBuilder] 场景已生成：" + path);
        }
}
