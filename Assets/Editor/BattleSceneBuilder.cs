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

    static void AddToBuildSettings()
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        for (int i = 0; i < scenes.Count; i++)
            if (scenes[i].path == ScenePath) return;

        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
