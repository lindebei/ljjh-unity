// 一键进 Play：打开战斗场景、进入播放模式，并把 Game 视图最大化（方便人眼看/试玩）。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DevLaunch
{
    [MenuItem("灵境/直接开打（进入 Play）")]
    public static void Run()
    {
        const string scene = "Assets/Scenes/BattlePrototype.unity";
        if (System.IO.File.Exists(scene)) EditorSceneManager.OpenScene(scene);
        EditorApplication.delayCall += () =>
        {
            EditorApplication.isPlaying = true;
            EditorApplication.delayCall += MaximizeGameView;
        };
        Debug.Log("[DevLaunch] 已请求进入 Play 模式，场景=" + scene);
    }

    static void MaximizeGameView()
    {
        var t = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (t == null) return;
        var w = EditorWindow.GetWindow(t);
        if (w != null) w.maximized = true;
    }
}