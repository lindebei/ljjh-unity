// 一键进 Play：打开战斗场景、进入播放模式，并把 Game 视图最大化（方便人眼看/试玩）。
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DevLaunch
{
    [MenuItem("灵境/直接开打（进入 Play）")]
    public static void Run()
    {
        Launch("Assets/Scenes/BattlePrototype.unity");
    }

    /// <summary>直接进一境地图（游戏真正的开头）。</summary>
    [MenuItem("灵境/直接进一境地图（进入 Play）")]
    public static void RunExplore()
    {
        LingJing.Battle.RunState.Reset();
        Launch("Assets/Scenes/Explore.unity");
    }

    /// <summary>从主菜单进游戏（能点「一境探索」进地图）。</summary>
    [MenuItem("灵境/从主菜单开打（进入 Play）")]
    public static void RunMenu()
    {
        Launch("Assets/Scenes/MainMenu.unity");
    }

    static void Launch(string scene)
    {
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