// 生成 RunMap 场景（一境地图），并塞进 Build Settings。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RunMapSceneBuilder
{
    const string ScenePath = "Assets/Scenes/RunMap.unity";

    [MenuItem("灵境/生成 一境地图场景")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        camGo.tag = "MainCamera";

        var runner = new GameObject("RunMapRunner");
        runner.AddComponent<LingJing.UI.RunMapScreen>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Debug.Log("[RunMapSceneBuilder] 场景已生成：" + ScenePath);
    }

    static void AddToBuildSettings(string path)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}